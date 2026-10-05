using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Progression;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class SpellContentRow
{
    public string RulesetId { get; set; } = "";
    public string Version { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string DataJson { get; set; } = "";
}

public sealed class SpellCatalog(RulesDbContext db) : ISpellCatalog
{
    public async Task<SpellDefinition[]> GetAsync(Ruleset ruleset, CancellationToken ct)
    {
        ruleset.RequireSupported();
        var row = await db.SpellContent.AsNoTracking().SingleOrDefaultAsync(x =>
            x.RulesetId == ruleset.Id && x.Version == ruleset.Version,ct)
            ?? throw new RuleViolation("Spell content is not installed for this ruleset.");
        return JsonSerializer.Deserialize<SpellDefinition[]>(row.DataJson,CombatCatalog.Json)!;
    }

    public static async Task ImportAsync(RulesDbContext db, CancellationToken ct)
    {
        // An intentionally narrow, executable starter catalog. Further SRD spells require a new content version.
        SpellDefinition[] content = [new("cure-wounds","Cure Wounds",1,
            ["bard","cleric","druid","paladin","ranger"],"Action","Touch","V,S",
            SpellEffectKind.SelfHealing,"SRD 5.2.1 p. 121")];
        var json = JsonSerializer.Serialize(content,CombatCatalog.Json);
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
        var existing = await db.SpellContent.SingleOrDefaultAsync(x =>
            x.RulesetId == Ruleset.Current.Id && x.Version == Ruleset.Current.Version,ct);
        if (existing is not null)
        {
            if (existing.ContentHash != hash || existing.DataJson != json)
                throw new InvalidOperationException("Pinned spell content differs; refusing to overwrite it.");
            return;
        }
        db.SpellContent.Add(new() { RulesetId=Ruleset.Current.Id,Version=Ruleset.Current.Version,
            ContentHash=hash,DataJson=json });
        await db.SaveChangesAsync(ct);
    }
}
