param([int]$Port = 5226)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runDirectory = Join-Path $repoRoot ('artifacts/combat-smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$null = New-Item -ItemType Directory -Path $runDirectory -Force
$dataDirectory = Join-Path $runDirectory 'data'
$assembly = Join-Path $repoRoot 'src/DndEngine.Api/bin/Debug/net10.0/DndEngine.Api.dll'
if (!(Test-Path -LiteralPath $assembly)) { throw 'Build the solution first.' }
$baseUrl = "http://127.0.0.1:$Port"
$engineProcess = $null
function Start-Engine([string]$label) {
    $script:engineProcess = Start-Process -FilePath 'dotnet' -ArgumentList @("`"$assembly`"", '--urls', $baseUrl, '--DataDirectory', "`"$dataDirectory`"") -WorkingDirectory $repoRoot -WindowStyle Hidden -PassThru -RedirectStandardOutput (Join-Path $runDirectory "$label.stdout.log") -RedirectStandardError (Join-Path $runDirectory "$label.stderr.log")
    for ($attempt = 0; $attempt -lt 60; $attempt++) {
        if ($script:engineProcess.HasExited) { throw "API exited; inspect $runDirectory" }
        try { $null = Invoke-RestMethod "$baseUrl/health"; return } catch { Start-Sleep -Milliseconds 250 }
    }
    throw 'API did not become ready.'
}
function Stop-Engine {
    if ($script:engineProcess -and !$script:engineProcess.HasExited) { Stop-Process -Id $script:engineProcess.Id; $script:engineProcess.WaitForExit() }
}
function Send([string]$method, [string]$route, $body) {
    Invoke-RestMethod -Uri "$baseUrl$route" -Method $method -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 30)
}
function Assert-True([bool]$condition, [string]$message) { if (!$condition) { throw $message } }
try {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try { $probe.Start() } finally { $probe.Stop() }
    Start-Engine 'first'
    $campaign = Send Post '/campaigns' @{name='Combat restart demonstration'}
    $characters = @(); $weaponByCharacter = @{}
    foreach ($name in @('Vaelaris', 'Goblin A', 'Goblin B')) {
        $character = Send Post '/characters' @{campaignId=$campaign.id; name=$name; level=1; maximumHp=1000; armorClass=13; abilities=@{Strength=14; Dexterity=14; Constitution=14; Intelligence=10; Wisdom=10; Charisma=10}; skillProficiencies=@(); savingThrowProficiencies=@()}
        $characters += $character
        $null = Send Put "/characters/$($character.id)/combat-profile" @{speed=30; weaponProficiencies=@('longsword'); resistances=@(); immunities=@(); vulnerabilities=@(); conditionImmunities=@()}
        $weaponByCharacter[$character.id] = Send Post "/characters/$($character.id)/weapons" @{definitionId='longsword'}
    }
    $encounter = Send Post "/campaigns/$($campaign.id)/combat" @{name='Bridge ambush'}
    for ($i = 0; $i -lt 3; $i++) {
        $body = @{characterId=$characters[$i].id; kind='Monster'; zeroHpPolicy='Die'}
        if ($i -eq 0) { $body.kind='PlayerCharacter'; $body.zeroHpPolicy='DeathSaves' }
        $null = Send Post "/combat/$($encounter.id)/combatants" $body
    }
    $initiative = Send Post "/combat/$($encounter.id)/initiative" @{}
    # The caller explicitly adjudicates any tied positions; the engine validates descending totals.
    $order = @($initiative.result.combatants | Sort-Object @{Expression={$_.initiative.total}; Descending=$true}, id | ForEach-Object {$_.id})
    $null = Send Post "/combat/$($encounter.id)/start" @{order=$order}
    $view = Invoke-RestMethod "$baseUrl/combat/$($encounter.id)"
    $actor = $view.encounter.combatants | Where-Object id -eq $view.currentCombatantId
    $target = $view.encounter.combatants | Where-Object id -ne $actor.id | Select-Object -First 1
    $move = Send Post "/combat/$($encounter.id)/move" @{combatantId=$actor.id; distance=10}
    Assert-True ($move.result.remaining -eq 20) 'Movement budget is wrong.'
    $attack = Send Post "/combat/$($encounter.id)/attack" @{combatantId=$actor.id; attack=@{targetId=$target.id; weaponId=$weaponByCharacter[$actor.characterId].id; mode='Melee'; context=@{distanceFeet=5; attackerCanSeeTarget=$true; targetCanSeeAttacker=$true}}}
    # Production uses real dice. Check invariants, never require a particular random outcome.
    Assert-True ($attack.result.attackRoll.total -eq ($attack.result.attackRoll.selectedRoll + 4)) 'Attack modifier breakdown is wrong.'
    Assert-True ($attack.result.resources.actionUsed) 'Attack did not consume the action.'
    if ($attack.result.hit) {
        $sum = ($attack.result.damageRoll.rolls | Measure-Object -Sum).Sum
        Assert-True ($attack.result.damage.appliedDamage -eq ($sum + 2)) 'Damage breakdown is wrong.'
        Assert-True ($attack.result.health.after.current -eq (1000 - $attack.result.damage.appliedDamage)) 'HP did not match damage.'
    } else { Assert-True ($null -eq $attack.result.damage) 'Miss unexpectedly dealt damage.' }
    $condition = Send Post "/combat/$($encounter.id)/conditions" @{combatantId=$actor.id; kind='Poisoned'; source='Smoke test poison'}
    foreach ($member in $order) { $null = Send Post "/combat/$($encounter.id)/end-turn" @{combatantId=$member} }
    $before = Invoke-RestMethod "$baseUrl/combat/$($encounter.id)"
    $eventsBefore = Invoke-RestMethod "$baseUrl/campaigns/$($campaign.id)/events?limit=500"
    Assert-True ($before.encounter.round -eq 2 -and $before.currentCombatantId -eq $actor.id) 'Turn cycle did not advance to round 2.'
    Stop-Engine
    Start-Engine 'restarted'
    $after = Invoke-RestMethod "$baseUrl/combat/$($encounter.id)"
    $eventsAfter = Invoke-RestMethod "$baseUrl/campaigns/$($campaign.id)/events?limit=500"
    Assert-True (($before | ConvertTo-Json -Depth 40 -Compress) -ceq ($after | ConvertTo-Json -Depth 40 -Compress)) 'Combat state changed across restart.'
    Assert-True (($eventsBefore | ConvertTo-Json -Depth 40 -Compress) -ceq ($eventsAfter | ConvertTo-Json -Depth 40 -Compress)) 'Timeline changed across restart.'
    $null = Send Post "/combat/$($encounter.id)/move" @{combatantId=$actor.id; distance=5}
    $completed = Send Post "/combat/$($encounter.id)/end" @{}
    Assert-True ($completed.result.status -eq 'Completed') 'Encounter did not complete.'
    @{result='PASS'; verifiedAtUtc=[DateTimeOffset]::UtcNow; campaign=$campaign; initiative=$initiative; attack=$attack; condition=$condition; beforeRestart=$before; afterRestart=$after; events=$eventsAfter; completed=$completed} | ConvertTo-Json -Depth 50 | Set-Content -LiteralPath (Join-Path $runDirectory 'result.json') -Encoding UTF8
    Write-Output "PASS: encounter=$($encounter.id), round=2, restart state and timeline identical. Evidence: $runDirectory/result.json"
} finally { Stop-Engine }
