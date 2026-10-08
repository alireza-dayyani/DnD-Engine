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
        Assert.Equal(0,SpellPreparation.Capacity("paladin",1));
        Assert.Equal(0,SpellPreparation.Capacity("ranger",1));
        Assert.Equal(0,SpellPreparation.MaximumSpellLevel("paladin",1));
        Assert.Equal(0,SpellPreparation.MaximumSpellLevel("ranger",1));
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

    [Fact]
    public void CantripChoicesUseTheirOwnClassLimitsAndTiming()
    {
        SpellDefinition[] catalog=[
            new("fire-bolt","Fire Bolt",0,["sorcerer","wizard"],"Action","120 feet","V,S",
                SpellEffectKind.SpellAttack,"SRD"),
            new("other-wizard-cantrip","Other Wizard Cantrip",0,["wizard"],"Action","120 feet","V,S",
                SpellEffectKind.SpellAttack,"test"),
            new("sacred-flame","Sacred Flame",0,["cleric"],"Action","60 feet","V,S",
                SpellEffectKind.SavingThrowDamage,"SRD")
        ];
        Assert.Equal(3,CantripKnowledge.Capacity("cleric",1));
        Assert.Equal(4,CantripKnowledge.Capacity("cleric",4));
        Assert.Equal(5,CantripKnowledge.Capacity("cleric",10));
        Assert.Equal(0,CantripKnowledge.Capacity("paladin",1));
        var known=CantripKnowledge.Add([],new("wizard",1),catalog,["fire-bolt"]);
        Assert.Single(known);
        Assert.Throws<RuleViolation>(()=>CantripKnowledge.Add(known,new("wizard",1),catalog,["fire-bolt"]));
        Assert.Throws<RuleViolation>(()=>CantripKnowledge.Add([],new("cleric",1),catalog,["fire-bolt"]));
        Assert.Throws<RuleViolation>(()=>CantripKnowledge.Add([],new("wizard",1),catalog,["unknown"]));
        Assert.Throws<RuleViolation>(()=>CantripKnowledge.Replace(known,[new("wizard",1)],catalog,
            new("wizard","fire-bolt","sacred-flame"),null,true));
        Assert.Throws<RuleViolation>(()=>CantripKnowledge.Replace(known,[new("wizard",2)],catalog,
            new("wizard","fire-bolt","other-wizard-cantrip"),"wizard",false));
        Assert.Equal("other-wizard-cantrip",Assert.Single(CantripKnowledge.Replace(known,[new("wizard",2)],catalog,
            new("wizard","fire-bolt","other-wizard-cantrip"),null,true)).SpellId);
    }

    [Fact]
    public void WizardPreparationRequiresSpellbookMembership()
    {
        SpellDefinition[] catalog=[
            new("burning-hands","Burning Hands",1,["wizard","sorcerer"],"Action","Self","V,S",
                SpellEffectKind.SavingThrowDamage,"SRD"),
            new("blur","Blur",2,["wizard","sorcerer"],"Action","Self","V",
                SpellEffectKind.Blur,"SRD")
        ];
        var book=WizardSpellbook.Add([],new("wizard",1),catalog,["burning-hands"],6);
        Assert.Throws<RuleViolation>(()=>WizardSpellbook.Add(book,new("wizard",2),catalog,["blur"],2));
        book=WizardSpellbook.Add(book,new("wizard",3),catalog,["blur"],2);
        Assert.Throws<RuleViolation>(()=>SpellPreparation.Add([],new("wizard",3),catalog,["blur"],[]));
        var prepared=SpellPreparation.Add([],new("wizard",3),catalog,["burning-hands"],book);
        var replaced=SpellPreparation.Replace(prepared,[new("wizard",3)],catalog,
            [new("wizard","burning-hands","blur")],SpellPreparationMoment.LongRest,
            wizardSpellbook:book);
        Assert.Equal("blur",Assert.Single(replaced).SpellId);
    }
}
