param([int]$ApiPort=5546,[int]$McpPort=5547)
$ErrorActionPreference='Stop'
$repo=[IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$run=Join-Path $repo ('artifacts/mcp-smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$null=New-Item -ItemType Directory -Path $run -Force
$data=Join-Path $run 'data'
$api=Join-Path $repo 'src/DndEngine.Api/bin/Debug/net10.0/DndEngine.Api.dll'
$mcp=Join-Path $repo 'src/DndEngine.Mcp/bin/Debug/net10.0/DndEngine.Mcp.dll'
if (!(Test-Path -LiteralPath $api) -or !(Test-Path -LiteralPath $mcp)) { throw 'Build the solution first.' }
$env:DataDirectory=$data
$keyBytes=New-Object byte[] 48
$rng=[Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($keyBytes) } finally { $rng.Dispose() }
$env:DND_MCP_AUTH_KEY=[Convert]::ToBase64String($keyBytes)
$env:DND_MCP_URL="http://127.0.0.1:$McpPort"
$apiProcess=$null; $mcpProcess=$null
function Wait-Port([int]$port,[Diagnostics.Process]$process) {
    for($i=0;$i -lt 120;$i++) {
        if ($process.HasExited) { throw "Process exited before listening on $port; inspect $run" }
        $client=[Net.Sockets.TcpClient]::new()
        try { $client.Connect('127.0.0.1',$port); return }
        catch { Start-Sleep -Milliseconds 250 }
        finally { $client.Dispose() }
    }
    throw "Port $port did not open."
}
function Stop-Managed([Diagnostics.Process]$process) {
    if ($process -and !$process.HasExited) { Stop-Process -Id $process.Id; $process.WaitForExit() }
}
function Post([string]$route,$value,[string]$operationId='') {
    $headers=@{}
    if ($operationId) { $headers['X-Operation-Id']=$operationId }
    Invoke-RestMethod -Uri "http://127.0.0.1:$ApiPort$route" -Method Post `
        -Headers $headers -ContentType 'application/json' `
        -Body (ConvertTo-Json -InputObject $value -Depth 30 -Compress)
}
function Start-Mcp([string]$label) {
    $script:mcpProcess=Start-Process -FilePath 'dotnet' -ArgumentList @("`"$mcp`"") `
        -WorkingDirectory $repo -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $run "$label.stdout.log") `
        -RedirectStandardError (Join-Path $run "$label.stderr.log")
    Wait-Port $McpPort $script:mcpProcess
}
function Probe([string]$token,[guid]$operation,[string]$marker,[string]$expected='') {
    $env:DND_MCP_PROBE_TOKEN=$token
    $arguments=@($mcp,'client-probe',"http://127.0.0.1:$McpPort/mcp",'-',
        $campaign.id,$operation.ToString('D'),$character.id,$unowned.id,$marker)
    if ($expected) { $arguments+= $expected }
    $result=(& dotnet @arguments | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Official SDK MCP client probe failed.' }
    return ConvertFrom-Json -InputObject $result
}
try {
    foreach($port in @($ApiPort,$McpPort)) {
        $listener=[Net.Sockets.TcpListener]::new([Net.IPAddress]::Loopback,$port)
        try { $listener.Start() } finally { $listener.Stop() }
    }
    $apiProcess=Start-Process -FilePath 'dotnet' -ArgumentList @("`"$api`"",'--urls',
        "http://127.0.0.1:$ApiPort",'--DataDirectory',"`"$data`"") `
        -WorkingDirectory $repo -WindowStyle Hidden -PassThru `
        -RedirectStandardOutput (Join-Path $run 'api.stdout.log') `
        -RedirectStandardError (Join-Path $run 'api.stderr.log')
    Wait-Port $ApiPort $apiProcess
    $campaign=Post '/campaigns' @{name='Phase 7 MCP security smoke'}
    $character=Post '/characters' @{campaignId=$campaign.id;name='Guardian';level=1;
        maximumHp=1000;abilities=@{Strength=20;Dexterity=16;Constitution=20;
            Intelligence=12;Wisdom=12;Charisma=12};skillProficiencies=@();
        savingThrowProficiencies=@()}
    $scout=Post '/characters' @{campaignId=$campaign.id;name='Scout';level=1;
        maximumHp=12;abilities=@{Strength=12;Dexterity=12;Constitution=12;
            Intelligence=12;Wisdom=12;Charisma=12};skillProficiencies=@();
        savingThrowProficiencies=@()}
    $unowned=Post '/characters' @{campaignId=$campaign.id;name='Private witness';level=1;
        maximumHp=12;abilities=@{Strength=12;Dexterity=12;Constitution=12;
            Intelligence=12;Wisdom=12;Charisma=12};skillProficiencies=@();
        savingThrowProficiencies=@()}
    $null=Invoke-RestMethod -Uri "http://127.0.0.1:$ApiPort/characters/$($character.id)/combat-profile" `
        -Method Put -ContentType 'application/json' -Body (@{speed=30;
            weaponProficiencies=@('mace');resistances=@();immunities=@();
            vulnerabilities=@();conditionImmunities=@()} | ConvertTo-Json -Depth 20)
    $mace=Post "/characters/$($character.id)/weapons" @{definitionId='mace'}
    $locationId=[guid]::NewGuid(); $npcId=[guid]::NewGuid(); $questId=[guid]::NewGuid()
    $objectiveId=[guid]::NewGuid()
    $factId=[guid]::NewGuid()
    $seed=@{expectedRevision=0;cause='Authored playtest scene';changes=@(
        @{kind='CreateLocation';location=@{id=$locationId;name='Tomb chamber';
            description='A sealed chamber';kind='World';isDiscovered=$true}},
        @{kind='CreateNpc';npc=@{id=$npcId;name='Tomb keeper';
            description='A wary witness';appearance='travel cloak';background='keeper';
            personality='careful';motivations='safety';fears='undead';goals='protect the tomb';
            locationId=$locationId;isPublic=$true}},
        @{kind='CreateQuest';quest=@{id=$questId;title='Clear the tomb';
            description='Defeat the skeleton';status='Discovered';isPublic=$true;
            objectives=@(@{id=$objectiveId;description='Defeat the skeleton';completed=$false})}},
        @{kind='UpdateQuest';quest=@{id=$questId;title='Clear the tomb';
            description='Defeat the skeleton';status='Active';isPublic=$true;
            objectives=@(@{id=$objectiveId;description='Defeat the skeleton';completed=$false})}},
        @{kind='EstablishFact';fact=@{id=$factId;key='relic-holder';
            value=@{subject='Vaelaris';predicate='possesses';object='amber relic'};
            source='sealed witness';visibility='Secret'}})}
    $null=Post "/dm/campaigns/$($campaign.id)/world/changes" $seed ([guid]::NewGuid().ToString('D'))
    $monster=Post '/monsters' @{campaignId=$campaign.id;definitionId='skeleton'} ([guid]::NewGuid().ToString('D'))
    $null=Post "/characters/$($monster.instance.id)/damage" @{amount=12}
    $encounter=Post "/campaigns/$($campaign.id)/combat" @{name='Tomb chamber'}
    $heroMember=(Post "/combat/$($encounter.id)/combatants" @{characterId=$character.id}).result
    $monsterMember=(Post "/combat/$($encounter.id)/monsters" `
        @{monsterId=$monster.instance.id} ([guid]::NewGuid().ToString('D'))).result
    $initiative=Post "/combat/$($encounter.id)/initiative" @{}
    $order=@($initiative.result.combatants | Sort-Object `
        @{Expression={$_.initiative.total};Descending=$true},id | ForEach-Object {$_.id})
    Stop-Managed $apiProcess; $apiProcess=$null

    $dm=[guid]::NewGuid(); $player=[guid]::NewGuid()
    & dotnet $mcp admin grant-dm $campaign.id $dm.ToString('D') | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'DM provisioning failed.' }
    & dotnet $mcp admin grant-player $campaign.id $player.ToString('D') $character.id | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Player provisioning failed.' }
    & dotnet $mcp admin grant-player $campaign.id $player.ToString('D') $scout.id | Out-Null
    if ($LASTEXITCODE -ne 0) { throw 'Scout ownership provisioning failed.' }
    $dmToken=(& dotnet $mcp admin token $dm.ToString('D') | Out-String).Trim()
    $playerToken=(& dotnet $mcp admin token $player.ToString('D') | Out-String).Trim()
    if (!$dmToken -or !$playerToken) { throw 'Local JWT provisioning failed.' }
    Start-Mcp 'initial'
    try {
        $response=Invoke-WebRequest -Uri "http://127.0.0.1:$McpPort/mcp" -Method Post `
            -ContentType 'application/json' -Body '{}' -UseBasicParsing
        throw "Anonymous MCP unexpectedly returned $($response.StatusCode)."
    } catch [Net.WebException] {
        if ([int]$_.Exception.Response.StatusCode -ne 401) { throw }
    }
    $dmOperation=[guid]::NewGuid(); $playerOperation=[guid]::NewGuid()
    $dmFirst=Probe $dmToken $dmOperation '-'
    $playerFirst=Probe $playerToken $playerOperation 'amber relic'
    $env:DND_MCP_DM_TOKEN=$dmToken
    $env:DND_MCP_PLAYER_TOKEN=$playerToken
    $playtestRaw=(& dotnet $mcp client-playtest "http://127.0.0.1:$McpPort/mcp" `
        $campaign.id $character.id $scout.id $encounter.id $heroMember.id $monsterMember.id `
        $mace.id ($order -join ',') 'amber relic' | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Official SDK campaign playtest failed.' }
    $playtest=ConvertFrom-Json -InputObject $playtestRaw
    Stop-Managed $mcpProcess; $mcpProcess=$null
    Start-Mcp 'restarted'
    $resumeRaw=(& dotnet $mcp client-resume "http://127.0.0.1:$McpPort/mcp" `
        $campaign.id $playtest.witnessId | Out-String).Trim()
    if ($LASTEXITCODE -ne 0) { throw 'Official SDK campaign resume failed.' }
    $resume=ConvertFrom-Json -InputObject $resumeRaw
    $dmReplay=Probe $dmToken $dmOperation '-' $dmFirst.rollHash
    $playerReplay=Probe $playerToken $playerOperation 'amber relic' $playerFirst.rollHash
    $result=@{status='passed';campaignId=$campaign.id;characterId=$character.id;
        scoutId=$scout.id;encounterId=$encounter.id;dmSubjectId=$dm;
        playerSubjectId=$player;
        toolCount=$dmFirst.toolCount;anonymousDenied=$true;secretIsolated=$true;
        replayStable=($dmReplay.rollHash -eq $dmFirst.rollHash -and
            $playerReplay.rollHash -eq $playerFirst.rollHash);
        playtest=$playtest;resume=$resume;directory=$run}
    $result | ConvertTo-Json -Depth 8 | Set-Content -LiteralPath (Join-Path $run 'result.json')
    $result | ConvertTo-Json -Depth 8
}
finally {
    Stop-Managed $apiProcess
    Stop-Managed $mcpProcess
    Remove-Item Env:DND_MCP_PROBE_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:DND_MCP_DM_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:DND_MCP_PLAYER_TOKEN -ErrorAction SilentlyContinue
    Remove-Item Env:DND_MCP_AUTH_KEY -ErrorAction SilentlyContinue
    Remove-Item Env:DND_MCP_URL -ErrorAction SilentlyContinue
    Remove-Item Env:DataDirectory -ErrorAction SilentlyContinue
}
