using System.Security.Cryptography;
using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using Microsoft.EntityFrameworkCore;
namespace DndEngine.Infrastructure;

public sealed class RulesCatalog(RulesDbContext db) : IRulesCatalog
{
    public async Task RequireRulesetAsync(Ruleset ruleset, CancellationToken ct)
    {
        if (!await db.Rulesets.AnyAsync(x => x.Id == ruleset.Id && x.Version == ruleset.Version, ct))
            throw new RuleViolation("Ruleset content is not installed.");
    }
    public async Task<SkillDefinition> GetSkillAsync(Ruleset ruleset, string id, CancellationToken ct)
    {
        var row = await db.Skills.AsNoTracking().SingleOrDefaultAsync(x => x.RulesetId == ruleset.Id && x.Version == ruleset.Version && x.Id == id, ct)
            ?? throw new RuleViolation($"Unknown skill ID '{id}' for {ruleset.Version}.");
        return new(row.Id, row.Name, Enum.Parse<Ability>(row.Ability), row.Source);
    }
    public static async Task ImportAsync(RulesDbContext db, CancellationToken ct = default)
    {
        await using var stream = typeof(RulesCatalog).Assembly.GetManifestResourceStream("DndEngine.Infrastructure.Content.skills-5.2.1.json")!;
        using var memory = new MemoryStream();
        await stream.CopyToAsync(memory, ct);
        var bytes = memory.ToArray();
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var content = JsonSerializer.Deserialize<SkillSeed[]>(bytes, new JsonSerializerOptions { PropertyNameCaseInsensitive = true })!;
        if (content.Length != 18 || content.Select(x => x.Id).Distinct().Count() != content.Length)
            throw new InvalidOperationException("Invalid skill content pack.");
        foreach (var item in content) Guard.Defined(Enum.Parse<Ability>(item.Ability));
        var existing = await db.Rulesets.SingleOrDefaultAsync(x => x.Id == Ruleset.Current.Id && x.Version == Ruleset.Current.Version, ct);
        if (existing is not null)
        {
            var installed = await db.Skills.AsNoTracking().Where(x => x.RulesetId == existing.Id && x.Version == existing.Version).ToArrayAsync(ct);
            if (existing.ContentHash != hash || installed.Length != content.Length || content.Any(s => !installed.Any(x =>
                x.Id == s.Id && x.Name == s.Name && x.Ability == s.Ability && x.Source == "SRD-5.2.1 p.9")))
                throw new InvalidOperationException("Pinned rules content differs; refusing to overwrite it.");
            return;
        }
        db.Rulesets.Add(new() { Id = Ruleset.Current.Id, Version = Ruleset.Current.Version,
            SourceUrl = "https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf", License = "CC-BY-4.0", ContentHash = hash });
        db.Skills.AddRange(content.Select(x => new SkillRow { RulesetId = Ruleset.Current.Id, Version = Ruleset.Current.Version,
            Id = x.Id, Name = x.Name, Ability = x.Ability, Source = "SRD-5.2.1 p.9" }));
        await db.SaveChangesAsync(ct);
    }
    private sealed record SkillSeed(string Id, string Name, string Ability);
}
