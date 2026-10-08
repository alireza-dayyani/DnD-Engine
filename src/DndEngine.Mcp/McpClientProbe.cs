using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using ModelContextProtocol.Client;

namespace DndEngine.Mcp;

/// <summary>Deterministic official-SDK client probe used by process smoke tests.</summary>
public static class McpClientProbe
{
    public static async Task RunAsync(string[] args)
    {
        if (args.Length is < 7 or > 8)
            throw new ArgumentException("client-probe <endpoint> <token> <campaignId> <operationId> <ownedCharacter> <unownedCharacter> <secretMarker> [expectedHash]");
        var endpoint=new Uri(args[0]);
        var token=args[1]=="-" ? Environment.GetEnvironmentVariable("DND_MCP_PROBE_TOKEN") : args[1];
        if (string.IsNullOrWhiteSpace(token)) throw new ArgumentException("Probe token is required.");
        var campaignId=Guid.Parse(args[2]); var operationId=Guid.Parse(args[3]);
        var owned=Guid.Parse(args[4]); var unowned=Guid.Parse(args[5]);
        var marker=args[6];
        using var http=new HttpClient(new HttpClientHandler { UseProxy=false });
        var transport=new HttpClientTransport(new HttpClientTransportOptions
        {
            Endpoint=endpoint,TransportMode=HttpTransportMode.StreamableHttp,
            AdditionalHeaders=new Dictionary<string,string> { ["Authorization"]=$"Bearer {token}" }
        },http,null,false);
        await using var client=await McpClient.CreateAsync(transport);
        var tools=await client.ListToolsAsync();
        var expected=new[] { "get_campaign_summary","get_visible_world","get_dm_context",
            "roll_dice","perform_attack","apply_world_changes",
            "record_narrative_consequence" };
        foreach(var name in expected)
            if (!tools.Any(x=>x.Name==name)) throw new InvalidOperationException($"Missing MCP tool {name}.");
        var summary=await client.CallToolAsync("get_campaign_summary",
            new Dictionary<string,object?> { ["campaignId"]=campaignId });
        if (summary.IsError==true) throw new InvalidOperationException("Authenticated summary failed.");
        var world=await client.CallToolAsync("get_visible_world",
            new Dictionary<string,object?> { ["campaignId"]=campaignId });
        if (world.IsError==true) throw new InvalidOperationException("Authenticated world read failed.");
        var worldText=JsonSerializer.Serialize(world);
        if (marker!="-" && worldText.Contains(marker,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Secret marker leaked into player world response.");
        var cross=await client.CallToolAsync("get_campaign_summary",
            new Dictionary<string,object?> { ["campaignId"]=Guid.NewGuid() });
        if (cross.IsError!=true || marker!="-" &&
            JsonSerializer.Serialize(cross).Contains(marker,StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Cross-campaign access was not denied safely.");
        if (marker!="-")
        {
            var context=await client.CallToolAsync("get_dm_context",
                new Dictionary<string,object?> { ["campaignId"]=campaignId,
                    ["characterId"]=owned });
            if (context.IsError==true || JsonSerializer.Serialize(context).Contains(marker,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Player context revealed a hidden fact.");
            var events=await client.CallToolAsync("get_recent_events",
                new Dictionary<string,object?> { ["campaignId"]=campaignId,
                    ["characterId"]=owned });
            var eventText=JsonSerializer.Serialize(events.StructuredContent);
            if (events.IsError==true || eventText.Contains(marker,StringComparison.OrdinalIgnoreCase) ||
                eventText.Contains("\"data\"",StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Player event history revealed a payload.");
            var unauthorized=await client.CallToolAsync("get_character_knowledge",
                new Dictionary<string,object?> { ["campaignId"]=campaignId,
                    ["characterId"]=unowned });
            if (unauthorized.IsError!=true || JsonSerializer.Serialize(unauthorized)
                    .Contains(marker,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Unowned knowledge was not denied safely.");
            var malformed=await client.CallToolAsync("get_campaign_summary",
                new Dictionary<string,object?> { ["campaignId"]="not-a-guid" });
            if (malformed.IsError!=true || JsonSerializer.Serialize(malformed)
                    .Contains(marker,StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Malformed MCP input leaked a hidden fact.");
            var dmOnly=await client.CallToolAsync("apply_world_changes",
                new Dictionary<string,object?> { ["campaignId"]=campaignId,
                    ["request"]=new { expectedRevision=0,cause="unauthorized",
                        changes=Array.Empty<object>() },["operationId"]=Guid.NewGuid() });
            if (dmOnly.IsError!=true || JsonSerializer.Serialize(dmOnly).Contains(marker,
                    StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("Player reached a DM-only command or leaked a secret.");
        }
        var input=new Dictionary<string,object?> {
            ["campaignId"]=campaignId,["expression"]="1d20",
            ["operationId"]=operationId };
        var first=await client.CallToolAsync("roll_dice",input);
        var replay=await client.CallToolAsync("roll_dice",input);
        if (first.IsError==true || replay.IsError==true)
            throw new InvalidOperationException("MCP dice command failed.");
        var firstText=JsonSerializer.Serialize(first.StructuredContent);
        if (firstText!=JsonSerializer.Serialize(replay.StructuredContent))
            throw new InvalidOperationException("MCP idempotent replay changed the roll.");
        var hash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(firstText)));
        if (args.Length==8 && args[7]!=hash)
            throw new InvalidOperationException("MCP replay changed after restart.");
        Console.WriteLine(JsonSerializer.Serialize(new { status="passed",toolCount=tools.Count,
            rollHash=hash,worldSecretIsolated=marker!="-" }));
    }

}
