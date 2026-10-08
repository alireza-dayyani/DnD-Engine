param([int]$Port = 5528)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runDirectory = Join-Path $repoRoot ('artifacts/encounter-smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$null = New-Item -ItemType Directory -Path $runDirectory -Force
$dataDirectory = Join-Path $runDirectory 'data'
$assembly = Join-Path $repoRoot 'src/DndEngine.Api/bin/Debug/net10.0/DndEngine.Api.dll'
if (!(Test-Path -LiteralPath $assembly)) { throw 'Build the solution before running the smoke test.' }
$baseUrl = "http://127.0.0.1:$Port"
$engineProcess = $null
function Start-Engine([string]$label) {
    $script:engineProcess = Start-Process -FilePath 'dotnet' -ArgumentList @("`"$assembly`"", '--urls', $baseUrl, '--DataDirectory', "`"$dataDirectory`"") -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runDirectory "$label.stdout.log") -RedirectStandardError (Join-Path $runDirectory "$label.stderr.log")
    for ($attempt = 0; $attempt -lt 80; $attempt++) {
        if ($script:engineProcess.HasExited) { throw "API exited; inspect $runDirectory" }
        try { $null = Invoke-RestMethod "$baseUrl/health"; return } catch { Start-Sleep -Milliseconds 250 }
    }
    throw 'API did not become ready.'
}
function Stop-Engine {
    if ($script:engineProcess -and !$script:engineProcess.HasExited) {
        Stop-Process -Id $script:engineProcess.Id
        $script:engineProcess.WaitForExit()
    }
}
function Post([string]$route, $body, [string]$operationId = '') {
    $headers = @{}
    if ($operationId) { $headers['X-Operation-Id'] = $operationId }
    Invoke-RestMethod -Uri "$baseUrl$route" -Method Post -ContentType 'application/json' -Headers $headers -Body ($body | ConvertTo-Json -Depth 40)
}
function PostRaw([string]$route, [string]$bodyJson, [string]$operationId) {
    Invoke-RestMethod -Uri "$baseUrl$route" -Method Post -ContentType 'application/json' -Headers @{'X-Operation-Id'=$operationId} -Body $bodyJson
}
function Assert-True([bool]$condition, [string]$message) { if (!$condition) { throw $message } }
try {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try { $probe.Start() } finally { $probe.Stop() }
    Start-Engine 'first'
    $campaign = Post '/campaigns' @{name='Phase 5 encounter smoke'}
    $hero = Post '/characters' @{campaignId=$campaign.id;name='Guardian';level=1;maximumHp=1000;armorClass=20;abilities=@{Strength=20;Dexterity=16;Constitution=20;Intelligence=10;Wisdom=10;Charisma=10};skillProficiencies=@();savingThrowProficiencies=@()}
    $null = Invoke-RestMethod -Uri "$baseUrl/characters/$($hero.id)/combat-profile" -Method Put -ContentType 'application/json' -Body (@{speed=30;weaponProficiencies=@('mace');resistances=@();immunities=@();vulnerabilities=@();conditionImmunities=@()} | ConvertTo-Json -Depth 20)
    $mace = Post "/characters/$($hero.id)/weapons" @{definitionId='mace'}
    $fighter = Post '/srd-characters' @{campaignId=$campaign.id;name='Mira';speciesId='dwarf';speciesVariantId=$null;size='Medium';backgroundId='criminal';classId='fighter';baseAbilities=@{Strength=15;Dexterity=14;Constitution=14;Intelligence=10;Wisdom=12;Charisma=8};backgroundBonuses=@{Dexterity=2;Constitution=1};classSkills=@('athletics','perception');startingItemIds=@('chain-mail','greatsword');masteredWeaponIds=@('greatsword');fightingStyleFeat='defense'}
    $fighterInventory = Invoke-RestMethod "$baseUrl/characters/$($fighter.id)/inventory/state"
    $fighterInventory = Post "/characters/$($fighter.id)/inventory/items" @{definitionId='potion-of-healing';quantity=1;expectedRevision=$fighterInventory.revision} ([guid]::NewGuid().ToString())
    $potion = @($fighterInventory.items | Where-Object definitionId -eq 'potion-of-healing')[0]
    $null = Post "/characters/$($fighter.id)/damage" @{amount=3}
    $monsters = @()
    foreach ($definition in @('skeleton','skeleton','priest-acolyte')) {
        $monsters += Post '/monsters' @{campaignId=$campaign.id;definitionId=$definition} ([guid]::NewGuid().ToString())
    }
    $priest = $monsters[2]
    $null = Post "/characters/$($priest.instance.id)/damage" @{amount=4}
    $encounter = Post "/campaigns/$($campaign.id)/combat" @{name='Tomb chamber'}
    $heroMember = (Post "/combat/$($encounter.id)/combatants" @{characterId=$hero.id}).result
    $fighterMember = (Post "/combat/$($encounter.id)/combatants" @{characterId=$fighter.id}).result
    $memberByMonster = @{}
    foreach ($monster in $monsters) {
        $member = (Post "/combat/$($encounter.id)/monsters" @{monsterId=$monster.instance.id} ([guid]::NewGuid().ToString())).result
        $memberByMonster[$monster.instance.id] = $member
    }
    $initiative = Post "/combat/$($encounter.id)/initiative" @{}
    $order = @($initiative.result.combatants | Sort-Object @{Expression={$_.initiative.total};Descending=$true},id | ForEach-Object {$_.id})
    $null = Post "/combat/$($encounter.id)/start" @{order=$order}
    $spellCast = $false; $itemUsed = $false; $attacks = 0
    for ($turn = 0; $turn -lt 160; $turn++) {
        $view = Invoke-RestMethod "$baseUrl/combat/$($encounter.id)"
        $remaining = @($monsters | Where-Object { $monsterId = $_.instance.id; -not (@($view.characters | Where-Object id -eq $monsterId)[0].health.dead) })
        if ($remaining.Count -eq 0 -and $spellCast -and $itemUsed) { break }
        $actor = @($view.encounter.combatants | Where-Object id -eq $view.currentCombatantId)[0]
        if ($actor.id -eq $heroMember.id -and $remaining.Count -gt 0) {
            $target = $memberByMonster[$remaining[0].instance.id]
            $null = Post "/combat/$($encounter.id)/attack" @{combatantId=$actor.id;attack=@{targetId=$target.id;weaponId=$mace.id;mode='Melee';context=@{distanceFeet=5;attackerCanSeeTarget=$true;targetCanSeeAttacker=$true}}}
            $attacks++
        }
        elseif ($actor.id -eq $fighterMember.id -and !$itemUsed) {
            $null = Post "/combat/$($encounter.id)/items/use" @{combatantId=$actor.id;itemId=$potion.id;targetCombatantId=$actor.id;distanceFeet=0;expectedRevision=$view.encounter.revision} ([guid]::NewGuid().ToString())
            $itemUsed = $true
        }
        elseif ($actor.id -eq $memberByMonster[$priest.instance.id].id -and !$spellCast) {
            $null = Post "/combat/$($encounter.id)/monsters/spells/cast" @{combatantId=$actor.id;spellId='healing-word';targets=@(@{combatantId=$actor.id;distanceFeet=0;casterCanSeeTarget=$true});verbalAvailable=$true;somaticAvailable=$true;materialAvailable=$true;expectedRevision=$view.encounter.revision} ([guid]::NewGuid().ToString())
            $spellCast = $true
        }
        $null = Post "/combat/$($encounter.id)/end-turn" @{combatantId=$actor.id}
    }
    $beforeCompletion = Invoke-RestMethod "$baseUrl/combat/$($encounter.id)"
    Assert-True ($spellCast -and $itemUsed -and $attacks -gt 0) 'Weapon, spell, or item action was not exercised.'
    foreach ($monster in $monsters) {
        $state = @($beforeCompletion.characters | Where-Object id -eq $monster.instance.id)[0]
        Assert-True $state.health.dead "Monster $($monster.instance.id) was not defeated."
    }
    $completed = Post "/combat/$($encounter.id)/complete" @{outcome='Victory';expectedRevision=$beforeCompletion.encounter.revision} ([guid]::NewGuid().ToString())
    Assert-True ($completed.result.status -eq 'Completed') 'Encounter did not complete.'
    $rewards = Invoke-RestMethod "$baseUrl/combat/$($encounter.id)/rewards"
    Assert-True ($rewards.rewards.availableExperience -gt 0) 'No defeated-monster XP was recorded.'
    $xpOperation = [guid]::NewGuid().ToString()
    $xpBody = @{awards=@(@{characterId=$fighter.id;amount=$rewards.rewards.availableExperience});expectedRevision=$rewards.rewards.revision} | ConvertTo-Json -Depth 20
    $award = PostRaw "/combat/$($encounter.id)/rewards/experience" $xpBody $xpOperation
    $lootMonster = $monsters[0]
    $monsterInventory = Invoke-RestMethod "$baseUrl/characters/$($lootMonster.instance.id)/inventory/state"
    $lootItem = @($monsterInventory.items | Where-Object definitionId -eq 'shortsword')[0]
    $fighterInventory = Invoke-RestMethod "$baseUrl/characters/$($fighter.id)/inventory/state"
    $null = Post "/combat/$($encounter.id)/rewards/loot" @{monsterId=$lootMonster.instance.id;recipientId=$fighter.id;itemId=$lootItem.id;quantity=1;expectedMonsterRevision=$monsterInventory.revision;expectedRecipientRevision=$fighterInventory.revision} ([guid]::NewGuid().ToString())
    $eventsBefore = Invoke-RestMethod "$baseUrl/campaigns/$($campaign.id)/events?limit=500"
    Stop-Engine
    Start-Engine 'restart'
    $replayed = PostRaw "/combat/$($encounter.id)/rewards/experience" $xpBody $xpOperation
    $after = Invoke-RestMethod "$baseUrl/combat/$($encounter.id)/rewards"
    $fighterAfter = Invoke-RestMethod "$baseUrl/characters/$($fighter.id)/inventory/state"
    $eventsAfter = Invoke-RestMethod "$baseUrl/campaigns/$($campaign.id)/events?limit=500"
    Assert-True ($replayed.revision -eq $award.revision -and @($after.rewards.awards).Count -eq 1) 'XP retry duplicated or changed the award.'
    Assert-True (@($fighterAfter.items | Where-Object id -eq $lootItem.id).Count -eq 1) 'Loot did not persist.'
    Assert-True (@($eventsAfter | Where-Object type -eq 'ExperienceAwarded').Count -eq 1) 'XP event duplicated.'
    Assert-True (@($eventsAfter | Where-Object type -eq 'LootAwarded').Count -eq 1) 'Loot event duplicated.'
    Assert-True (@($eventsBefore).Count -eq @($eventsAfter).Count) 'Event count changed after replay.'
    $result = @{status='passed';campaignId=$campaign.id;encounterId=$encounter.id;monsterIds=@($monsters | ForEach-Object {$_.instance.id});attacks=$attacks;spellCast=$spellCast;itemUsed=$itemUsed;experience=$award.awards[0].amount;lootItemId=$lootItem.id;eventCount=@($eventsAfter).Count;directory=$runDirectory}
    $result | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $runDirectory 'result.json')
    $result | ConvertTo-Json -Depth 20
}
finally { Stop-Engine }
