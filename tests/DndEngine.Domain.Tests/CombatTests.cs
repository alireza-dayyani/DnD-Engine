using DndEngine.Domain.Combat;

namespace DndEngine.Domain.Tests;

public class CombatTests
{
    private static readonly WeaponDefinition Sword = new("longsword", "Longsword", WeaponCategory.Martial, WeaponKind.Melee,
        "1d8", 0, DamageType.Slashing, [WeaponProperty.Versatile], "sap", 0, 0, "1d10", null, false, "SRD-5.2.1 p.91");
    private sealed class Fight
    {
        public Character A { get; }
        public Character B { get; }
        public CombatProfile PA { get; }
        public CombatProfile PB { get; }
        public CombatEncounter Encounter { get; }
        public Guid Actor { get; }
        public Guid Target { get; }
        public Guid Weapon { get; } = Guid.NewGuid();
        public Fight(WeaponDefinition? weapon = null, int attacks = 1, bool start = true, ZeroHpPolicy policy = ZeroHpPolicy.DeathSaves)
        {
            var campaign = Guid.NewGuid(); weapon ??= Sword;
            A = MakeCharacter(campaign, "Mira", 30); B = MakeCharacter(campaign, "Goblin", 20);
            PA = MakeProfile(A, weapon.Id, attacks); PB = MakeProfile(B, weapon.Id);
            PA.GrantWeapon(new(Weapon, weapon.Id, weapon.Has(WeaponProperty.Ammunition) ? 10 : 0));
            Encounter = CombatEncounter.Create(campaign, "Ambush");
            Actor = Encounter.Add(A.Id, CombatantKind.PlayerCharacter, ZeroHpPolicy.DeathSaves, false, null).Id;
            Target = Encounter.Add(B.Id, CombatantKind.Monster, policy, false, null).Id;
            if (start) { Initiative(new FixedDiceRoller(15, 10)); Encounter.Start(null); }
        }
        public InitiativeResult Initiative(IDiceRoller dice) => Encounter.RollInitiative(new Dictionary<Guid, Character> { [A.Id] = A, [B.Id] = B },
            new Dictionary<Guid, CombatProfile> { [A.Id] = PA, [B.Id] = PB }, dice);
        public WeaponAttackOptions Options => new(Target, Weapon, AttackMode.Melee, new(5, true, true));
        public WeaponAttackResult Attack(IDiceRoller dice, WeaponAttackOptions? options = null, WeaponDefinition? weapon = null) =>
            new WeaponAttackResolver(dice).Resolve(Encounter, Actor, A, PA, B, PB, weapon ?? Sword, options ?? Options);
        public void Condition(ConditionKind kind, bool target = false, Guid? source = null)
        { (target ? PB : PA).AddCondition(new(Guid.NewGuid(), kind, "test", source), target ? B : A); }
    }
    private static Character MakeCharacter(Guid campaign, string name, int hp) => new(Guid.NewGuid(), campaign, name, 1,
        Enum.GetValues<Ability>().ToDictionary(x => x, x => x == Ability.Strength ? 18 : 14), [], [], 13, new(hp));
    private static CombatProfile MakeProfile(Character c, string weapon, int attacks = 1) => new(new(c.Id, new(30, [weapon], [], [], [], [], attacks), [], []));

    [Fact]
    public void InitiativeTiesRequireAdjudicationAndInvalidOrdersFail()
    {
        var f = new Fight(start: false); var roll = f.Initiative(new FixedDiceRoller(10, 10));
        Assert.Equal("GameMaster", Assert.Single(roll.Ties).DecidedBy);
        Assert.All(roll.Combatants, c => Assert.Equal(12, c.Initiative!.Total));
        Assert.Throws<RuleViolation>(() => f.Encounter.Start(null));
        Assert.Throws<RuleViolation>(() => f.Encounter.Start([f.Actor, f.Actor]));
        f.Encounter.Start([f.Target, f.Actor]); Assert.Equal(f.Target, f.Encounter.CurrentCombatantId);
        Assert.Throws<RuleViolation>(() => f.Encounter.Start(null));
    }
    [Fact]
    public void IdenticalMonsterGroupsRollOnceAndSurpriseIsDisadvantage()
    {
        var campaign = Guid.NewGuid(); var a = MakeCharacter(campaign, "A", 10); var b = MakeCharacter(campaign, "B", 10);
        var e = CombatEncounter.Create(campaign, "Identical goblins");
        e.Add(a.Id, CombatantKind.Monster, ZeroHpPolicy.Die, true, "goblins"); e.Add(b.Id, CombatantKind.Monster, ZeroHpPolicy.Die, true, "goblins");
        var result = e.RollInitiative(new Dictionary<Guid, Character> { [a.Id] = a, [b.Id] = b },
            new Dictionary<Guid, CombatProfile> { [a.Id] = MakeProfile(a, Sword.Id), [b.Id] = MakeProfile(b, Sword.Id) }, new FixedDiceRoller(17, 4));
        Assert.All(result.Combatants, c => { Assert.Equal(6, c.Initiative!.Total); Assert.Equal(AdvantageState.Disadvantage, c.Initiative.AdvantageState); });
        e.Start(result.Combatants.Select(x => x.Id).ToArray());
        Assert.False(e.Combatant(e.CurrentCombatantId!.Value).Resources.ActionUsed);
    }
    [Fact]
    public void LifecycleAndTurnOwnershipAreEnforced()
    {
        var f = new Fight(start: false);
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller()));
        Assert.Throws<RuleViolation>(() => f.Encounter.EndTurn(f.Actor));
        Assert.Throws<RuleViolation>(() => f.Encounter.Complete());
        f.Initiative(new FixedDiceRoller(15, 1)); f.Encounter.Start(null);
        Assert.Throws<RuleViolation>(() => f.Encounter.EndTurn(f.Target));
        f.Encounter.Complete(); Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller()));
        Assert.Throws<RuleViolation>(() => f.Encounter.EndTurn(f.Actor));
    }
    [Fact]
    public void MovementDashCrawlAndRoundReset()
    {
        var f = new Fight(); var effects = new ConditionEffects(f.A, f.PA);
        var move = f.Encounter.Move(f.Actor, effects, 10, MovementMode.Walk, true, false);
        Assert.Equal(20, move.Cost); Assert.Equal(10, move.Remaining);
        Assert.Throws<RuleViolation>(() => f.Encounter.Move(f.Actor, effects, 11, MovementMode.Walk, false, false));
        f.Encounter.Act(f.Actor, effects, CombatAction.Dash);
        Assert.Equal(40, f.Encounter.MovementRemaining(f.Actor, 30));
        f.Condition(ConditionKind.Prone);
        Assert.Throws<RuleViolation>(() => f.Encounter.Move(f.Actor, effects, 1, MovementMode.Walk, false, false));
        Assert.Equal(15, f.Encounter.Move(f.Actor, effects, 5, MovementMode.Crawl, true, false).Cost);
        Assert.Equal(15, f.Encounter.Stand(f.Actor, f.A, f.PA)); Assert.False(effects.Has(ConditionKind.Prone));
        f.Encounter.EndTurn(f.Actor); f.Encounter.EndTurn(f.Target);
        Assert.Equal(2, f.Encounter.State.Round); Assert.Equal(3, f.Encounter.State.TurnNumber);
        Assert.Equal(30, f.Encounter.MovementRemaining(f.Actor, 30)); Assert.False(f.Encounter.Combatant(f.Actor).Resources.ActionUsed);
    }
    [Theory]
    [InlineData(1, false, false, 20)]
    [InlineData(6, false, false, 20)]
    [InlineData(7, true, false, 11)]
    [InlineData(20, true, true, 6)]
    public void AttackThresholdNaturalRollsAndCriticalDice(int roll, bool hit, bool critical, int remaining)
    {
        var f = new Fight(); var r = f.Attack(new FixedDiceRoller(roll, 5, 5));
        Assert.Equal(hit, r.Hit); Assert.Equal(critical, r.Critical); Assert.Equal(remaining, f.B.Health.State.Current);
        Assert.Equal(6, r.AttackRoll.Modifier); Assert.Equal(4, r.DamageModifier);
        Assert.True(r.Resources.ActionUsed); Assert.Equal(0, r.Resources.AttacksRemaining);
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller()));
        if (hit) Assert.Equal(critical ? 2 : 1, r.DamageRoll!.Rolls.Count);
        else Assert.Null(r.Damage);
    }
    [Theory]
    [InlineData(true, false, AdvantageState.Advantage, 18)]
    [InlineData(false, true, AdvantageState.Disadvantage, 3)]
    [InlineData(true, true, AdvantageState.Normal, 3)]
    [InlineData(false, false, AdvantageState.Normal, 3)]
    public void AdvantageCancellationUsesOneOrTwoDice(bool advantage, bool disadvantage, AdvantageState expected, int selected)
    {
        var f = new Fight(); var r = f.Attack(new FixedDiceRoller(3, 18, 5), f.Options with { Advantage = advantage, Disadvantage = disadvantage });
        Assert.Equal(expected, r.AttackRoll.AdvantageState); Assert.Equal(selected, r.AttackRoll.SelectedRoll);
    }
    [Theory]
    [InlineData(false, false, false, 7)] [InlineData(false, true, false, 3)]
    [InlineData(false, false, true, 14)] [InlineData(false, true, true, 6)]
    [InlineData(true, false, false, 0)] [InlineData(true, true, true, 0)]
    public void DamageOrderingRoundsResistanceBeforeVulnerability(bool immune, bool resistant, bool vulnerable, int expected)
    {
        var r = DamageResolver.Resolve(9, -2, DamageType.Fire, immune, resistant, vulnerable);
        Assert.Equal(7, r.AdjustedDamage); Assert.Equal(expected, r.AppliedDamage);
    }
    [Fact]
    public void ResistanceThenTempHpThenHp()
    {
        var f = new Fight(); f.B.Health.GrantTemporary(3, true);
        f.PB.ImportCapabilities(f.PB.State.Capabilities with { Resistances = [DamageType.Slashing, DamageType.Slashing] });
        var result = f.Attack(new FixedDiceRoller(10, 7));
        Assert.Equal(11, result.Damage!.RawDamage); Assert.Equal(5, result.Damage.AppliedDamage);
        Assert.Equal(3, result.Health!.TemporaryAbsorbed); Assert.Equal(18, f.B.Health.State.Current);
    }
    [Fact]
    public void ImportedMultipleAttacksConsumeOneAction()
    {
        var f = new Fight(attacks: 2); f.Attack(new FixedDiceRoller(1));
        Assert.Equal(1, f.Encounter.Combatant(f.Actor).Resources.AttacksRemaining);
        Assert.Throws<RuleViolation>(() => f.Encounter.Act(f.Actor, new(f.A, f.PA), CombatAction.Dash));
        f.Attack(new FixedDiceRoller(1)); Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller()));
    }
    [Fact]
    public void LightBonusRequiresDifferentWeaponAndOmitsPositiveAbilityDamage()
    {
        var dagger = Sword with { Id = "dagger", DamageDice = "1d4", Properties = [WeaponProperty.Light, WeaponProperty.Finesse], VersatileDice = null };
        var f = new Fight(dagger); var second = Guid.NewGuid(); f.PA.GrantWeapon(new(second, dagger.Id, 0));
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), f.Options with { Use = AttackUse.LightBonus, WeaponId = second }, dagger));
        f.Attack(new FixedDiceRoller(10, 2), weapon: dagger);
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), f.Options with { Use = AttackUse.LightBonus }, dagger));
        var extra = f.Attack(new FixedDiceRoller(10, 3), f.Options with { Use = AttackUse.LightBonus, WeaponId = second }, dagger);
        Assert.Equal(0, extra.DamageModifier); Assert.Equal(3, extra.Damage!.RawDamage); Assert.True(extra.Resources.BonusActionUsed);
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), f.Options with { Use = AttackUse.LightBonus, WeaponId = second }, dagger));
    }
    [Fact]
    public void ReactionCanHappenOffTurnAndResetsAtStartOfOwnTurn()
    {
        var f = new Fight(); f.Encounter.EndTurn(f.Actor);
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), f.Options with { Use = AttackUse.Opportunity }));
        var options = f.Options with { Use = AttackUse.Opportunity, Context = new(5, true, true, OpportunityProvoked: true) };
        var r = f.Attack(new FixedDiceRoller(1), options); Assert.True(r.Resources.ReactionUsed); Assert.False(r.Resources.ActionUsed);
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), options));
        f.Encounter.EndTurn(f.Target); Assert.False(f.Encounter.Combatant(f.Actor).Resources.ReactionUsed);
    }
    [Fact]
    public void DisengagePreventsOpportunityAttack()
    {
        var f = new Fight(); f.Encounter.EndTurn(f.Actor); f.Encounter.Act(f.Target, new(f.B, f.PB), CombatAction.Disengage);
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), f.Options with { Use = AttackUse.Opportunity, Context = new(5, true, true, OpportunityProvoked: true) }));
    }
    [Fact]
    public void LoadingAmmunitionAndRangeAreMechanicalRestrictions()
    {
        var bow = Sword with { Id = "crossbow", Kind = WeaponKind.Ranged, Properties = [WeaponProperty.Loading, WeaponProperty.Ammunition, WeaponProperty.TwoHanded],
            NormalRange = 80, LongRange = 320, Ammunition = "bolt", VersatileDice = null };
        var f = new Fight(bow, attacks: 2); var options = f.Options with { Mode = AttackMode.Ranged, Hands = 2, Context = new(100, true, true) };
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), options with { Hands = 1 }, bow));
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), options with { Context = new(321, true, true) }, bow));
        var r = f.Attack(new FixedDiceRoller(15, 3), options, bow);
        Assert.Equal(AdvantageState.Disadvantage, r.AttackRoll.AdvantageState); Assert.Equal(9, f.PA.Weapon(f.Weapon).AmmunitionRemaining);
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), options, bow));
        Assert.Equal(9, f.PA.Weapon(f.Weapon).AmmunitionRemaining);
    }
    [Fact]
    public void ThrownWeaponBecomesUnavailableAndUsesMeleeAbility()
    {
        var axe = Sword with { Id = "axe", Properties = [WeaponProperty.Thrown], NormalRange = 20, LongRange = 60, VersatileDice = null };
        var f = new Fight(axe, attacks: 2); var options = f.Options with { Mode = AttackMode.Thrown, Context = new(20, true, true) };
        var r = f.Attack(new FixedDiceRoller(10, 2), options, axe); Assert.Equal(Ability.Strength, r.Ability);
        Assert.False(f.PA.Weapon(f.Weapon).Available); Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), options, axe));
    }
    [Theory]
    [InlineData(ConditionKind.Paralyzed, 0, false)] [InlineData(ConditionKind.Petrified, 0, false)]
    [InlineData(ConditionKind.Unconscious, 0, false)] [InlineData(ConditionKind.Stunned, 30, false)]
    [InlineData(ConditionKind.Incapacitated, 30, false)] [InlineData(ConditionKind.Grappled, 0, true)]
    [InlineData(ConditionKind.Restrained, 0, true)] [InlineData(ConditionKind.Poisoned, 30, true)]
    [InlineData(ConditionKind.Exhaustion, 25, true)] [InlineData(ConditionKind.Prone, 30, true)]
    [InlineData(ConditionKind.Blinded, 30, true)] [InlineData(ConditionKind.Deafened, 30, true)]
    [InlineData(ConditionKind.Charmed, 30, true)] [InlineData(ConditionKind.Frightened, 30, true)]
    [InlineData(ConditionKind.Invisible, 30, true)]
    public void EveryConditionHasComposableMechanicalState(ConditionKind kind, int speed, bool canAct)
    {
        var f = new Fight(); f.Condition(kind, source: f.B.Id); var effects = new ConditionEffects(f.A, f.PA);
        Assert.Equal(speed, effects.Speed); Assert.Equal(canAct, effects.CanAct);
        if (!canAct) Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller()));
        var condition = Assert.Single(f.PA.State.Conditions); f.PA.RemoveCondition(condition.Id);
        Assert.False(effects.Has(kind));
    }
    [Theory]
    [InlineData(ConditionKind.Paralyzed)] [InlineData(ConditionKind.Unconscious)]
    public void HitsWithinFiveFeetAgainstHelplessTargetsAreCritical(ConditionKind condition)
    {
        var f = new Fight(); f.Condition(condition, target: true);
        var r = f.Attack(new FixedDiceRoller(10, 15, 2, 3));
        Assert.True(r.Critical); Assert.Equal(AdvantageState.Advantage, r.AttackRoll.AdvantageState); Assert.Equal(9, r.Damage!.RawDamage);
    }
    [Fact]
    public void ZeroHpAndMonsterDeathPolicyDiffer()
    {
        var pc = new Fight(); pc.B.Health.Damage(15); var down = pc.Attack(new FixedDiceRoller(10, 4));
        Assert.True(down.Health!.After.Unconscious); Assert.False(down.Health.After.Dead);
        pc.Encounter.EndTurn(pc.Actor); pc.Encounter.EndTurn(pc.Target);
        var second = pc.Attack(new FixedDiceRoller(10, 15, 1, 1)); Assert.True(second.Critical); Assert.Equal(2, pc.B.Health.State.DeathFailures);
        var monster = new Fight(policy: ZeroHpPolicy.Die); monster.B.Health.Damage(15);
        Assert.True(monster.Attack(new FixedDiceRoller(10, 4)).Health!.After.Dead);
    }
    [Fact]
    public void ConditionSourcesDoNotStackAndExpiryRemovesOnlyMatchingInstance()
    {
        var f = new Fight(); f.Condition(ConditionKind.Poisoned); f.Condition(ConditionKind.Poisoned);
        var timed = new ActiveCondition(Guid.NewGuid(), ConditionKind.Poisoned, "timer", null, ExpiryBoundary.TurnStart, f.Encounter.State.Id, 3);
        f.PA.AddCondition(timed, f.A);
        Assert.Empty(f.PA.Expire(f.Encounter.State.Id, 2, ExpiryBoundary.TurnStart));
        Assert.Single(f.PA.Expire(f.Encounter.State.Id, 3, ExpiryBoundary.TurnStart));
        var r = f.Attack(new FixedDiceRoller(15, 2)); Assert.Equal(2, r.AttackRoll.Rolls.Count); Assert.Equal(2, f.PA.State.Conditions.Length);
    }
    [Fact]
    public void ExhaustionSixKillsAndPenaltiesUseLevels()
    {
        var f = new Fight(); for (var i = 0; i < 5; i++) f.Condition(ConditionKind.Exhaustion);
        var effects = new ConditionEffects(f.A, f.PA); Assert.Equal(-10, effects.D20Penalty); Assert.Equal(5, effects.Speed);
        f.Condition(ConditionKind.Exhaustion); Assert.True(f.A.Health.State.Dead);
    }
    [Fact]
    public void PetrificationResistsAllDamageButDoesNotGrantPoisonDamageImmunity()
    {
        var f = new Fight(); f.Condition(ConditionKind.Petrified, target: true);
        Assert.Throws<RuleViolation>(() => f.Condition(ConditionKind.Poisoned, target: true));
        var effects = new ConditionEffects(f.B, f.PB); Assert.All(Enum.GetValues<DamageType>(), type => Assert.True(effects.Resists(type)));
        Assert.DoesNotContain(DamageType.Poison, f.PB.State.Capabilities.Immunities);
    }
    [Fact]
    public void CharmFearAndGrappleUseTheirSource()
    {
        var f = new Fight(); f.Condition(ConditionKind.Charmed, source: f.B.Id);
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller())); f.PA.RemoveCondition(f.PA.State.Conditions[0].Id);
        f.Condition(ConditionKind.Frightened, source: f.B.Id);
        Assert.Throws<RuleViolation>(() => f.Encounter.Move(f.Actor, new(f.A, f.PA), 1, MovementMode.Walk, false, true));
        var r = f.Attack(new FixedDiceRoller(15, 2), f.Options with { Context = new(5, true, true, VisibleFearSources: [f.B.Id]) });
        Assert.Equal(AdvantageState.Disadvantage, r.AttackRoll.AdvantageState);
    }
    [Fact]
    public void IncorrectGuessedLocationMissesEvenNaturalTwenty()
    {
        var f = new Fight(); var r = f.Attack(new FixedDiceRoller(20), f.Options with { Context = new(5, true, true, TargetLocationCorrect: false) });
        Assert.False(r.Hit); Assert.False(r.Critical); Assert.Null(r.DamageRoll);
    }
    [Fact]
    public void ExhaustionAffectsDeathSaveThresholdButNotNaturalResults()
    {
        var hp = new HitPoints(20); hp.Damage(20); hp.DeathSave(new FixedDiceRoller(11), -2);
        Assert.Equal(1, hp.State.DeathFailures); hp.DeathSave(new FixedDiceRoller(20), -10); Assert.Equal(1, hp.State.Current);
    }
    [Theory]
    [InlineData(Cover.None, 13, true)] [InlineData(Cover.Half, 15, false)] [InlineData(Cover.ThreeQuarters, 18, false)]
    public void CoverAddsToAc(Cover cover, int ac, bool hit)
    {
        var f = new Fight(); var r = f.Attack(new FixedDiceRoller(7, 2), f.Options with { Context = new(5, true, true, Cover: cover) });
        Assert.Equal(ac, r.ArmorClass); Assert.Equal(hit, r.Hit);
    }
    [Fact]
    public void InvalidCoverRangeAndOwnershipDoNotConsumeResources()
    {
        var f = new Fight();
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), f.Options with { Context = new(5, true, true, Cover: Cover.Total) }));
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), f.Options with { Context = new(6, true, true) }));
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), f.Options with { WeaponId = Guid.NewGuid() }));
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), f.Options with { Ability = Ability.Dexterity }));
        Assert.False(f.Encounter.Combatant(f.Actor).Resources.ActionUsed); Assert.Equal(20, f.B.Health.State.Current);
    }
    [Fact]
    public void VersatileAndReachUseDefinitionProperties()
    {
        var weapon = Sword with { Properties = [WeaponProperty.Versatile, WeaponProperty.Reach] };
        var f = new Fight(weapon); var r = f.Attack(new FixedDiceRoller(10, 10), f.Options with { Hands = 2, Context = new(10, true, true) }, weapon);
        Assert.Equal("1d10", r.DamageRoll!.Expression); Assert.Equal(14, r.Damage!.RawDamage);
    }
    [Fact]
    public void FinesseChoosesRequestedAbilityAndMissingProficiencyAddsNothing()
    {
        var weapon = Sword with { Properties = [WeaponProperty.Finesse], VersatileDice = null };
        var f = new Fight(weapon); f.PA.ImportCapabilities(f.PA.State.Capabilities with { WeaponProficiencies = [] });
        var r = f.Attack(new FixedDiceRoller(15, 2), f.Options with { Ability = Ability.Dexterity }, weapon);
        Assert.Equal(2, r.AttackRoll.Modifier); Assert.Equal(0, r.ProficiencyBonus); Assert.Equal(4, r.Damage!.AppliedDamage);
    }
    [Fact]
    public void HeavyRangedUsesDexterityAndCloseThreatAddsDisadvantage()
    {
        var weapon = Sword with { Kind = WeaponKind.Ranged, Properties = [WeaponProperty.Heavy, WeaponProperty.Ammunition],
            NormalRange = 30, LongRange = 90, Ammunition = "bullet", VersatileDice = null };
        var f = new Fight(weapon);
        var r = f.Attack(new FixedDiceRoller(15, 3), f.Options with { Mode = AttackMode.Ranged, Context = new(5, true, true, CloseRangedThreat: true) }, weapon);
        Assert.Equal(AdvantageState.Disadvantage, r.AttackRoll.AdvantageState); Assert.Equal(Ability.Dexterity, r.Ability);
    }
    [Fact]
    public void BlowgunCriticalDoesNotInventDiceOrAbilityDamage()
    {
        var weapon = Sword with { Id = "blowgun", Kind = WeaponKind.Ranged, DamageDice = null, FixedDamage = 1,
            Properties = [WeaponProperty.Ammunition, WeaponProperty.Loading], NormalRange = 25, LongRange = 100, Ammunition = "needle", VersatileDice = null };
        var f = new Fight(weapon); var r = f.Attack(new FixedDiceRoller(20), f.Options with { Mode = AttackMode.Ranged }, weapon);
        Assert.True(r.Critical); Assert.Null(r.DamageRoll); Assert.Equal(0, r.DamageModifier); Assert.Equal(1, r.Damage!.RawDamage);
    }
    [Fact]
    public void DartCannotAvoidThrownConsumptionByUsingRangedMode()
    {
        var weapon = Sword with { Id = "dart", Kind = WeaponKind.Ranged, Properties = [WeaponProperty.Thrown, WeaponProperty.Finesse],
            NormalRange = 20, LongRange = 60, VersatileDice = null };
        var f = new Fight(weapon);
        Assert.Throws<RuleViolation>(() => f.Attack(new FixedDiceRoller(), f.Options with { Mode = AttackMode.Ranged }, weapon));
        f.Attack(new FixedDiceRoller(1), f.Options with { Mode = AttackMode.Thrown }, weapon); Assert.False(f.PA.Weapon(f.Weapon).Available);
    }
    [Fact]
    public void UnseenAdvantageCancelsProneDisadvantageAndIncomingProneDependsOnDistance()
    {
        var f = new Fight(); f.Condition(ConditionKind.Prone);
        var r = f.Attack(new FixedDiceRoller(10, 2), f.Options with { Context = new(5, true, false) });
        Assert.Equal(AdvantageState.Normal, r.AttackRoll.AdvantageState);
    }
}
