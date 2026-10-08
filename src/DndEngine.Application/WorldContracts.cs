using DndEngine.Domain;
using DndEngine.Domain.World;

namespace DndEngine.Application;

public interface IWorldStore
{
    Task<WorldState?> GetAsync(Guid campaignId, CancellationToken ct);
    Task SaveAsync(WorldState before, WorldState after, Campaign campaign,
        IReadOnlyList<CampaignEvent> events, CancellationToken ct);
    Task<CampaignEvent[]> GetEventsAsync(Guid campaignId,long after,int limit,
        IReadOnlyCollection<string> types,bool includeTypes,CancellationToken ct);
}

public enum WorldChangeKind
{
    CreateNpc, UpdateNpc, CreateLocation, UpdateLocation, CreateFaction, UpdateFaction,
    SetMembership, SetRelationship, EstablishFact, UpdateFact, GrantKnowledge,
    DiscloseSecret, CreateQuest, UpdateQuest
}

public sealed record WorldChange(WorldChangeKind Kind,
    NarrativeNpc? Npc = null, WorldLocation? Location = null,
    WorldFaction? Faction = null, FactionMembership? Membership = null,
    WorldRelationship? Relationship = null, WorldFact? Fact = null,
    KnowledgeRecord? Knowledge = null, WorldQuest? Quest = null);
public sealed record ApplyWorldChanges(long ExpectedRevision, string Cause, WorldChange[] Changes);
public sealed record WorldMutation<T>(long ExpectedRevision, string Cause, T Value);
