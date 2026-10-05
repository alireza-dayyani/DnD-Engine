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
    public void PreparationLimitsUseIndividualClassLevels()
    {
        Assert.Equal(4,SpellPreparation.Capacity("bard",1));
        Assert.Equal(22,SpellPreparation.Capacity("bard",20));
        Assert.Equal(2,SpellPreparation.Capacity("paladin",1));
        Assert.Equal(6,SpellPreparation.Capacity("ranger",5));
        Assert.Equal(4,SpellPreparation.Capacity("sorcerer",2));
        Assert.Equal(10,SpellPreparation.Capacity("warlock",9));
        Assert.Equal(21,SpellPreparation.Capacity("wizard",16));
        Assert.Equal(1,SpellPreparation.MaximumSpellLevel("ranger",4));
        Assert.Equal(2,SpellPreparation.MaximumSpellLevel("ranger",5));
        Assert.Equal(2,SpellPreparation.MaximumSpellLevel("warlock",3));
        Assert.Equal(2,SpellPreparation.MaximumSpellLevel("cleric",3));
    }

    [Fact]
    public void AdditionalSpellsRespectClassListCapacityAndClassSpellLevel()
    {
        var prepared=SpellPreparation.Add([new("cleric","cure-wounds")],new("bard",1),Catalog,["healing-word"]);
        Assert.Contains(prepared,x=>x.ClassId=="cleric" && x.SpellId=="cure-wounds");
        Assert.Contains(prepared,x=>x.ClassId=="bard" && x.SpellId=="healing-word");
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Add(prepared,new("bard",1),Catalog,["healing-word"]));
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Add([],new("paladin",1),Catalog,["healing-word"]));
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Add([],new("wizard",1),Catalog,["cure-wounds"]));
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Add(
            [new("bard","a"),new("bard","b"),new("bard","c")],new("bard",1),Catalog,
            ["cure-wounds","healing-word"]));
        SpellDefinition[] higher=[new("test-level-2","Test level 2 spell",2,["cleric"],
            "Action","Self","V",SpellEffectKind.SelfHealing,"test")];
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Add([],new("cleric",2),higher,["test-level-2"]));
        Assert.Single(SpellPreparation.Add([],new("cleric",3),higher,["test-level-2"]));
    }

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
