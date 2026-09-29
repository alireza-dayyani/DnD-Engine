using DndEngine.Domain;
namespace DndEngine.Domain.Tests;

public class DiceTests
{
    [Theory]
    [InlineData("d20",1,20,0)][InlineData("1d20+5",1,20,5)][InlineData("2d6",2,6,0)]
    [InlineData("8d6",8,6,0)][InlineData("1d8+4",1,8,4)][InlineData("2d10-3",2,10,-3)]
    [InlineData("d4",1,4,0)][InlineData("d12",1,12,0)][InlineData("D100",1,100,0)]
    public void ParsesDice(string text,int count,int sides,int modifier)
    {
        var e=DiceExpression.Parse(text);
        Assert.Equal(count,e.Count); Assert.Equal(sides,e.Sides); Assert.Equal(modifier,e.Modifier);
        Assert.Equal(e,DiceExpression.Parse(e.ToString()));
    }
    [Theory]
    [InlineData("")][InlineData("d")][InlineData("2d3")][InlineData("0d20")][InlineData("-1d6")]
    [InlineData("d20++5")][InlineData("d20+5x")][InlineData("2d6+1d4")][InlineData("1001d6")]
    [InlineData("999999999999d6")][InlineData("1d20+99999999999")][InlineData(null)]
    public void RejectsMalformedOrUnboundedDice(string? text) => Assert.Throws<RuleViolation>(() => DiceExpression.Parse(text));
    [Fact]
    public void DeterministicDiceAddModifierOnce()
    {
        var r=new FixedDiceRoller(3,6).Roll(DiceExpression.Parse("2d6-2"));
        Assert.Equal(new[]{3,6},r.Rolls); Assert.Equal(7,r.Total);
    }
    [Theory][InlineData(0)][InlineData(21)]
    public void RejectsInvalidInjectedRolls(int roll) => Assert.Throws<RuleViolation>(() => new FixedDiceRoller(roll).Roll(new(1,20)));
    [Fact] public void ExhaustedDiceFail() => Assert.Throws<RuleViolation>(() => new FixedDiceRoller().Roll(new(1,20)));
}
