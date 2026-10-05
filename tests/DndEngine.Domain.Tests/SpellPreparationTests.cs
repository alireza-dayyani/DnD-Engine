using DndEngine.Domain.Progression;

namespace DndEngine.Domain.Tests;

public class SpellPreparationTests
{
    private static readonly SpellDefinition[] Catalog = [
        new("cure-wounds","Cure Wounds",1,["bard","cleric","druid","paladin","ranger"],
            "Action","Touch","V,S",SpellEffectKind.SelfHealing,"SRD",2,8),
        new("healing-word","Healing Word",1,["bard","cleric","druid"],
            "Bonus Action","60 feet","V",SpellEffectKind.SelfHealing,"SRD",2,4)
    ];

    [Fact]
    public void RestAndClassLevelReplacementRespectClassTiming()
    {
        PreparedSpell[] prepared=[new("cleric","cure-wounds"),new("bard","cure-wounds")];
        ClassLevel[] classes=[new("cleric",1),new("bard",1)];
        var rest=SpellPreparation.Replace(prepared,classes,Catalog,
            [new("cleric","cure-wounds","healing-word")],SpellPreparationMoment.LongRest);
        Assert.Contains(rest,x=>x.ClassId=="cleric" && x.SpellId=="healing-word");
        Assert.Contains(rest,x=>x.ClassId=="bard" && x.SpellId=="cure-wounds");
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Replace(prepared,classes,Catalog,
            [new("bard","cure-wounds","healing-word")],SpellPreparationMoment.LongRest));
        var level=SpellPreparation.Replace(prepared,classes,Catalog,
            [new("bard","cure-wounds","healing-word")],SpellPreparationMoment.ClassLevelGained,"bard");
        Assert.Contains(level,x=>x.ClassId=="bard" && x.SpellId=="healing-word");
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Replace(prepared,classes,Catalog,
            [new("bard","cure-wounds","healing-word")],SpellPreparationMoment.ClassLevelGained,"cleric"));
    }

    [Fact]
    public void ReplacementRequiresPreparedSourceAndEligibleUniqueTarget()
    {
        PreparedSpell[] prepared=[new("cleric","cure-wounds")];
        ClassLevel[] classes=[new("cleric",1)];
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Replace(prepared,classes,Catalog,
            [new("cleric","healing-word","cure-wounds")],SpellPreparationMoment.LongRest));
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Replace(prepared,classes,Catalog,
            [new("cleric","cure-wounds","unknown")],SpellPreparationMoment.LongRest));
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Replace(prepared,classes,Catalog,
            [new("cleric","cure-wounds","healing-word"),
             new("cleric","cure-wounds","healing-word")],SpellPreparationMoment.LongRest));
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Replace(
            [new("cleric","cure-wounds"),new("cleric","healing-word")],classes,Catalog,
            [new("cleric","cure-wounds","healing-word")],SpellPreparationMoment.LongRest));
        SpellDefinition[] paladinCatalog=[Catalog[0],new("paladin-test-spell","Test spell",1,["paladin"],
            "Action","Touch","V,S",SpellEffectKind.SelfHealing,"test")];
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Replace(
            [new("paladin","cure-wounds"),new("paladin","paladin-test-spell")],[new("paladin",1)],paladinCatalog,
            [new("paladin","cure-wounds","paladin-test-spell"),new("paladin","paladin-test-spell","cure-wounds")],
            SpellPreparationMoment.LongRest));
        Assert.Equal("cure-wounds",prepared[0].SpellId);
    }
}
