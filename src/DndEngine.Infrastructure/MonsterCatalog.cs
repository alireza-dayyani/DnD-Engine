using System.Security.Cryptography;
using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Monsters;
using DndEngine.Domain.Progression;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class MonsterPackRow
{
    public string RulesetId { get; set; } = "";
    public string RulesetVersion { get; set; } = "";
    public string PackVersion { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string DataJson { get; set; } = "";
}

public sealed class MonsterCatalog(RulesDbContext db) : IMonsterCatalog
{
    public async Task<MonsterPack> GetAsync(Ruleset ruleset, string packVersion, CancellationToken ct)
    {
        ruleset.RequireSupported();
        var row = await db.MonsterPacks.AsNoTracking().SingleOrDefaultAsync(x =>
            x.RulesetId == ruleset.Id && x.RulesetVersion == ruleset.Version &&
            x.PackVersion == packVersion, ct) ?? throw new RuleViolation("Monster pack is not installed.");
        return JsonSerializer.Deserialize<MonsterPack>(row.DataJson, CombatCatalog.Json)!;
    }

    public static async Task ImportAsync(RulesDbContext db, CancellationToken ct)
    {
        await using var stream = typeof(MonsterCatalog).Assembly.GetManifestResourceStream(
            "DndEngine.Infrastructure.Content.monsters-5.2.1-pack1.json")!;
        using var buffer = new MemoryStream(); await stream.CopyToAsync(buffer, ct);
        var bytes = buffer.ToArray();
        var pack = JsonSerializer.Deserialize<MonsterPack>(bytes, CombatCatalog.Json)!;
        if (pack.Version != "1" || pack.Monsters.Length != 3 ||
            pack.Monsters.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != pack.Monsters.Length)
            throw new InvalidOperationException("Invalid monster pack.");
        foreach (var monster in pack.Monsters) monster.Validate();
        var combat = await db.CombatContent.AsNoTracking().SingleAsync(x =>
            x.RulesetId == Ruleset.Current.Id && x.Version == Ruleset.Current.Version, ct);
        var weapons = JsonSerializer.Deserialize<DndEngine.Domain.Combat.CombatContent>(
            combat.DataJson, CombatCatalog.Json)!.Weapons;
        var characterRow=await db.CharacterContent.AsNoTracking().SingleAsync(x=>
            x.RulesetId==Ruleset.Current.Id && x.Version==Ruleset.Current.Version,ct);
        var characterRules=JsonSerializer.Deserialize<CharacterRules>(
            characterRow.DataJson,CombatCatalog.Json)!;
        var spellRow=await db.SpellPacks.AsNoTracking().SingleAsync(x=>
            x.RulesetId==Ruleset.Current.Id && x.RulesetVersion==Ruleset.Current.Version &&
            x.PackVersion==SpellPackVersions.Current,ct);
        var spells=JsonSerializer.Deserialize<SpellDefinition[]>(spellRow.DataJson,CombatCatalog.Json)!;
        foreach (var monster in pack.Monsters)
        {
            if (monster.Gear.Any(x=>weapons.All(w=>w.Id!=x.DefinitionId) &&
                characterRules.Items.All(y=>y.Id!=x.DefinitionId)))
                throw new InvalidOperationException("Monster gear has no installed item definition.");
            if (monster.Spells.Where(x=>x.Supported).Any(x=>
                spells.All(s=>s.Id!=x.SpellId) || x.SpellId!="healing-word"))
                throw new InvalidOperationException("Supported monster spell has no tested resolver.");
        }
        foreach (var monster in pack.Monsters)
        foreach (var action in monster.Actions.Where(x => x.Supported))
        {
            var weapon = weapons.SingleOrDefault(x => x.Id == action.WeaponId)
                ?? throw new InvalidOperationException("Supported monster attack has no weapon definition.");
            if (weapon.DamageDice != action.DamageDice || weapon.DamageType != action.DamageType ||
                action.DamageModifier != action.AttackBonus - 2 ||
                weapon.NormalRange != action.NormalRangeFeet ||
                weapon.LongRange != action.LongRangeFeet ||
                action.AttackBonus != monster.Abilities[DndEngine.Domain.Ability.Dexterity] / 2 - 5 + 2 &&
                action.AttackBonus != monster.Abilities[DndEngine.Domain.Ability.Strength] / 2 - 5 + 2)
                throw new InvalidOperationException("Monster attack cannot use the existing weapon resolver exactly.");
        }
        var json = JsonSerializer.Serialize(pack, CombatCatalog.Json);
        var hash = Convert.ToHexString(SHA256.HashData(bytes));
        var existing = await db.MonsterPacks.SingleOrDefaultAsync(x =>
            x.RulesetId == Ruleset.Current.Id && x.RulesetVersion == Ruleset.Current.Version &&
            x.PackVersion == pack.Version, ct);
        if (existing is not null)
        {
            if (existing.ContentHash != hash || existing.DataJson != json)
                throw new InvalidOperationException("Pinned monster content differs; refusing to overwrite it.");
            return;
        }
        db.MonsterPacks.Add(new() { RulesetId = Ruleset.Current.Id,
            RulesetVersion = Ruleset.Current.Version, PackVersion = pack.Version,
            ContentHash = hash, DataJson = json });
        await db.SaveChangesAsync(ct);
    }
}
