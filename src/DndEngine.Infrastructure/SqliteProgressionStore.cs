using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Progression;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class ProgressionRow
{
    public Guid CharacterId { get; set; }
    public string StateJson { get; set; } = "";
}

public sealed class SqliteProgressionStore(CampaignDbContext db) : IProgressionStore
{
    public async Task<ProgressionState?> GetAsync(Guid characterId, CancellationToken ct)
    {
        var row = await db.Progressions.AsNoTracking().SingleOrDefaultAsync(x => x.CharacterId == characterId,ct);
        return row is null ? null : JsonSerializer.Deserialize<ProgressionState>(row.StateJson,CombatCatalog.Json);
    }
    public Task<bool> IsEnrolledAsync(Guid characterId, CancellationToken ct) =>
        db.CombatMemberships.AsNoTracking().AnyAsync(x => x.CharacterId == characterId,ct);
    public async Task CreateAsync(Character character, ProgressionState state, CombatProfile profile, CampaignEvent entry, CancellationToken ct)
    {
        db.Characters.Add(SqliteCampaignStore.ToRow(character));
        db.Progressions.Add(new() { CharacterId = character.Id, StateJson = JsonSerializer.Serialize(state,CombatCatalog.Json) });
        db.CombatProfiles.Add(new() { CharacterId = character.Id, StateJson = JsonSerializer.Serialize(profile.State,CombatCatalog.Json) });
        db.Events.Add(SqliteCampaignStore.ToRow(entry));
        await Commit(ct);
    }
    public async Task SaveAsync(Character character, ProgressionState state, CombatProfile profile, CampaignEvent entry, CancellationToken ct)
    {
        var row = SqliteCampaignStore.ToRow(character);
        db.Attach(row); row.Revision = checked(character.Revision + 1);
        db.Entry(row).State = EntityState.Modified;
        db.Entry(row).Property(x => x.Revision).OriginalValue = character.Revision;
        var progression = await db.Progressions.SingleAsync(x => x.CharacterId == character.Id,ct);
        progression.StateJson = JsonSerializer.Serialize(state,CombatCatalog.Json);
        var combat = await db.CombatProfiles.SingleAsync(x => x.CharacterId == character.Id,ct);
        combat.StateJson = JsonSerializer.Serialize(profile.State,CombatCatalog.Json);
        db.Events.Add(SqliteCampaignStore.ToRow(entry));
        await Commit(ct);
    }
    private async Task Commit(CancellationToken ct)
    {
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new StateConflictException("Character changed concurrently. Reload before trying again."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        { throw new StateConflictException("Character write conflicted with existing state. Reload before trying again."); }
        finally { db.ChangeTracker.Clear(); }
    }
}
