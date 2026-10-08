using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Monsters;
using DndEngine.Domain.Inventory;

namespace DndEngine.Application;

public interface IMonsterCatalog
{
    Task<MonsterPack> GetAsync(Ruleset ruleset, string packVersion, CancellationToken ct);
}

public interface IMonsterStore
{
    Task<MonsterInstance?> GetAsync(Guid id, CancellationToken ct);
    Task CreateAsync(MonsterInstance instance, Character character, CombatProfile profile,
        InventoryState inventory, CampaignEvent entry, CancellationToken ct);
}

public sealed record CreateMonsterInstance(Guid CampaignId, string DefinitionId,
    string PackVersion = "1", string? Name = null,
    Dictionary<string, int>? Ammunition = null);
public sealed record AddMonsterToEncounter(Guid MonsterId, bool Surprised = false,
    string? InitiativeGroup = null);
public sealed record MonsterView(MonsterInstance Instance, MonsterDefinition Definition,
    CharacterView Character, CombatProfileState CombatProfile);
