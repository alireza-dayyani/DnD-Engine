using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain.Combat;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class SqliteEncounterIndex(CampaignDbContext db) : IEncounterIndex
{
    public async Task<Guid?> CurrentAsync(Guid campaignId,CancellationToken ct)
    {
        // Scan the campaign's stored states so an older active encounter cannot
        // disappear behind a fixed candidate window of unrelated encounters.
        var rows=await db.Encounters.AsNoTracking().Where(x=>x.CampaignId==campaignId)
            .OrderByDescending(x=>x.Id).Select(x=>new { x.Id,x.StateJson })
            .ToArrayAsync(ct);
        foreach(var row in rows)
        {
            var state=JsonSerializer.Deserialize<EncounterState>(row.StateJson,CombatCatalog.Json);
            if (state?.Status==EncounterStatus.Active) return row.Id;
        }
        return null;
    }
}
