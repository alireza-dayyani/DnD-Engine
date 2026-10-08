namespace DndEngine.Domain.World;

public enum WorldVisibility { Public, Private, Secret }
public enum NpcStatus { Alive, Dead, Missing }
public enum LocationKind { World, Region, Settlement, District, Building, Room }
public enum WorldEntityKind { Npc, Character, Faction }
public enum KnowledgeStatus { Known, Suspected, Believed }
public enum QuestStatus { Unknown, Discovered, Active, Completed, Failed, Abandoned }

public sealed record WorldEntityRef(WorldEntityKind Kind, Guid Id);
public sealed record WorldValue(string Subject, string Predicate, string Object);
public sealed record NarrativeNpc(Guid Id, string Name, string Description, string Appearance,
    string Background, string Personality, string Motivations, string Fears, string Goals,
    Guid? LocationId = null, NpcStatus Status = NpcStatus.Alive,
    string PublicInformation = "", string PrivateInformation = "",
    Guid? MechanicalCharacterId = null, bool IsPublic = false);
public sealed record WorldLocation(Guid Id, string Name, string Description, LocationKind Kind,
    Guid? ParentId = null, string Condition = "", Guid? ControllingFactionId = null,
    bool IsDiscovered = false);
public sealed record WorldFaction(Guid Id, string Name, string Description, string Goals,
    string Motivations, string Status, Guid? LeaderNpcId = null,
    Guid? HeadquartersLocationId = null, int? Influence = null, bool IsPublic = false,
    Guid[]? AlliedFactionIds = null, Guid[]? EnemyFactionIds = null,
    Dictionary<string,int>? Resources = null);
public sealed record FactionMembership(Guid Id, Guid FactionId, WorldEntityRef Member,
    string Role, bool Active = true);
public sealed record WorldRelationship(Guid Id, WorldEntityRef From, WorldEntityRef To,
    int Trust, int Respect, int Affection, int Fear, int Suspicion, int Hostility,
    string Label, string Note);
public sealed record WorldFact(Guid Id, string Key, WorldValue Value, string Source,
    WorldVisibility Visibility);
public sealed record KnowledgeRecord(Guid Id, Guid FactId, WorldEntityRef Holder,
    KnowledgeStatus Status, int Confidence, WorldValue? BeliefValue,
    long AcquiredGameSeconds, string Source);
public sealed record QuestObjective(Guid Id, string Description, bool Completed,
    Guid[]? DependsOnObjectiveIds = null);
public sealed record WorldQuest(Guid Id, string Title, string Description, QuestStatus Status,
    QuestObjective[] Objectives, Guid[]? PrerequisiteQuestIds = null,
    Guid[]? RelatedNpcIds = null, Guid[]? RelatedFactionIds = null,
    Guid[]? RelatedLocationIds = null, long? DeadlineGameSeconds = null,
    string Rewards = "", string Consequences = "", bool IsPublic = false);

public sealed record WorldState(Guid CampaignId, long Revision,
    NarrativeNpc[] Npcs, WorldLocation[] Locations, WorldFaction[] Factions,
    FactionMembership[] Memberships, WorldRelationship[] Relationships,
    WorldFact[] Facts, KnowledgeRecord[] Knowledge, WorldQuest[] Quests)
{
    public static WorldState Empty(Guid campaignId) =>
        new(campaignId,0,[],[],[],[],[],[],[],[]);
}

public sealed record PublicNpc(Guid Id, string Name, string Description, string Appearance,
    Guid? LocationId, NpcStatus Status, string PublicInformation);
public sealed record PublicFaction(Guid Id, string Name, string Description, string Status,
    Guid? LeaderNpcId, Guid? HeadquartersLocationId);
public sealed record PublicFact(Guid Id, string Key, WorldValue Value);
public sealed record PublicQuest(Guid Id, string Title, string Description, QuestStatus Status,
    QuestObjective[] Objectives, Guid[] RelatedNpcIds, Guid[] RelatedFactionIds,
    Guid[] RelatedLocationIds, long? DeadlineGameSeconds);
public sealed record PublicWorldState(Guid CampaignId, long Revision, long GameSeconds,
    PublicNpc[] Npcs, WorldLocation[] Locations, PublicFaction[] Factions,
    PublicFact[] Facts, PublicQuest[] Quests);
public sealed record DmWorldState(WorldState State, long GameSeconds);
