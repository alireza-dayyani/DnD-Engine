using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Progression;
using DndEngine.Domain.Monsters;
using DndEngine.Domain.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class CombatProfileRow
{
    public Guid CharacterId { get; set; }
    public string StateJson { get; set; } = "";
}
public sealed class EncounterRow
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public long Revision { get; set; }
    public string StateJson { get; set; } = "";
}
// One character cannot spend the same resources in two unfinished encounters.
public sealed class CombatMembershipRow
{
    public Guid CharacterId { get; set; }
    public Guid EncounterId { get; set; }
}
public sealed class EncounterRewardRow
{
    public Guid EncounterId { get; set; }
    public string Outcome { get; set; } = "";
    public int AvailableExperience { get; set; }
    public string AwardsJson { get; set; } = "";
    public string DefeatedMonsterIdsJson { get; set; } = "";
    public long Revision { get; set; }
}
public sealed class SqliteCombatStore(CampaignDbContext db) : ICombatStore,IEncounterRewardStore
{
    public async Task<CombatProfile?> GetProfileAsync(Guid characterId, CancellationToken ct)
    {
        var row = await db.CombatProfiles.AsNoTracking().SingleOrDefaultAsync(x => x.CharacterId == characterId, ct);
        return row is null ? null : new(JsonSerializer.Deserialize<CombatProfileState>(row.StateJson, CombatCatalog.Json)!);
    }
    public async Task<CombatEncounter?> GetEncounterAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Encounters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : new(JsonSerializer.Deserialize<EncounterState>(row.StateJson, CombatCatalog.Json)! with { Revision = row.Revision });
    }
    public Task<bool> IsEnrolledAsync(Guid characterId, CancellationToken ct) => db.CombatMemberships.AsNoTracking().AnyAsync(x => x.CharacterId == characterId, ct);
    public async Task SaveProfileAsync(Character character, CombatProfile profile, CampaignEvent entry, CancellationToken ct,
        InventoryState? inventory = null)
    {
        try
        {
            UpdateCharacter(character); await UpsertProfile(profile, ct); db.Events.Add(SqliteCampaignStore.ToRow(entry));
            if (inventory is not null)
            {
                inventory.Validate();
                var row=await db.InventoryStates.SingleOrDefaultAsync(x=>x.OwnerId==character.Id,ct);
                if (row is null) { row=new() { OwnerId=character.Id }; db.InventoryStates.Add(row); }
                row.ItemsJson=JsonSerializer.Serialize(inventory.Items,CombatCatalog.Json);
                row.CopperPieces=inventory.CopperPieces;
            }
            await Commit(ct);
        }
        finally { db.ChangeTracker.Clear(); }
    }
    public async Task SaveEncounterAsync(CombatEncounter encounter, IReadOnlyList<Character> characters,
        IReadOnlyList<CombatProfile> profiles, IReadOnlyList<CampaignEvent> events, bool create, CancellationToken ct,
        IReadOnlyDictionary<Guid, ProgressionState>? progressionUpdates = null,
        Campaign? clockBefore = null, Campaign? clockAfter = null,
        IReadOnlyDictionary<Guid,MonsterInstance>? monsterUpdates = null,
        EncounterRewardState? rewardState = null,
        IReadOnlyDictionary<Guid,InventoryState>? inventoryUpdates = null)
    {
        try
        {
            if ((clockBefore is null) != (clockAfter is null) ||
                clockBefore is not null && (clockBefore.Id != encounter.State.CampaignId ||
                    clockAfter!.Id != clockBefore.Id || clockAfter.Revision != clockBefore.Revision + 1 ||
                    clockAfter.GameSeconds != clockBefore.GameSeconds + 6))
                throw new RuleViolation("Invalid combat round time transition.");
            var state = encounter.State;
            var row = new EncounterRow { Id = state.Id, CampaignId = state.CampaignId, Revision = state.Revision,
                StateJson = JsonSerializer.Serialize(state, CombatCatalog.Json) };
            if (create) db.Encounters.Add(row);
            else
            {
                db.Attach(row); row.Revision = checked(state.Revision + 1); db.Entry(row).State = EntityState.Modified;
                db.Entry(row).Property(x => x.Revision).OriginalValue = state.Revision;
            }
            foreach (var character in characters) UpdateCharacter(character);
            foreach (var profile in profiles) await UpsertProfile(profile, ct);
            if (progressionUpdates is not null) foreach (var (characterId, progression) in progressionUpdates)
            {
                var progressionRow = await db.Progressions.SingleAsync(x => x.CharacterId == characterId,ct);
                progressionRow.StateJson = JsonSerializer.Serialize(progression,CombatCatalog.Json);
            }
            if (monsterUpdates is not null) foreach (var (monsterId,monster) in monsterUpdates)
            {
                if (monsterId != monster.Id || monster.CampaignId != state.CampaignId)
                    throw new RuleViolation("Invalid monster update identity.");
                var monsterRow = await db.MonsterInstances.SingleAsync(x=>x.Id==monsterId,ct);
                monsterRow.LimitedUsesJson=JsonSerializer.Serialize(monster.LimitedUsesRemaining ?? [],CombatCatalog.Json);
                monsterRow.Revision=checked(monster.Revision+1);
                db.Entry(monsterRow).Property(x=>x.Revision).OriginalValue=monster.Revision;
            }
            if (inventoryUpdates is not null) foreach (var (ownerId,inventory) in inventoryUpdates)
            {
                inventory.Validate();
                if (!state.Combatants.Any(x=>x.CharacterId==ownerId))
                    throw new RuleViolation("Inventory owner is not in this encounter.");
                var inventoryRow=await db.InventoryStates.SingleAsync(x=>x.OwnerId==ownerId,ct);
                inventoryRow.ItemsJson=JsonSerializer.Serialize(inventory.Items,CombatCatalog.Json);
                inventoryRow.CopperPieces=inventory.CopperPieces;
            }
            if (rewardState is not null)
            {
                if (state.Status!=EncounterStatus.Completed || rewardState.EncounterId!=state.Id)
                    throw new RuleViolation("Rewards require a completed encounter.");
                db.EncounterRewards.Add(new() { EncounterId=state.Id,
                    Outcome=rewardState.Outcome.ToString(),
                    AvailableExperience=rewardState.AvailableExperience,
                    AwardsJson=JsonSerializer.Serialize(rewardState.Awards,CombatCatalog.Json),
                    DefeatedMonsterIdsJson=JsonSerializer.Serialize(rewardState.DefeatedMonsterIds,CombatCatalog.Json),
                    Revision=0 });
            }
            if (clockBefore is not null)
            {
                var campaign = await db.Campaigns.SingleAsync(x => x.Id == clockBefore.Id, ct);
                campaign.GameSeconds = clockAfter!.GameSeconds;
                campaign.Revision = clockAfter.Revision;
                db.Entry(campaign).Property(x => x.Revision).OriginalValue = clockBefore.Revision;
            }
            var memberships = await db.CombatMemberships.Where(x => x.EncounterId == state.Id).ToArrayAsync(ct);
            if (state.Status == EncounterStatus.Completed) db.CombatMemberships.RemoveRange(memberships);
            else foreach (var member in state.Combatants.Where(x => memberships.All(m => m.CharacterId != x.CharacterId)))
                db.CombatMemberships.Add(new() { CharacterId = member.CharacterId, EncounterId = state.Id });
            db.Events.AddRange(events.Select(SqliteCampaignStore.ToRow));
            await Commit(ct);
        }
        finally { db.ChangeTracker.Clear(); }
    }
    private void UpdateCharacter(Character character)
    {
        var row = SqliteCampaignStore.ToRow(character); db.Attach(row); row.Revision = checked(character.Revision + 1);
        db.Entry(row).State = EntityState.Modified; db.Entry(row).Property(x => x.Revision).OriginalValue = character.Revision;
    }
    private async Task UpsertProfile(CombatProfile profile, CancellationToken ct)
    {
        var row = await db.CombatProfiles.SingleOrDefaultAsync(x => x.CharacterId == profile.State.CharacterId, ct);
        if (row is null) { row = new() { CharacterId = profile.State.CharacterId }; db.CombatProfiles.Add(row); }
        row.StateJson = JsonSerializer.Serialize(profile.State, CombatCatalog.Json);
    }
    private async Task Commit(CancellationToken ct)
    {
        // One SaveChanges transaction includes all HP, profiles, resources, membership and events.
        // No retry: consumed random rolls must never be silently replaced.
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new StateConflictException("Combat or character changed concurrently. Reload before trying again."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        { throw new StateConflictException("Combat write conflicted with existing state. Reload before trying again."); }
    }

    public async Task<EncounterRewardState?> GetAsync(Guid encounterId,CancellationToken ct)
    {
        var row=await db.EncounterRewards.AsNoTracking().SingleOrDefaultAsync(x=>x.EncounterId==encounterId,ct);
        return row is null ? null : new(row.EncounterId,Enum.Parse<EncounterOutcome>(row.Outcome),
            row.AvailableExperience,JsonSerializer.Deserialize<ExperienceAward[]>(row.AwardsJson,CombatCatalog.Json)!,
            JsonSerializer.Deserialize<Guid[]>(row.DefeatedMonsterIdsJson,CombatCatalog.Json)!,row.Revision);
    }
    public async Task AwardExperienceAsync(EncounterRewardState before,EncounterRewardState after,
        CampaignEvent entry,CancellationToken ct)
    {
        if (before.EncounterId!=after.EncounterId || after.Revision!=before.Revision+1 ||
            before.AvailableExperience!=after.AvailableExperience || before.Outcome!=after.Outcome ||
            !before.DefeatedMonsterIds.SequenceEqual(after.DefeatedMonsterIds))
            throw new RuleViolation("Invalid reward transition.");
        try
        {
            var row=new EncounterRewardRow { EncounterId=before.EncounterId,
                Outcome=before.Outcome.ToString(),AvailableExperience=before.AvailableExperience,
                AwardsJson=JsonSerializer.Serialize(after.Awards,CombatCatalog.Json),
                DefeatedMonsterIdsJson=JsonSerializer.Serialize(before.DefeatedMonsterIds,CombatCatalog.Json),
                Revision=after.Revision };
            db.Attach(row); db.Entry(row).State=EntityState.Modified;
            db.Entry(row).Property(x=>x.Revision).OriginalValue=before.Revision;
            db.Events.Add(SqliteCampaignStore.ToRow(entry));
            await Commit(ct);
        }
        finally { db.ChangeTracker.Clear(); }
    }
}
