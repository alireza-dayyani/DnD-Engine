using DndEngine.Domain;
namespace DndEngine.Domain.Tests;

public class MechanicsTests
{
    private static Character Character(int score = 18, int level = 5) => new(Guid.NewGuid(), Guid.NewGuid(), "Vaelaris", level,
        Enum.GetValues<Ability>().ToDictionary(x => x, _ => score), ["deception"], [Ability.Wisdom], 14, new HitPoints(20));
    private static readonly SkillDefinition Deception = new("deception", "Deception", Ability.Charisma, "SRD-5.2.1 p.9");

    [Theory]
    [InlineData(1,-5)][InlineData(2,-4)][InlineData(3,-4)][InlineData(9,-1)][InlineData(10,0)]
    [InlineData(11,0)][InlineData(18,4)][InlineData(20,5)][InlineData(29,9)][InlineData(30,10)]
    public void AbilityModifiersRoundDown(int score, int expected) => Assert.Equal(expected, new AbilityScore(score).Modifier);
    [Theory]
    [InlineData(0)][InlineData(-1)][InlineData(31)]
    public void InvalidScoresFail(int score) => Assert.Throws<RuleViolation>(() => new AbilityScore(score));
    [Theory]
    [InlineData(1,2)][InlineData(4,2)][InlineData(5,3)][InlineData(8,3)][InlineData(9,4)]
    [InlineData(12,4)][InlineData(13,5)][InlineData(16,5)][InlineData(17,6)][InlineData(20,6)]
    public void ProficiencyFollowsLevel(int level, int expected) => Assert.Equal(expected, new CharacterLevel(level).ProficiencyBonus);
    [Theory][InlineData(0)][InlineData(21)]
    public void InvalidLevelFails(int level) => Assert.Throws<RuleViolation>(() => new CharacterLevel(level));
    [Fact]
    public void SkillAndSaveModifiersUseProficiencyOnlyWhenTrained()
    {
        var c = Character();
        Assert.Equal(7, c.SkillModifier(Deception));
        Assert.Equal(4, c.SkillModifier(new("arcana", "Arcana", Ability.Intelligence, "SRD-5.2.1")));
        Assert.Equal(7, c.SavingThrowModifier(Ability.Wisdom));
        Assert.Equal(4, c.SavingThrowModifier(Ability.Dexterity));
    }
    [Theory]
    [InlineData(false,false,11,18,1,true)]
    [InlineData(true,false,16,23,2,true)]
    [InlineData(false,true,7,14,2,false)]
    [InlineData(true,true,11,18,1,true)]
    public void ChecksResolveAdvantageAndCancellation(bool advantage, bool disadvantage, int selected, int total, int count, bool success)
    {
        var dice = count == 1 ? new FixedDiceRoller(11) : new FixedDiceRoller(7,16);
        var result = new CheckResolver(dice).Resolve(Character(), CheckKind.Skill, Ability.Charisma, new(18,advantage,disadvantage), Deception);
        Assert.Equal(selected, result.SelectedRoll); Assert.Equal(total,result.Total);
        Assert.Equal(count,result.Rolls.Count); Assert.Equal(success,result.Success);
        Assert.Equal(4,result.AbilityModifier); Assert.Equal(3,result.ProficiencyModifier);
    }
    [Theory][InlineData(1,8,true)][InlineData(20,28,false)]
    public void NaturalOneAndTwentyHaveNoSpecialCheckOutcome(int roll, int dc, bool expected)
    {
        var result = new CheckResolver(new FixedDiceRoller(roll)).Resolve(Character(),CheckKind.Skill,Ability.Charisma,new(dc),Deception);
        Assert.Equal(expected,result.Success);
    }
    [Fact]
    public void AbilityCheckDoesNotAddUnrelatedSaveProficiency()
    {
        var r = new CheckResolver(new FixedDiceRoller(10)).Resolve(Character(), CheckKind.Ability,Ability.Wisdom,new(15,OtherModifier:1));
        Assert.True(r.Success); Assert.Equal(0,r.ProficiencyModifier); Assert.Equal(15,r.Total);
    }
    [Theory][InlineData(Ability.Wisdom,3)][InlineData(Ability.Strength,0)]
    public void SavingThrowsUseAbilityProficiency(Ability ability,int proficiency)
    {
        var result = new CheckResolver(new FixedDiceRoller(10)).Resolve(Character(),CheckKind.SavingThrow,ability,new(17));
        Assert.Equal(proficiency,result.ProficiencyModifier); Assert.Equal(proficiency==3,result.Success);
    }
    [Fact]
    public void SkillCanUseGmSelectedAbility()
    {
        var c = Character();
        var r = new CheckResolver(new FixedDiceRoller(10)).Resolve(c,CheckKind.Skill,Ability.Strength,new(17),Deception);
        Assert.Equal(Ability.Strength,r.Ability); Assert.Equal(3,r.ProficiencyModifier);
    }
    [Theory][InlineData(Ability.Strength)][InlineData(Ability.Dexterity)]
    public void UnconsciousAutomaticallyFailsPhysicalSavesWithoutDice(Ability ability)
    {
        var c = Character(); c.Health.Damage(20);
        var result = new CheckResolver(new FixedDiceRoller()).Resolve(c,CheckKind.SavingThrow,ability,new(1));
        Assert.False(result.Success); Assert.Empty(result.Rolls); Assert.Null(result.Total); Assert.Equal("Unconscious",result.AutomaticOutcome);
    }
    [Fact]
    public void VoluntaryFailureUsesNoDice()
    {
        var r = new CheckResolver(new FixedDiceRoller()).Resolve(Character(),CheckKind.SavingThrow,Ability.Wisdom,new(1,VoluntaryFailure:true));
        Assert.False(r.Success); Assert.Empty(r.Rolls); Assert.Equal("VoluntaryFailure",r.AutomaticOutcome);
    }
    [Fact]
    public void UnconsciousChecksAndDeadOperationsFail()
    {
        var c = Character(); c.Health.Damage(20);
        Assert.Throws<RuleViolation>(() => new CheckResolver(new FixedDiceRoller()).Resolve(c,CheckKind.Ability,Ability.Wisdom,new(10)));
        c.Health.Damage(20);
        Assert.Throws<RuleViolation>(() => new CheckResolver(new FixedDiceRoller()).Resolve(c,CheckKind.SavingThrow,Ability.Wisdom,new(10)));
    }
    [Fact]
    public void InvalidChecksFailBeforeRolling()
    {
        var resolver = new CheckResolver(new FixedDiceRoller());
        Assert.Throws<RuleViolation>(() => resolver.Resolve(Character(),CheckKind.Ability,(Ability)99,new(10)));
        Assert.Throws<RuleViolation>(() => resolver.Resolve(Character(),CheckKind.Ability,Ability.Wisdom,new(-1)));
        Assert.Throws<RuleViolation>(() => resolver.Resolve(Character(),CheckKind.Ability,Ability.Wisdom,new(10,VoluntaryFailure:true)));
    }
}
