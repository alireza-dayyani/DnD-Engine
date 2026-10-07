using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Progression;
using DndEngine.Domain.Combat;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class SpellContentRow
{
    public string RulesetId { get; set; } = "";
    public string Version { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string DataJson { get; set; } = "";
}

public sealed class SpellPackRow
{
    public string RulesetId { get; set; } = "";
    public string RulesetVersion { get; set; } = "";
    public string PackVersion { get; set; } = "";
    public string ContentHash { get; set; } = "";
    public string DataJson { get; set; } = "";
}

public sealed class SpellCatalog(RulesDbContext db) : ISpellCatalog
{
    private sealed record InitialSpellDefinition(string Id, string Name, int Level, string[] ClassIds,
        string CastingTime, string Range, string Components, SpellEffectKind Effect, string Source);

    public async Task<SpellDefinition[]> GetAsync(Ruleset ruleset, string packVersion, CancellationToken ct)
    {
        ruleset.RequireSupported();
        string json;
        if (packVersion == SpellPackVersions.Initial)
            json = (await db.SpellContent.AsNoTracking().SingleOrDefaultAsync(x =>
                x.RulesetId == ruleset.Id && x.Version == ruleset.Version,ct))?.DataJson
                ?? throw new RuleViolation("Initial spell pack is not installed for this ruleset.");
        else
            json = (await db.SpellPacks.AsNoTracking().SingleOrDefaultAsync(x =>
                x.RulesetId == ruleset.Id && x.RulesetVersion == ruleset.Version && x.PackVersion == packVersion,ct))?.DataJson
                ?? throw new RuleViolation("Spell pack is not installed for this ruleset.");
        var spells = JsonSerializer.Deserialize<SpellDefinition[]>(json,CombatCatalog.Json)!;
        return packVersion == SpellPackVersions.Initial
            ? spells.Select(x => x.Id == "cure-wounds" ? x with { DicePerSlotLevel=2,DieSides=8 } : x).ToArray()
            : spells;
    }

    public static async Task ImportAsync(RulesDbContext db, CancellationToken ct)
    {
        // Keep the original serialized shape byte-for-byte so existing pinned databases still start.
        InitialSpellDefinition[] initial = [new("cure-wounds","Cure Wounds",1,
            ["bard","cleric","druid","paladin","ranger"],"Action","Touch","V,S",
            SpellEffectKind.SelfHealing,"SRD 5.2.1 p. 121")];
        await ImportInitialAsync(db,JsonSerializer.Serialize(initial,CombatCatalog.Json),ct);
        SpellDefinition[] previous = [
            new("cure-wounds","Cure Wounds",1,["bard","cleric","druid","paladin","ranger"],
                "Action","Touch","V,S",SpellEffectKind.SelfHealing,"SRD 5.2.1 p. 121",2,8),
            new("healing-word","Healing Word",1,["bard","cleric","druid"],
                "Bonus Action","60 feet","V",SpellEffectKind.SelfHealing,"SRD 5.2.1 p. 139",2,4)
        ];
        await ImportPackAsync(db,SpellPackVersions.Second,previous,ct);
        SpellDefinition[] third = [..previous,
            new("fire-bolt","Fire Bolt",0,["sorcerer","wizard"],"Action","120 feet","V,S",
                SpellEffectKind.SpellAttack,"SRD 5.2.1 p. 131",Damage:new(DamageType.Fire,1,10)),
            new("sacred-flame","Sacred Flame",0,["cleric"],"Action","60 feet","V,S",
                SpellEffectKind.SavingThrowDamage,"SRD 5.2.1 p. 158",Damage:new(DamageType.Radiant,1,8,Ability.Dexterity,
                    IgnoreCover:true)),
            new("burning-hands","Burning Hands",1,["sorcerer","wizard"],"Action","Self (15-foot Cone)","V,S",
                SpellEffectKind.SavingThrowDamage,"SRD 5.2.1 p. 116",Damage:new(DamageType.Fire,3,6,
                Ability.Dexterity,1,HalfOnSave:true,Area:true)),
            new("blur","Blur",2,["sorcerer","wizard"],"Action","Self","V",
                SpellEffectKind.Blur,"SRD 5.2.1 p. 114",ConcentrationTurns:10),
            new("circle-of-death","Circle of Death",6,["sorcerer","warlock","wizard"],"Action",
                "150 feet","V,S,M",SpellEffectKind.SavingThrowDamage,"SRD 5.2.1 p. 115",
                Damage:new(DamageType.Necrotic,8,8,Ability.Constitution,2,HalfOnSave:true,Area:true),
                MaterialCostGp:500)
        ];
        await ImportPackAsync(db,SpellPackVersions.Previous,third,ct);
        SpellDefinition[] current = [..third.Select(x => x.Id == "circle-of-death"
            ? x with { MaterialItemId="black-pearl-powder-500gp" } : x),
            new("poison-spray","Poison Spray",0,["druid","sorcerer","warlock","wizard"],
                "Action","30 feet","V,S",SpellEffectKind.SpellAttack,"SRD 5.2.1 p. 153",
                Damage:new(DamageType.Poison,1,12)),
            new("eldritch-blast","Eldritch Blast",0,["warlock"],
                "Action","120 feet","V,S",SpellEffectKind.SpellAttack,"SRD 5.2.1 p. 127",
                Damage:new(DamageType.Force,1,10)),
            new("vicious-mockery","Vicious Mockery",0,["bard"],
                "Action","60 feet","V",SpellEffectKind.SavingThrowDamage,"SRD 5.2.1 p. 172",
                Damage:new(DamageType.Psychic,1,6,Ability.Wisdom))
        ];
        await ImportPackAsync(db,SpellPackVersions.Fourth,current,ct);
        SpellDefinition[] fifth = [..current,
            new("hellish-rebuke","Hellish Rebuke",1,["warlock"],"Reaction","60 feet","V,S",
                SpellEffectKind.SavingThrowDamage,"SRD 5.2.1 p. 140",
                Damage:new(DamageType.Fire,2,10,Ability.Dexterity,1,HalfOnSave:true))
        ];
        await ImportPackAsync(db,SpellPackVersions.Fifth,fifth,ct);
        SpellDefinition[] sixth = [..fifth,
            new("comprehend-languages","Comprehend Languages",1,
                ["bard","sorcerer","warlock","wizard"],"Action or Ritual","Self","V,S,M",
                SpellEffectKind.LanguageComprehension,"SRD 5.2.1 p. 117",Ritual:true)
        ];
        await ImportPackAsync(db,SpellPackVersions.Current,sixth,ct);
    }

    private static async Task ImportPackAsync(RulesDbContext db,string version,SpellDefinition[] spells,CancellationToken ct)
    {
        var json = JsonSerializer.Serialize(spells,CombatCatalog.Json);
        var hash = Hash(json);
        var pack = await db.SpellPacks.SingleOrDefaultAsync(x => x.RulesetId == Ruleset.Current.Id &&
            x.RulesetVersion == Ruleset.Current.Version && x.PackVersion == version,ct);
        if (pack is not null)
        {
            if (pack.ContentHash != hash || pack.DataJson != json)
                throw new InvalidOperationException("Pinned spell pack differs; refusing to overwrite it.");
            return;
        }
        db.SpellPacks.Add(new() { RulesetId=Ruleset.Current.Id,RulesetVersion=Ruleset.Current.Version,
            PackVersion=version,ContentHash=hash,DataJson=json });
        await db.SaveChangesAsync(ct);
    }

    private static async Task ImportInitialAsync(RulesDbContext db, string json, CancellationToken ct)
    {
        var hash = Hash(json);
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

    private static string Hash(string json) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
}
