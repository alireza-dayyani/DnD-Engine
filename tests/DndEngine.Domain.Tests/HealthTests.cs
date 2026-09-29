using DndEngine.Domain;
namespace DndEngine.Domain.Tests;

public class HealthTests
{
    [Fact]
    public void DamageHealingAndOverhealingRespectBoundaries()
    {
        var hp=new HitPoints(20);
        Assert.Equal(7,hp.Damage(7).HitPointsLost); Assert.Equal(13,hp.State.Current);
        Assert.Equal(3,hp.Heal(3).HitPointsRegained); Assert.Equal(16,hp.State.Current);
        Assert.Equal(4,hp.Heal(100).HitPointsRegained); Assert.Equal(20,hp.State.Current);
    }
    [Fact]
    public void TemporaryHpAbsorbsFirstAndNeverStacks()
    {
        var hp=new HitPoints(20); hp.GrantTemporary(5,true); hp.GrantTemporary(3,false);
        Assert.Equal(5,hp.State.Temporary);
        hp.GrantTemporary(3,true); Assert.Equal(3,hp.State.Temporary);
        var r=hp.Damage(7); Assert.Equal(3,r.TemporaryAbsorbed); Assert.Equal(4,r.HitPointsLost);
        hp.Heal(3); Assert.Equal(0,hp.State.Temporary);
    }
    [Fact]
    public void ZeroHpUnconsciousHealingWakesButRemainsProne()
    {
        var hp=new HitPoints(20); hp.Damage(21);
        Assert.Equal(0,hp.State.Current); Assert.True(hp.State.Unconscious); Assert.False(hp.State.Dead);
        hp.GrantTemporary(10,true); Assert.True(hp.State.Unconscious);
        hp.Heal(0); Assert.True(hp.State.Unconscious);
        hp.Heal(1); Assert.False(hp.State.Unconscious); Assert.True(hp.State.Prone);
    }
    [Theory][InlineData(39,false)][InlineData(40,true)]
    public void MassiveDamageUsesRemainingDamage(int damage,bool dead)
    {
        var hp=new HitPoints(20); hp.Damage(damage); Assert.Equal(dead,hp.State.Dead);
    }
    [Fact]
    public void TempBufferPreventsMassiveDamageWhileConscious()
    {
        var hp=new HitPoints(20); hp.GrantTemporary(5,true); hp.Damage(40); Assert.False(hp.State.Dead);
    }
    [Fact]
    public void DamageAtZeroAndCriticalHitsAccumulateFailures()
    {
        var hp=new HitPoints(20); hp.Damage(20); hp.Damage(0);
        Assert.Equal(0,hp.State.DeathFailures); hp.Damage(1,true); Assert.Equal(2,hp.State.DeathFailures);
        hp.Damage(1); Assert.True(hp.State.Dead); Assert.Throws<RuleViolation>(()=>hp.Heal(10));
    }
    [Fact]
    public void DamageAtZeroIncludesDamageAbsorbedByTemporaryHp()
    {
        var hp=new HitPoints(20); hp.Damage(20); hp.GrantTemporary(30,true); hp.Damage(1);
        Assert.Equal(1,hp.State.DeathFailures); Assert.Equal(29,hp.State.Temporary);
        hp.Damage(20); Assert.True(hp.State.Dead);
    }
    [Theory][InlineData(1,2,0)][InlineData(9,1,0)][InlineData(10,0,1)][InlineData(19,0,1)]
    public void DeathSaveThresholdAndNaturalOne(int roll,int failures,int successes)
    {
        var hp=new HitPoints(20); hp.Damage(20); hp.DeathSave(new FixedDiceRoller(roll));
        Assert.Equal(failures,hp.State.DeathFailures); Assert.Equal(successes,hp.State.DeathSuccesses);
    }
    [Fact]
    public void NaturalTwentyRestoresOneAndResetsCounters()
    {
        var hp=new HitPoints(20); hp.Damage(20); hp.Damage(1); hp.DeathSave(new FixedDiceRoller(20));
        Assert.Equal(1,hp.State.Current); Assert.Equal(0,hp.State.DeathFailures); Assert.Equal(0,hp.State.DeathSuccesses);
    }
    [Fact]
    public void ThreeSuccessesStabilizeAndDamageDestabilizes()
    {
        var hp=new HitPoints(20); hp.Damage(20);
        var dice=new FixedDiceRoller(10,9,10,10);
        for(var i=0;i<4;i++) hp.DeathSave(dice);
        Assert.True(hp.State.Stable); Assert.True(hp.State.Unconscious);
        Assert.Equal(0,hp.State.DeathSuccesses); Assert.Equal(0,hp.State.DeathFailures);
        Assert.Throws<RuleViolation>(()=>hp.DeathSave(dice));
        hp.Damage(1); Assert.False(hp.State.Stable); Assert.Equal(1,hp.State.DeathFailures);
    }
    [Fact]
    public void ThreeFailuresKill()
    {
        var hp=new HitPoints(20); hp.Damage(20); hp.DeathSave(new FixedDiceRoller(1)); hp.DeathSave(new FixedDiceRoller(9));
        Assert.True(hp.State.Dead);
    }
    [Fact]
    public void InvalidHpOperationsFail()
    {
        Assert.Throws<RuleViolation>(()=>new HitPoints(0));
        var hp=new HitPoints(20);
        Assert.Throws<RuleViolation>(()=>hp.Damage(-1)); Assert.Throws<RuleViolation>(()=>hp.Heal(-1));
        Assert.Throws<RuleViolation>(()=>hp.GrantTemporary(-1,true)); Assert.Throws<RuleViolation>(()=>hp.DeathSave(new FixedDiceRoller()));
        Assert.Throws<RuleViolation>(()=>new HitPoints(new HealthState(20,21,0,false,false,0,0,false)));
    }
}
