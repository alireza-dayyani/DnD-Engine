param([int]$Port = 5530)
$ErrorActionPreference = 'Stop'
$repoRoot = [IO.Path]::GetFullPath((Join-Path $PSScriptRoot '..'))
$runDirectory = Join-Path $repoRoot ('artifacts/world-smoke-' + (Get-Date -Format 'yyyyMMdd-HHmmss'))
$null = New-Item -ItemType Directory -Path $runDirectory -Force
$dataDirectory = Join-Path $runDirectory 'data'
$assembly = Join-Path $repoRoot 'src/DndEngine.Api/bin/Debug/net10.0/DndEngine.Api.dll'
if (!(Test-Path -LiteralPath $assembly)) { throw 'Build the solution before running the world smoke test.' }
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
function PostJson([string]$route, [string]$json, [string]$operationId) {
    Invoke-RestMethod -Method Post -Uri "$baseUrl$route" -ContentType 'application/json' -Headers @{ 'X-Operation-Id' = $operationId } -Body $json
}
function Json($value) { ConvertTo-Json -InputObject $value -Depth 30 -Compress }
function Assert([bool]$condition, [string]$message) { if (!$condition) { throw $message } }

try {
    Start-Engine 'initial'
    $campaign = PostJson '/campaigns' (Json @{ name='World smoke' }) ([guid]::NewGuid().ToString())
    $campaignId = $campaign.id
    $dm = "/dm/campaigns/$campaignId/world"
    $public = "/campaigns/$campaignId/world"
    $root=[guid]::NewGuid().ToString(); $region=[guid]::NewGuid().ToString()
    $town=[guid]::NewGuid().ToString(); $mira=[guid]::NewGuid().ToString()
    $spy=[guid]::NewGuid().ToString(); $faction=[guid]::NewGuid().ToString()
    $fact=[guid]::NewGuid().ToString(); $quest=[guid]::NewGuid().ToString()
    $objective=[guid]::NewGuid().ToString(); $relation=[guid]::NewGuid().ToString()
    $npcBase=@{ description='A named person'; appearance='blue cloak'; background='courier'; personality='careful'; motivations='protect the town'; fears='exposure'; goals='find the relic' }
    $miraNpc=$npcBase.Clone(); $miraNpc.id=$mira; $miraNpc.name='Mira'; $miraNpc.locationId=$town; $miraNpc.isPublic=$true; $miraNpc.publicInformation='A local guide'; $miraNpc.privateInformation='The hidden cipher'
    $spyNpc=$npcBase.Clone(); $spyNpc.id=$spy; $spyNpc.name='Secret Spy'; $spyNpc.locationId=$town
    $secret=@{ id=$fact; key='artifact-holder'; value=@{ subject='Vaelaris'; predicate='possesses'; object='the amber relic' }; source='wizard witness'; visibility='Secret' }
    $baseQuest=@{ id=$quest; title='Find the relic'; description='Investigate the vault'; status='Discovered'; objectives=@(@{ id=$objective; description='Speak with Mira'; completed=$false }); relatedNpcIds=@($mira); relatedFactionIds=@($faction); relatedLocationIds=@($town); isPublic=$true }
    $initial=@{ expectedRevision=0; cause='Session zero'; changes=@(
        @{kind='CreateLocation';location=@{id=$root;name='World';description='Known world';kind='World';isDiscovered=$true}},
        @{kind='CreateLocation';location=@{id=$region;name='Region';description='Western region';kind='Region';parentId=$root;isDiscovered=$true}},
        @{kind='CreateLocation';location=@{id=$town;name='Town';description='River town';kind='Settlement';parentId=$region;isDiscovered=$true}},
        @{kind='CreateNpc';npc=$miraNpc},
        @{kind='CreateNpc';npc=$spyNpc},
        @{kind='CreateFaction';faction=@{id=$faction;name='Lantern Court';description='Local council';goals='defend town';motivations='duty';status='active';leaderNpcId=$mira;headquartersLocationId=$town;isPublic=$true}},
        @{kind='SetMembership';membership=@{id=[guid]::NewGuid().ToString();factionId=$faction;member=@{kind='Npc';id=$mira};role='captain'}},
        @{kind='SetRelationship';relationship=@{id=$relation;from=@{kind='Npc';id=$mira};to=@{kind='Npc';id=$spy};trust=-2;respect=0;affection=0;fear=1;suspicion=3;hostility=2;label='wary';note='past deception'}},
        @{kind='EstablishFact';fact=$secret},
        @{kind='GrantKnowledge';knowledge=@{id=[guid]::NewGuid().ToString();factId=$fact;holder=@{kind='Npc';id=$mira};status='Known';confidence=100;beliefValue=$null;acquiredGameSeconds=0;source='saw it'}},
        @{kind='CreateQuest';quest=$baseQuest}
    )}
    $initialJson=Json $initial
    $initialOperation=[guid]::NewGuid().ToString()
    $created=PostJson "$dm/changes" $initialJson $initialOperation
    Assert ($created.state.revision -eq 1) 'Initial world revision was not committed.'
    $safe=Invoke-RestMethod "$baseUrl$public"
    Assert ($safe.npcs.Count -eq 1 -and $safe.npcs[0].name -eq 'Mira') 'Player view exposed or omitted an NPC.'
    Assert ($safe.facts.Count -eq 0) 'Player view exposed a secret fact.'
    Assert ((Json $safe) -notmatch 'hidden cipher|amber relic|Secret Spy') 'Player view leaked restricted content.'
    $ordinaryEvents=Invoke-RestMethod "$baseUrl/campaigns/$campaignId/events"
    Assert ((Json $ordinaryEvents) -notmatch 'amber relic|wizard witness|FactEstablished|KnowledgeAcquired') 'Ordinary campaign timeline leaked narrative secrets.'
    $otherKnowledge=Invoke-RestMethod "$baseUrl$dm/knowledge/Npc/$spy"
    Assert ($otherKnowledge.Count -eq 0) 'An NPC acquired knowledge automatically.'

    $active=$baseQuest.Clone(); $active.status='Active'
    $advanced=PostJson "$dm/changes" (Json @{expectedRevision=1;cause='Investigation begins';changes=@(@{kind='UpdateQuest';quest=$active})}) ([guid]::NewGuid().ToString())
    Assert ($advanced.state.quests[0].status -eq 'Active') 'Quest did not activate.'
    $done=$baseQuest.Clone(); $done.status='Completed'; $done.objectives=@(@{id=$objective;description='Speak with Mira';completed=$true})
    $dead=$miraNpc.Clone(); $dead.status='Dead'
    $vacant=@{id=$faction;name='Lantern Court';description='Local council';goals='defend town';motivations='duty';status='leaderless';headquartersLocationId=$town;isPublic=$true}
    $consequence=@{expectedRevision=2;cause='After the encounter';changes=@(
        @{kind='UpdateNpc';npc=$dead},
        @{kind='UpdateFaction';faction=$vacant},
        @{kind='UpdateQuest';quest=$done}
    )}
    $finalJson=Json $consequence
    $finalOperation=[guid]::NewGuid().ToString()
    $applied=PostJson "$dm/changes" $finalJson $finalOperation
    Assert ($applied.state.revision -eq 3) 'Consequence batch did not commit.'
    $eventsBefore=(Invoke-RestMethod "$baseUrl$dm/events").Count
    Stop-Engine

    Start-Engine 'restart'
    $persisted=Invoke-RestMethod "$baseUrl$dm/"
    Assert ($persisted.state.revision -eq 3) 'World revision did not survive restart.'
    Assert (@($persisted.state.npcs | Where-Object { $_.id -eq $mira -and $_.status -eq 'Dead' }).Count -eq 1) 'NPC consequence did not survive restart.'
    Assert ($persisted.state.quests[0].status -eq 'Completed') 'Quest did not survive restart.'
    $replayed=PostJson "$dm/changes" $finalJson $finalOperation
    Assert ($replayed.state.revision -eq 3) 'Identical command replay changed the result.'
    Assert ((Invoke-RestMethod "$baseUrl$dm/events").Count -eq $eventsBefore) 'Replay duplicated world events.'
    Assert ((Json (Invoke-RestMethod "$baseUrl/campaigns/$campaignId/events")) -notmatch 'amber relic|FactEstablished') 'Timeline leaked a secret after restart.'
    $result=@{status='passed';campaignId=$campaignId;worldRevision=3;eventCount=$eventsBefore;directory=$runDirectory;secretIsolated=$true;replayStable=$true}
    $result | ConvertTo-Json -Depth 10 | Set-Content -LiteralPath (Join-Path $runDirectory 'result.json')
    $result | ConvertTo-Json -Depth 10
}
finally { Stop-Engine }
