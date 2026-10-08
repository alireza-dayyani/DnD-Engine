using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.World;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class WorldStateRow
{
    public Guid CampaignId { get; set; }
    public long Revision { get; set; }
    public string StateJson { get; set; } = "";
}

public sealed class SqliteWorldStore(CampaignDbContext db) : IWorldStore
{
    public async Task<CampaignEvent[]> GetEventsAsync(Guid campaignId,long after,int limit,
        IReadOnlyCollection<string> types,bool includeTypes,CancellationToken ct)
    {
        var allowed=types.ToArray();
        var rows=await db.Events.AsNoTracking()
            .Where(x=>x.CampaignId==campaignId && x.Sequence>after &&
                (includeTypes ? allowed.Contains(x.Type) : !allowed.Contains(x.Type)))
            .OrderBy(x=>x.Sequence).Take(limit).ToArrayAsync(ct);
        return rows.Select(x=>new CampaignEvent(x.Sequence,x.EventId,x.CampaignId,x.CharacterId,
            x.Type,x.OccurredAtUtc,new(x.RulesetId,x.SrdVersion),x.SchemaVersion,
            x.CharacterRevision,JsonSerializer.Deserialize<JsonElement>(x.DataJson))).ToArray();
    }

    public async Task<WorldState?> GetAsync(Guid campaignId,CancellationToken ct)
    {
        var row=await db.WorldStates.AsNoTracking()
            .SingleOrDefaultAsync(x=>x.CampaignId==campaignId,ct);
        return row is null ? null :
            JsonSerializer.Deserialize<WorldState>(row.StateJson,CombatCatalog.Json)! with {
                Revision=row.Revision };
    }

    public async Task SaveAsync(WorldState before,WorldState after,Campaign campaign,
        IReadOnlyList<CampaignEvent> events,CancellationToken ct)
    {
        if (before.CampaignId!=campaign.Id || after.CampaignId!=campaign.Id ||
            after.Revision!=before.Revision+1 || events.Count==0 ||
            events.Any(x=>x.CampaignId!=campaign.Id))
            throw new RuleViolation("Invalid world transaction.");
        try
        {
            var campaignRow=await db.Campaigns.SingleAsync(x=>x.Id==campaign.Id,ct);
            if (campaignRow.GameSeconds!=campaign.GameSeconds ||
                campaignRow.Revision!=campaign.Revision)
                throw new StateConflictException("Campaign clock changed. Reload the world before writing.");
            campaignRow.Revision=checked(campaign.Revision+1);
            db.Entry(campaignRow).Property(x=>x.Revision).OriginalValue=campaign.Revision;

            var row=await db.WorldStates.SingleOrDefaultAsync(x=>x.CampaignId==campaign.Id,ct);
            if (row is null)
            {
                if (before.Revision!=0) throw new StateConflictException("World state changed.");
                row=new() { CampaignId=campaign.Id,Revision=after.Revision };
                db.WorldStates.Add(row);
            }
            else
            {
                if (row.Revision!=before.Revision)
                    throw new StateConflictException("World state changed. Reload before writing.");
                row.Revision=after.Revision;
                db.Entry(row).Property(x=>x.Revision).OriginalValue=before.Revision;
            }
            row.StateJson=JsonSerializer.Serialize(after,CombatCatalog.Json);
            db.Events.AddRange(events.Select(SqliteCampaignStore.ToRow));
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        { throw new StateConflictException("World or campaign clock changed concurrently. Reload before writing."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException
            { SqliteErrorCode: 19 or 5 or 6 })
        { throw new StateConflictException("World write conflicted with existing state. Reload before writing."); }
        catch (SqliteException ex) when (ex.SqliteErrorCode is 5 or 6)
        { throw new StateConflictException("World database was busy. Reload before writing."); }
        finally { db.ChangeTracker.Clear(); }
    }
}
