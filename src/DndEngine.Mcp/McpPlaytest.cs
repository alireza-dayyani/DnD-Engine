using System.Text.Json;
using ModelContextProtocol.Client;

namespace DndEngine.Mcp;

/// <summary>A scripted, official-SDK client. It makes no model or narration claims.</summary>
public static class McpPlaytest
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length!=10) throw new ArgumentException(
            "client-playtest <endpoint> <campaign> <hero> <scout> <encounter> <heroActor> <monsterActor> <weapon> <orderCsv> <secret>");
        var campaign=Guid.Parse(args[1]); var hero=Guid.Parse(args[2]);
        var scout=Guid.Parse(args[3]); var encounter=Guid.Parse(args[4]);
        var heroActor=Guid.Parse(args[5]); var monsterActor=Guid.Parse(args[6]);
        var weapon=Guid.Parse(args[7]);
        var order=args[8].Split(',').Select(Guid.Parse).ToArray();
        var secret=args[9];
        using var dmHttp=new HttpClient(new HttpClientHandler { UseProxy=false });
        using var playerHttp=new HttpClient(new HttpClientHandler { UseProxy=false });
        await using var dm=await Connect(args[0],"DND_MCP_DM_TOKEN",dmHttp);
        await using var player=await Connect(args[0],"DND_MCP_PLAYER_TOKEN",playerHttp);
        var dmContext=await Call(dm,"get_dm_context",new() { ["campaignId"]=campaign });
        var playerContext=await Call(player,"get_dm_context",new() {
            ["campaignId"]=campaign,["characterId"]=hero });
        if (!dmContext.ToString().Contains(secret,StringComparison.OrdinalIgnoreCase) ||
            playerContext.ToString().Contains(secret,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Playtest context disclosure was incorrect.");
        await Call(player,"get_available_actions",new() {
            ["campaignId"]=campaign,["characterId"]=hero });
        await Call(player,"make_skill_check",new() {
            ["campaignId"]=campaign,["characterId"]=scout,
            ["request"]=new { skillId="athletics",dc=10 },["operationId"]=Guid.NewGuid() });
        await Call(dm,"start_encounter",new() { ["campaignId"]=campaign,
            ["encounterId"]=encounter,["request"]=new { order },
            ["operationId"]=Guid.NewGuid() });

        var attacks=0; var actionEconomyDenied=false; var monsterDead=false;
        for(var turn=0;turn<80;turn++)
        {
            var view=await Call(dm,"get_active_encounter",new() { ["campaignId"]=campaign });
            monsterDead=Field(view,"characters").EnumerateArray().Any(x=>
                Field(x,"id").GetGuid()==FindMonsterCharacter(view,monsterActor) &&
                Field(Field(x,"health"),"dead").GetBoolean());
            if (monsterDead) break;
            var actor=Field(view,"currentCombatantId").GetGuid();
            if (actor==heroActor)
            {
                var input=new Dictionary<string,object?> { ["campaignId"]=campaign,
                    ["encounterId"]=encounter,["request"]=new {
                        combatantId=heroActor,attack=new { targetId=monsterActor,
                            weaponId=weapon,mode="Melee",context=new {
                                distanceFeet=5,attackerCanSeeTarget=true,
                                targetCanSeeAttacker=true } } },
                    ["operationId"]=Guid.NewGuid() };
                var attack=await Call(player,"perform_attack",input);
                var replay=await Call(player,"perform_attack",input);
                if (attack.ToString()!=replay.ToString())
                    throw new InvalidOperationException("Attack replay was not exact.");
                attacks++;
                if (attacks==1)
                {
                    input["operationId"]=Guid.NewGuid();
                    var second=await player.CallToolAsync("perform_attack",input);
                    actionEconomyDenied=second.IsError==true;
                    if (!actionEconomyDenied)
                        throw new InvalidOperationException("Second attack bypassed action economy.");
                }
            }
            if (actor!=heroActor && actor!=monsterActor)
                throw new InvalidOperationException("Unexpected combatant in the playtest.");
            await Call(actor==heroActor ? player : dm,"end_turn",new() {
                ["campaignId"]=campaign,["encounterId"]=encounter,
                ["request"]=new { combatantId=actor },["operationId"]=Guid.NewGuid() });
        }
        if (!monsterDead || attacks==0) throw new InvalidOperationException(
            "The seeded encounter was not completed within the turn limit.");
        var final=await Call(dm,"get_active_encounter",new() { ["campaignId"]=campaign });
        await Call(dm,"complete_encounter",new() { ["campaignId"]=campaign,
            ["encounterId"]=encounter,["request"]=new { outcome="Victory",
                expectedRevision=Field(Field(final,"encounter"),"revision").GetInt64() },
            ["operationId"]=Guid.NewGuid() });
        var pending=await Call(dm,"get_pending_consequences",new() { ["campaignId"]=campaign });
        var completion=pending.EnumerateArray().First(x=>
            Field(Field(x,"source"),"type").GetString()=="EncounterCompleted");
        var sourceEventId=Field(Field(completion,"source"),"eventId").GetGuid();
        var world=await Call(dm,"get_visible_world",new() { ["campaignId"]=campaign });
        var revision=Field(Field(world,"state"),"revision").GetInt64();
        var witness=Guid.NewGuid();
        var proposed=await Call(dm,"propose_narrative_consequence",new() {
            ["campaignId"]=campaign,["sourceEventId"]=sourceEventId,
            ["proposal"]=new { expectedRevision=revision,cause="Witness responds to the encounter",
                changes=new[] { new { kind="CreateNpc",npc=new { id=witness,
                    name="Aftermath witness",description="Saw the encounter end",
                    appearance="travel cloak",background="local traveler",
                    personality="observant",motivations="safety",fears="monsters",
                    goals="testify",isPublic=true } } } },
            ["operationId"]=Guid.NewGuid() });
        var resolution=new Dictionary<string,object?> { ["campaignId"]=campaign,
            ["sourceEventId"]=sourceEventId,["apply"]=true,
            ["reviewToken"]=Field(proposed,"reviewToken").GetString(),
            ["operationId"]=Guid.NewGuid() };
        var applied=await Call(dm,"record_narrative_consequence",resolution);
        var appliedReplay=await Call(dm,"record_narrative_consequence",resolution);
        if (applied.ToString()!=appliedReplay.ToString())
            throw new InvalidOperationException("Consequence replay was not exact.");
        Console.WriteLine(JsonSerializer.Serialize(new { status="passed",attacks,
            actionEconomyDenied,sourceEventId,witnessId=witness,
            worldRevision=revision+1 }));
    }

    public static async Task ResumeAsync(string[] args)
    {
        if (args.Length!=3) throw new ArgumentException(
            "client-resume <endpoint> <campaign> <witness>");
        using var http=new HttpClient(new HttpClientHandler { UseProxy=false });
        await using var dm=await Connect(args[0],"DND_MCP_DM_TOKEN",http);
        var context=await Call(dm,"get_dm_context",new() {
            ["campaignId"]=Guid.Parse(args[1]) });
        if (!context.ToString().Contains(args[2],StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Committed consequence disappeared on restart.");
        Console.WriteLine(JsonSerializer.Serialize(new { status="passed",contextRecovered=true }));
    }

    private static Guid FindMonsterCharacter(JsonElement view,Guid combatantId) =>
        Field(Field(view,"encounter"),"combatants").EnumerateArray()
            .Where(x=>Field(x,"id").GetGuid()==combatantId)
            .Select(x=>Field(x,"characterId").GetGuid()).Single();

    private static JsonElement Field(JsonElement item,string name) =>
        item.EnumerateObject().First(x=>x.Name.Equals(name,
            StringComparison.OrdinalIgnoreCase)).Value;

    private static async Task<JsonElement> Call(McpClient client,string name,
        Dictionary<string,object?> arguments)
    {
        var response=await client.CallToolAsync(name,arguments);
        if (response.IsError==true)
            throw new InvalidOperationException($"MCP playtest tool {name} failed: " +
                JsonSerializer.Serialize(response.Content));
        return JsonSerializer.SerializeToElement(response.StructuredContent);
    }

    private static async Task<McpClient> Connect(string endpoint,string tokenVariable,
        HttpClient http)
    {
        var token=Environment.GetEnvironmentVariable(tokenVariable);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"{tokenVariable} is required.");
        var transport=new HttpClientTransport(new HttpClientTransportOptions {
            Endpoint=new Uri(endpoint),TransportMode=HttpTransportMode.StreamableHttp,
            AdditionalHeaders=new Dictionary<string,string> {
                ["Authorization"]=$"Bearer {token}" } },http,null,false);
        return await McpClient.CreateAsync(transport);
    }
}
