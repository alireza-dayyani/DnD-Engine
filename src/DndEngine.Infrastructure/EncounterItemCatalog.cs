using System.Security.Cryptography;
using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Inventory;
using DndEngine.Domain.Progression;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class EncounterItemPackRow
{
    public string RulesetId { get; set; } = "";
    public string RulesetVersion { get; set; } = "";
    public string PackVersion { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string DataJson { get; set; } = "";
}

public sealed class EncounterItemCatalog(RulesDbContext db) : IEncounterItemCatalog
{
    public async Task<EncounterItemPack> GetAsync(Ruleset ruleset,string packVersion,CancellationToken ct)
    {
        ruleset.RequireSupported();
        var row=await db.EncounterItemPacks.AsNoTracking().SingleOrDefaultAsync(x=>
            x.RulesetId==ruleset.Id && x.RulesetVersion==ruleset.Version &&
            x.PackVersion==packVersion,ct) ?? throw new RuleViolation("Item pack is not installed.");
        return JsonSerializer.Deserialize<EncounterItemPack>(row.DataJson,CombatCatalog.Json)!;
    }

    public static async Task ImportAsync(RulesDbContext db,CancellationToken ct)
    {
        await using var stream=typeof(EncounterItemCatalog).Assembly.GetManifestResourceStream(
            "DndEngine.Infrastructure.Content.items-5.2.1-pack1.json")!;
        using var buffer=new MemoryStream(); await stream.CopyToAsync(buffer,ct);
        var bytes=buffer.ToArray();
        var pack=JsonSerializer.Deserialize<EncounterItemPack>(bytes,CombatCatalog.Json)!;
        if (pack.Version!="1" || pack.Items.Length==0 ||
            pack.Items.Select(x=>x.Id).Distinct(StringComparer.Ordinal).Count()!=pack.Items.Length ||
            pack.Items.Any(x=>string.IsNullOrWhiteSpace(x.Id) || string.IsNullOrWhiteSpace(x.Name) ||
                x.Kind!=ItemKind.Gear || x.PriceCopper<0 || x.Consumable &&
                x.EffectId!="heal-2d4-plus-2"))
            throw new InvalidOperationException("Invalid Phase 5 item pack.");
        var json=JsonSerializer.Serialize(pack,CombatCatalog.Json);
        var hash=Convert.ToHexString(SHA256.HashData(bytes));
        var existing=await db.EncounterItemPacks.SingleOrDefaultAsync(x=>
            x.RulesetId==Ruleset.Current.Id && x.RulesetVersion==Ruleset.Current.Version &&
            x.PackVersion==pack.Version,ct);
        if (existing is not null)
        {
            if (existing.ContentHash!=hash || existing.DataJson!=json)
                throw new InvalidOperationException("Pinned item content differs; refusing to overwrite it.");
            return;
        }
        db.EncounterItemPacks.Add(new() { RulesetId=Ruleset.Current.Id,
            RulesetVersion=Ruleset.Current.Version,PackVersion=pack.Version,
            ContentHash=hash,DataJson=json });
        await db.SaveChangesAsync(ct);
    }
}
