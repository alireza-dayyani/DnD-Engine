using System.Security.Cryptography;
using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Progression;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class CharacterContentRow
{
    public string RulesetId { get; set; } = "";
    public string Version { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string DataJson { get; set; } = "";
}

public sealed class CharacterRulesCatalog(RulesDbContext db) : ICharacterRulesCatalog
{
    public async Task<CharacterRules> GetAsync(Ruleset ruleset, CancellationToken ct)
    {
        ruleset.RequireSupported();
        var row = await db.CharacterContent.AsNoTracking().SingleOrDefaultAsync(x => x.RulesetId == ruleset.Id && x.Version == ruleset.Version, ct)
            ?? throw new RuleViolation("Character content is not installed for this ruleset.");
        return JsonSerializer.Deserialize<CharacterRules>(row.DataJson, CombatCatalog.Json)!;
    }
    public static async Task ImportAsync(RulesDbContext db, CancellationToken ct)
    {
        var content = CharacterRulesSeed.Create();
        if (content.Species.Length != 9 || content.Backgrounds.Length != 4 || content.Classes.Length != 12 ||
            content.Items.Count(x => x.Kind is ItemKind.Armor or ItemKind.Shield) != 13 ||
            content.Classes.Any(x => x.HitDie is not (6 or 8 or 10 or 12)))
            throw new InvalidOperationException("Invalid character content pack.");
        var json = JsonSerializer.Serialize(content,CombatCatalog.Json);
        var hash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(json)));
        var existing = await db.CharacterContent.SingleOrDefaultAsync(x => x.RulesetId == Ruleset.Current.Id && x.Version == Ruleset.Current.Version, ct);
        if (existing is not null)
        {
            if (existing.ContentHash != hash || existing.DataJson != json) throw new InvalidOperationException("Pinned character content differs; refusing to overwrite it.");
            return;
        }
        db.CharacterContent.Add(new() { RulesetId = Ruleset.Current.Id, Version = Ruleset.Current.Version, ContentHash = hash, DataJson = json });
        await db.SaveChangesAsync(ct);
    }
}
