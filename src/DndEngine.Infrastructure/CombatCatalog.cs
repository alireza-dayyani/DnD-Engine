using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class CombatContentRow
{
    public string RulesetId { get; set; } = "";
    public string Version { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string DataJson { get; set; } = "";
}
public sealed class CombatCatalog(RulesDbContext db) : ICombatCatalog
{
    internal static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    public async Task<CombatContent> GetAsync(Ruleset ruleset, CancellationToken ct)
    {
        ruleset.RequireSupported();
        var row = await db.CombatContent.AsNoTracking().SingleOrDefaultAsync(x => x.RulesetId == ruleset.Id && x.Version == ruleset.Version, ct)
            ?? throw new RuleViolation("Combat content is not installed for this ruleset.");
        return JsonSerializer.Deserialize<CombatContent>(row.DataJson, Json)!;
    }
    public static async Task ImportAsync(RulesDbContext db, CancellationToken ct)
    {
        await using var stream = typeof(CombatCatalog).Assembly.GetManifestResourceStream("DndEngine.Infrastructure.Content.combat-5.2.1.json")!;
        using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray(); var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var content = JsonSerializer.Deserialize<CombatContent>(bytes, Json)!;
        if (content.Weapons.Length != 38 || content.Weapons.Select(x => x.Id).Distinct().Count() != 38 ||
            !content.Conditions.Select(x => x.Id).Order().SequenceEqual(Enum.GetValues<ConditionKind>().Order()) ||
            content.Masteries.Length != 8 || content.Masteries.Select(x => x.Id).Distinct().Count() != 8)
            throw new InvalidOperationException("Invalid combat content pack.");
        foreach (var weapon in content.Weapons)
        {
            weapon.Validate();
            if (!content.Masteries.Any(x => x.Id == weapon.Mastery)) throw new InvalidOperationException("Unknown weapon mastery.");
        }
        var json = JsonSerializer.Serialize(content, Json);
        var existing = await db.CombatContent.SingleOrDefaultAsync(x => x.RulesetId == Ruleset.Current.Id && x.Version == Ruleset.Current.Version, ct);
        if (existing is not null)
        {
            if (existing.ContentHash != hash || existing.DataJson != json) throw new InvalidOperationException("Pinned combat content differs; refusing to overwrite it.");
            return;
        }
        db.CombatContent.Add(new() { RulesetId = Ruleset.Current.Id, Version = Ruleset.Current.Version, ContentHash = hash, DataJson = json });
        await db.SaveChangesAsync(ct);
    }
}
