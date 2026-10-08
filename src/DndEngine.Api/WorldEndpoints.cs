using System.Net;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.World;

namespace DndEngine.Api;

public static class WorldEndpoints
{
    public static void MapWorld(this WebApplication app)
    {
        app.MapGet("/campaigns/{id:guid}/world",
            (Guid id,WorldService service,CancellationToken ct)=>service.GetPublicAsync(id,ct));

        var dm=app.MapGroup("/dm/campaigns/{id:guid}/world");
        dm.AddEndpointFilter(async (context,next)=>
            context.HttpContext.Connection.RemoteIpAddress is { } address &&
            IPAddress.IsLoopback(address)
                ? await next(context) : Results.Problem(statusCode:403,
                    title:"DM world operations require a loopback connection."));

        dm.MapGet("/",(Guid id,WorldService service,CancellationToken ct)=>service.GetDmAsync(id,ct));
        dm.MapGet("/npcs/{npcId:guid}",async (Guid id,Guid npcId,WorldService service,CancellationToken ct)=>
            (await service.GetDmAsync(id,ct)).State.Npcs.SingleOrDefault(x=>x.Id==npcId)
                ?? throw new NotFoundException("NPC not found."));
        dm.MapGet("/locations/{locationId:guid}",async (Guid id,Guid locationId,WorldService service,CancellationToken ct)=>
            (await service.GetDmAsync(id,ct)).State.Locations.SingleOrDefault(x=>x.Id==locationId)
                ?? throw new NotFoundException("Location not found."));
        dm.MapGet("/factions/{factionId:guid}",async (Guid id,Guid factionId,WorldService service,CancellationToken ct)=>
            (await service.GetDmAsync(id,ct)).State.Factions.SingleOrDefault(x=>x.Id==factionId)
                ?? throw new NotFoundException("Faction not found."));
        dm.MapGet("/relationships/{relationshipId:guid}",async (Guid id,Guid relationshipId,WorldService service,CancellationToken ct)=>
            (await service.GetDmAsync(id,ct)).State.Relationships.SingleOrDefault(x=>x.Id==relationshipId)
                ?? throw new NotFoundException("Relationship not found."));
        dm.MapGet("/facts",async (Guid id,WorldService service,CancellationToken ct)=>
            (await service.GetDmAsync(id,ct)).State.Facts);
        dm.MapGet("/knowledge/{kind}/{holderId:guid}",
            (Guid id,WorldEntityKind kind,Guid holderId,WorldService service,CancellationToken ct)=>
                service.GetKnowledgeAsync(id,kind,holderId,ct));
        dm.MapGet("/quests/active",async (Guid id,WorldService service,CancellationToken ct)=>
            (await service.GetDmAsync(id,ct)).State.Quests.Where(x=>x.Status==QuestStatus.Active).ToArray());
        dm.MapGet("/events",(Guid id,long? after,int? limit,WorldService service,CancellationToken ct)=>
            service.GetRecentEventsAsync(id,after??0,limit??100,ct));

        dm.MapPost("/changes",(Guid id,ApplyWorldChanges request,WorldService service,CancellationToken ct)=>
            service.ApplyAsync(id,request,ct)).WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPost("/npcs",(Guid id,WorldMutation<NarrativeNpc> request,WorldService service,CancellationToken ct)=>
            One(id,request,WorldChangeKind.CreateNpc,x=>new(WorldChangeKind.CreateNpc,Npc:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPut("/npcs/{npcId:guid}",(Guid id,Guid npcId,WorldMutation<NarrativeNpc> request,
            WorldService service,CancellationToken ct)=>
            One(id,Match(npcId,request),WorldChangeKind.UpdateNpc,
                x=>new(WorldChangeKind.UpdateNpc,Npc:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPost("/locations",(Guid id,WorldMutation<WorldLocation> request,WorldService service,CancellationToken ct)=>
            One(id,request,WorldChangeKind.CreateLocation,x=>new(WorldChangeKind.CreateLocation,Location:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPut("/locations/{locationId:guid}",(Guid id,Guid locationId,WorldMutation<WorldLocation> request,
            WorldService service,CancellationToken ct)=>
            One(id,Match(locationId,request),WorldChangeKind.UpdateLocation,
                x=>new(WorldChangeKind.UpdateLocation,Location:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPost("/factions",(Guid id,WorldMutation<WorldFaction> request,WorldService service,CancellationToken ct)=>
            One(id,request,WorldChangeKind.CreateFaction,x=>new(WorldChangeKind.CreateFaction,Faction:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPut("/factions/{factionId:guid}",(Guid id,Guid factionId,WorldMutation<WorldFaction> request,
            WorldService service,CancellationToken ct)=>
            One(id,Match(factionId,request),WorldChangeKind.UpdateFaction,
                x=>new(WorldChangeKind.UpdateFaction,Faction:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPost("/memberships",(Guid id,WorldMutation<FactionMembership> request,WorldService service,CancellationToken ct)=>
            One(id,request,WorldChangeKind.SetMembership,x=>new(WorldChangeKind.SetMembership,Membership:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPost("/relationships",(Guid id,WorldMutation<WorldRelationship> request,WorldService service,CancellationToken ct)=>
            One(id,request,WorldChangeKind.SetRelationship,x=>new(WorldChangeKind.SetRelationship,Relationship:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPost("/facts",(Guid id,WorldMutation<WorldFact> request,WorldService service,CancellationToken ct)=>
            One(id,request,WorldChangeKind.EstablishFact,x=>new(WorldChangeKind.EstablishFact,Fact:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPut("/facts/{factId:guid}",(Guid id,Guid factId,WorldMutation<WorldFact> request,
            WorldService service,CancellationToken ct)=>
            One(id,Match(factId,request),WorldChangeKind.UpdateFact,
                x=>new(WorldChangeKind.UpdateFact,Fact:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPost("/facts/{factId:guid}/disclose",(Guid id,Guid factId,
            WorldMutation<WorldFact> request,WorldService service,CancellationToken ct)=>
            One(id,Match(factId,request),WorldChangeKind.DiscloseSecret,
                x=>new(WorldChangeKind.DiscloseSecret,Fact:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPost("/knowledge",(Guid id,WorldMutation<KnowledgeRecord> request,WorldService service,CancellationToken ct)=>
            One(id,request,WorldChangeKind.GrantKnowledge,x=>new(WorldChangeKind.GrantKnowledge,Knowledge:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPost("/quests",(Guid id,WorldMutation<WorldQuest> request,WorldService service,CancellationToken ct)=>
            One(id,request,WorldChangeKind.CreateQuest,x=>new(WorldChangeKind.CreateQuest,Quest:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        dm.MapPut("/quests/{questId:guid}",(Guid id,Guid questId,WorldMutation<WorldQuest> request,
            WorldService service,CancellationToken ct)=>
            One(id,Match(questId,request),WorldChangeKind.UpdateQuest,
                x=>new(WorldChangeKind.UpdateQuest,Quest:x),service,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
    }

    private static Task<DmWorldState> One<T>(Guid id,WorldMutation<T> request,
        WorldChangeKind kind,Func<T,WorldChange> operation,WorldService service,CancellationToken ct)
        where T:class => service.ApplyAsync(id,new(request.ExpectedRevision,request.Cause,
            [operation(request.Value ?? throw new RuleViolation("World value is required."))]),ct);

    private static WorldMutation<T> Match<T>(Guid id,WorldMutation<T> request) where T:class
    {
        var value=request.Value ?? throw new RuleViolation("World value is required.");
        var actual=value switch {
            NarrativeNpc x=>x.Id,WorldLocation x=>x.Id,WorldFaction x=>x.Id,
            WorldFact x=>x.Id,WorldQuest x=>x.Id,_=>Guid.Empty };
        if (actual!=id) throw new RuleViolation("Route and world entity IDs differ.");
        return request;
    }
}
