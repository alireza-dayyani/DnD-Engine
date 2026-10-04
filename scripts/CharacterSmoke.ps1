param([int]$Port = 5227)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runDirectory = Join-Path $repoRoot ('artifacts/character-smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
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
function Post([string]$route, $body) {
    Invoke-RestMethod -Uri "$baseUrl$route" -Method Post -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 30)
}
function Assert-True([bool]$condition, [string]$message) { if (!$condition) { throw $message } }
try {
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try { $probe.Start() } finally { $probe.Stop() }
    Start-Engine 'first'
    $campaign = Post '/campaigns' @{name='Phase 3 character smoke'}
    $choices = Invoke-RestMethod "$baseUrl/character-choices"
    Assert-True ($choices.species.Count -eq 9 -and $choices.backgrounds.Count -eq 4 -and $choices.classes.Count -eq 12) 'Character content count mismatch.'
    $sheet = Post '/srd-characters' @{
        campaignId=$campaign.id; name='Mira'; speciesId='dwarf'; speciesVariantId=$null; size='Medium'; backgroundId='criminal'; classId='fighter'
        baseAbilities=@{Strength=15;Dexterity=14;Constitution=14;Intelligence=10;Wisdom=12;Charisma=8}
        backgroundBonuses=@{Dexterity=2;Constitution=1}; classSkills=@('athletics','perception')
        startingItemIds=@('chain-mail','greatsword'); masteredWeaponIds=@('greatsword'); fightingStyleFeat='defense'
    }
    Assert-True ($sheet.level -eq 1 -and $sheet.maximumHp -eq 13 -and $sheet.armorClass.total -eq 13) 'Level 1 derivation failed.'
    $armor = @($sheet.inventory | Where-Object definitionId -eq 'chain-mail')[0]
    $sheet = Post "/characters/$($sheet.id)/inventory/equip" @{itemId=$armor.id;expectedRevision=$sheet.revision}
    Assert-True ($sheet.armorClass.total -eq 17) 'Chain Mail and Defense AC failed.'
    $sheet = Post "/characters/$($sheet.id)/level-up" @{classId='fighter';expectedRevision=$sheet.revision}
    Assert-True ($sheet.level -eq 2 -and @($sheet.resources | Where-Object id -eq 'action-surge').Count -eq 1) 'Fighter level 2 failed.'
    $sheet = Post "/characters/$($sheet.id)/resources/spend" @{resourceId='action-surge';amount=1;expectedRevision=$sheet.revision}
    Assert-True ((@($sheet.resources | Where-Object id -eq 'action-surge')[0].current) -eq 0) 'Resource spend failed.'
    $rest = Post "/characters/$($sheet.id)/rests/short" @{hitDieSides=@(10);expectedRevision=$sheet.revision}
    Assert-True ($rest.hitDieRolls.Count -eq 1 -and $rest.sheet.hitDice[0].available -eq 1 -and @($rest.sheet.resources | Where-Object id -eq 'action-surge')[0].current -eq 1) 'Short Rest recovery failed.'
    $sheet = Post "/characters/$($sheet.id)/level-up" @{classId='rogue';expectedRevision=$rest.sheet.revision;multiclassSkill='investigation'}
    Assert-True ($sheet.level -eq 3 -and $sheet.proficiencyBonus -eq 2 -and @($sheet.classes).Count -eq 2) 'Multiclass level failed.'
    $rest = Post "/characters/$($sheet.id)/rests/long" @{expectedRevision=$sheet.revision}
    Assert-True ($rest.sheet.hitDice.Count -eq 2 -and @($rest.sheet.hitDice | Where-Object { $_.available -ne $_.total }).Count -eq 0) 'Long Rest Hit Dice failed.'
    $id = $sheet.id
    Stop-Engine
    Start-Engine 'restart'
    $persisted = Invoke-RestMethod "$baseUrl/characters/$id/sheet"
    Assert-True ($persisted.level -eq 3 -and $persisted.armorClass.total -eq 17) 'Restart persistence failed.'
    $events = Invoke-RestMethod "$baseUrl/campaigns/$($campaign.id)/events"
    Assert-True (@($events | Where-Object type -eq 'LevelGained').Count -eq 2) 'Level events missing.'
    $result = @{status='passed';characterId=$id;campaignId=$campaign.id;level=$persisted.level;classes=$persisted.classes;armorClass=$persisted.armorClass.total;eventCount=@($events).Count;directory=$runDirectory}
    $result | ConvertTo-Json -Depth 20 | Set-Content (Join-Path $runDirectory 'result.json')
    $result | ConvertTo-Json -Depth 20
}
finally { Stop-Engine }
