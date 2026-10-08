using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Monsters;
using DndEngine.Domain.Inventory;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class MonsterInstanceRow
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public string DefinitionId { get; set; } = "";
    public string PackVersion { get; set; } = "";
    public string SpellPackVersion { get; set; } = "";
    public string LimitedUsesJson { get; set; } = "";
    public long Revision { get; set; }
}

public sealed class SqliteMonsterStore(CampaignDbContext db) : IMonsterStore
{
    public async Task<MonsterInstance?> GetAsync(Guid id, CancellationToken ct)
    {
        var row = await db.MonsterInstances.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : new(row.Id,row.CampaignId,row.DefinitionId,row.PackVersion,
            row.SpellPackVersion,JsonSerializer.Deserialize<Dictionary<string,int>>(
                row.LimitedUsesJson,CombatCatalog.Json),row.Revision);
    }

    public async Task CreateAsync(MonsterInstance instance, Character character,
        CombatProfile profile, InventoryState inventory, CampaignEvent entry, CancellationToken ct)
    {
        inventory.Validate();
        if (instance.Id != character.Id || instance.Id != profile.State.CharacterId ||
            instance.CampaignId != character.CampaignId)
            throw new RuleViolation("Monster state identities do not match.");
        db.Characters.Add(SqliteCampaignStore.ToRow(character));
        db.CombatProfiles.Add(new() { CharacterId=character.Id,
            StateJson=JsonSerializer.Serialize(profile.State,CombatCatalog.Json) });
        db.MonsterInstances.Add(new() { Id=instance.Id, CampaignId=instance.CampaignId,
            DefinitionId=instance.DefinitionId, PackVersion=instance.PackVersion,
            SpellPackVersion=instance.SpellPackVersion,
            LimitedUsesJson=JsonSerializer.Serialize(instance.LimitedUsesRemaining ?? [],CombatCatalog.Json),
            Revision=instance.Revision });
        db.InventoryStates.Add(new() { OwnerId=instance.Id,
            ItemsJson=JsonSerializer.Serialize(inventory.Items,CombatCatalog.Json),
            CopperPieces=inventory.CopperPieces });
        db.Events.Add(SqliteCampaignStore.ToRow(entry));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
}
