using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Progression;
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
public sealed class SqliteCombatStore(CampaignDbContext db) : ICombatStore
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
    public async Task SaveProfileAsync(Character character, CombatProfile profile, CampaignEvent entry, CancellationToken ct)
    {
        try
        {
            UpdateCharacter(character); await UpsertProfile(profile, ct); db.Events.Add(SqliteCampaignStore.ToRow(entry));
            await Commit(ct);
        }
        finally { db.ChangeTracker.Clear(); }
    }
    public async Task SaveEncounterAsync(CombatEncounter encounter, IReadOnlyList<Character> characters,
        IReadOnlyList<CombatProfile> profiles, IReadOnlyList<CampaignEvent> events, bool create, CancellationToken ct,
        IReadOnlyDictionary<Guid, ProgressionState>? progressionUpdates = null)
    {
        try
        {
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
}
