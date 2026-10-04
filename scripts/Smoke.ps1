param([int]$Port = 5225)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runDirectory = Join-Path $repoRoot ('artifacts/smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$null = New-Item -ItemType Directory -Path $runDirectory -Force
$dataDirectory = Join-Path $runDirectory 'data'
$assembly = Join-Path $repoRoot 'src/DndEngine.Api/bin/Debug/net10.0/DndEngine.Api.dll'
if (!(Test-Path -LiteralPath $assembly)) { throw 'Build the solution before running the smoke test.' }
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
    if ($script:engineProcess -and !$script:engineProcess.HasExited) {
        Stop-Process -Id $script:engineProcess.Id
        $script:engineProcess.WaitForExit()
    }
}
function Post([string]$route, $body) {
    Invoke-RestMethod -Uri "$baseUrl$route" -Method Post -ContentType 'application/json' -Body ($body | ConvertTo-Json -Depth 20)
}
function Assert-True([bool]$condition, [string]$message) { if (!$condition) { throw $message } }
try {
    # Refuse an occupied port so the test cannot accidentally address another running engine.
    $probe = [Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback, $Port)
    try { $probe.Start() } finally { $probe.Stop() }
    Start-Engine 'first'
    $campaign = Post '/campaigns' @{name='Phase 1 restart demonstration'}
    $character = Post '/characters' @{campaignId=$campaign.id; name='Vaelaris'; level=5; maximumHp=20; abilities=@{Strength=10; Dexterity=14; Constitution=12; Intelligence=10; Wisdom=16; Charisma=18}; skillProficiencies=@('deception'); savingThrowProficiencies=@('Wisdom')}
    $id = $character.id
    $retrieved = Invoke-RestMethod "$baseUrl/characters/$id"
    Assert-True ($retrieved.health.current -eq 20) 'Initial HP did not persist.'
    $normal = Post "/characters/$id/checks/skill" @{skillId='deception'; dc=15}
    Assert-True ($normal.rolls.Count -eq 1 -and $normal.total -eq ($normal.selectedRoll + 7)) 'Normal check breakdown invalid.'
    $advantage = Post "/characters/$id/checks/skill" @{skillId='deception'; dc=15; advantage=$true}
    Assert-True ($advantage.rolls.Count -eq 2 -and $advantage.selectedRoll -eq ($advantage.rolls | Measure-Object -Maximum).Maximum) 'Advantage selected the wrong die.'
    $save = Post "/characters/$id/saving-throws" @{ability='Wisdom'; dc=15}
    Assert-True ($save.total -eq ($save.selectedRoll + 6)) 'Saving throw modifier invalid.'
    $damage = Post "/characters/$id/damage" @{amount=7}
    $healing = Post "/characters/$id/heal" @{amount=3}
    Assert-True ($damage.change.after.current -eq 13 -and $healing.change.after.current -eq 16) 'HP changes invalid.'
    Stop-Engine
    Start-Engine 'restarted'
    $persisted = Invoke-RestMethod "$baseUrl/characters/$id"
    $events = Invoke-RestMethod "$baseUrl/campaigns/$($campaign.id)/events"
    Assert-True ($persisted.health.current -eq 16 -and $persisted.revision -eq 5) 'State did not survive process restart.'
    Assert-True ($events.Count -eq 7 -and $events[-1].type -eq 'CharacterHealed') 'Audit timeline did not persist.'
    $evidence = @{verifiedAtUtc=[DateTimeOffset]::UtcNow; databaseDirectory=$dataDirectory; campaign=$campaign; character=$character; normalCheck=$normal; advantageCheck=$advantage; savingThrow=$save; damage=$damage; healing=$healing; afterRestart=$persisted; events=$events; result='PASS'}
    $evidence | ConvertTo-Json -Depth 30 | Set-Content -LiteralPath (Join-Path $runDirectory 'result.json') -Encoding UTF8
    Write-Output "PASS: campaign=$($campaign.id), character=$id, HP=16/20, revision=5, events=7. Evidence: $runDirectory/result.json"
} finally { Stop-Engine }
