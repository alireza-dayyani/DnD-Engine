namespace DndEngine.Domain.Combat;

// Visibility and spatial facts are adjudicated by the caller, including special senses.
public sealed record AttackContext(int DistanceFeet, bool AttackerCanSeeTarget, bool TargetCanSeeAttacker,
    bool TargetLocationCorrect = true, bool CloseRangedThreat = false, Cover Cover = Cover.None,
    Guid[]? VisibleFearSources = null, bool OpportunityProvoked = false);
public sealed record WeaponAttackOptions(Guid TargetId, Guid WeaponId, AttackMode Mode, AttackContext Context,
    AttackUse Use = AttackUse.Action, Ability? Ability = null, int Hands = 1, bool FreeHandToLoad = true,
    bool Mounted = false, bool Advantage = false, bool Disadvantage = false, int OtherModifier = 0, int FlatDamageAdjustment = 0);
public sealed record WeaponDamageRoll(string Expression, IReadOnlyList<int> Rolls, int Total);
public sealed record WeaponAttackResult(Guid AttackerId, Guid TargetId, Guid WeaponId, string DefinitionId,
    AttackUse Use, Ability Ability, int AbilityModifier, int ProficiencyBonus, int OtherModifier, int ConditionModifier,
    int ArmorClass, D20Roll AttackRoll, bool Hit, bool Critical, WeaponDamageRoll? DamageRoll, int DamageModifier,
    DamageResolution? Damage, HealthChange? Health, TurnResources Resources, WeaponAttackOptions Options, string Source);

public sealed class WeaponAttackResolver(IDiceRoller dice)
{
    public WeaponAttackResult Resolve(CombatEncounter encounter, Guid attackerId, Character attacker, CombatProfile attackerProfile,
        Character target, CombatProfile targetProfile, WeaponDefinition weapon, WeaponAttackOptions options)
    {
        encounter.RequireActive(); Guard.Defined(options.Mode); Guard.Defined(options.Use);
        var context = options.Context ?? throw new RuleViolation("Attack context is required.");
        Guard.Defined(context.Cover); Guard.Range(context.DistanceFeet, 0, 10000, "Distance");
        Guard.Range(options.OtherModifier, -100, 100, "Attack modifier");
        Guard.Range(options.FlatDamageAdjustment, -100000, 100000, "Damage adjustment");
        Guard.Range(options.Hands, 1, 2, "Hands");
        var actor = encounter.Combatant(attackerId); var defender = encounter.Combatant(options.TargetId);
        if (actor.CharacterId != attacker.Id || defender.CharacterId != target.Id || attackerId == options.TargetId)
            throw new RuleViolation("Invalid attack participants.");
        var own = new ConditionEffects(attacker, attackerProfile); var other = new ConditionEffects(target, targetProfile);
        own.RequireAction(); target.Health.RequireAlive();
        if (own.CannotAttack(target.Id)) throw new RuleViolation("Cannot attack the charmer.");
        weapon.Validate(); var instance = attackerProfile.Weapon(options.WeaponId);
        if (instance.DefinitionId != weapon.Id || !instance.Available) throw new RuleViolation("Weapon is not available.");
        if (weapon.Has(WeaponProperty.Ammunition) && (instance.AmmunitionRemaining < 1 || options.Hands == 1 && !options.FreeHandToLoad))
            throw new RuleViolation("Ammunition and a free loading hand are required.");
        if (weapon.Has(WeaponProperty.TwoHanded) && options.Hands < 2 && !(weapon.MountedOneHanded && options.Mounted))
            throw new RuleViolation("This weapon requires two hands.");
        if (context.Cover == Cover.Total) throw new RuleViolation("Total cover prevents this attack.");
        var melee = options.Mode == AttackMode.Melee;
        if (melee && weapon.Kind != WeaponKind.Melee || options.Mode == AttackMode.Ranged && (weapon.Kind != WeaponKind.Ranged || weapon.Has(WeaponProperty.Thrown)) ||
            options.Mode == AttackMode.Thrown && !weapon.Has(WeaponProperty.Thrown)) throw new RuleViolation("Unsupported weapon attack mode.");
        if (context.DistanceFeet > (melee ? weapon.Has(WeaponProperty.Reach) ? 10 : 5 : weapon.LongRange))
            throw new RuleViolation("Target is out of range.");
        var ability = weapon.Kind == WeaponKind.Melee ? Domain.Ability.Strength : Domain.Ability.Dexterity;
        if (weapon.Has(WeaponProperty.Finesse)) ability = options.Ability ??
            (attacker.AbilityModifier(Domain.Ability.Dexterity) > attacker.AbilityModifier(Domain.Ability.Strength) ? Domain.Ability.Dexterity : Domain.Ability.Strength);
        if (options.Ability is not null && options.Ability != ability || ability is not (Domain.Ability.Strength or Domain.Ability.Dexterity))
            throw new RuleViolation("Invalid weapon ability.");
        var resources = actor.Resources;
        if (options.Use == AttackUse.Opportunity)
        {
            if (!melee || !context.OpportunityProvoked || !context.AttackerCanSeeTarget || own.Has(ConditionKind.Blinded) || defender.Resources.Disengaging || resources.ReactionUsed)
                throw new RuleViolation("Opportunity attack is not available.");
            resources = resources with { ReactionUsed = true };
        }
        else
        {
            encounter.RequireTurn(attackerId);
            if (options.Use == AttackUse.LightBonus)
            {
                if (resources.BonusActionUsed || !weapon.Has(WeaponProperty.Light) || !(resources.LightWeaponsUsed ?? []).Any(x => x != instance.Id))
                    throw new RuleViolation("A Light bonus attack requires a different Light weapon used in the Attack action.");
                resources = resources with { BonusActionUsed = true };
            }
            else
            {
                var remaining = resources.ActionUsed ? resources.AttacksRemaining : attackerProfile.State.Capabilities.AttacksPerAction;
                if (remaining == 0) throw new RuleViolation("No attacks remain in this action.");
                if (weapon.Has(WeaponProperty.Loading) && (resources.LoadingWeaponsUsed ?? []).Contains(instance.Id))
                    throw new RuleViolation("Loading permits only one shot from this weapon per action.");
                resources = resources with { ActionUsed = true, AttacksRemaining = remaining - 1,
                    LightWeaponsUsed = weapon.Has(WeaponProperty.Light) ? [.. resources.LightWeaponsUsed ?? [], instance.Id] : resources.LightWeaponsUsed,
                    LoadingWeaponsUsed = weapon.Has(WeaponProperty.Loading) ? [.. resources.LoadingWeaponsUsed ?? [], instance.Id] : resources.LoadingWeaponsUsed };
            }
        }
        var disadvantage = options.Disadvantage || own.OwnAttacksHaveDisadvantage || own.FearVisible(context.VisibleFearSources ?? []) ||
            own.GrappleDisadvantage(target.Id) || !context.AttackerCanSeeTarget ||
            other.Has(ConditionKind.Prone) && context.DistanceFeet > 5 ||
            defender.Resources.Dodging && other.CanAct && other.Speed > 0 && context.TargetCanSeeAttacker ||
            !melee && (context.DistanceFeet > weapon.NormalRange || context.CloseRangedThreat) ||
            weapon.Has(WeaponProperty.Heavy) && attacker.Abilities[weapon.Kind == WeaponKind.Melee ? Domain.Ability.Strength : Domain.Ability.Dexterity].Value < 13;
        var advantage = options.Advantage || other.AttacksAgainstHaveAdvantage || !context.TargetCanSeeAttacker ||
            other.Has(ConditionKind.Prone) && context.DistanceFeet <= 5;
        var modifier = attacker.AbilityModifier(ability);
        var proficiency = attackerProfile.State.Capabilities.WeaponProficiencies.Contains(weapon.Id) ? attacker.Level.ProficiencyBonus : 0;
        var ac = target.ArmorClass + (context.Cover == Cover.Half ? 2 : context.Cover == Cover.ThreeQuarters ? 5 : 0);
        var roll = D20Roll.Make(dice, modifier + proficiency + options.OtherModifier + own.D20Penalty, advantage, disadvantage);
        var hit = context.TargetLocationCorrect && roll.SelectedRoll != 1 && (roll.SelectedRoll == 20 || roll.Total >= ac);
        var critical = hit && (roll.SelectedRoll == 20 || other.CriticalWithinFiveFeet && context.DistanceFeet <= 5);
        DiceResult? damageRoll = null; DamageResolution? damage = null; HealthChange? health = null;
        var damageModifier = weapon.DamageDice is null ? 0 : options.Use == AttackUse.LightBonus ? Math.Min(0, modifier) : modifier;
        if (hit)
        {
            var expression = weapon.DamageDice is null ? null : DiceExpression.Parse(melee && options.Hands == 2 && weapon.VersatileDice is not null ? weapon.VersatileDice : weapon.DamageDice);
            if (expression is not null) damageRoll = dice.Roll(new(expression.Count * (critical ? 2 : 1), expression.Sides));
            var raw = Math.Max(0, (damageRoll?.Total ?? weapon.FixedDamage) + damageModifier);
            var traits = targetProfile.State.Capabilities;
            damage = DamageResolver.Resolve(raw, options.FlatDamageAdjustment, weapon.DamageType, traits.Immunities.Contains(weapon.DamageType),
                other.Resists(weapon.DamageType), traits.Vulnerabilities.Contains(weapon.DamageType));
            health = target.Health.Damage(damage.AppliedDamage, critical);
            if (target.Health.State.Current == 0 && defender.ZeroHpPolicy == ZeroHpPolicy.Die) { target.Health.Die(); health = health with { After = target.Health.State }; }
        }
        attackerProfile.SpendWeapon(instance.Id, weapon.Has(WeaponProperty.Ammunition), options.Mode == AttackMode.Thrown);
        encounter.SetResources(attackerId, resources);
        return new(attackerId, defender.Id, instance.Id, weapon.Id, options.Use, ability, modifier, proficiency, options.OtherModifier,
            own.D20Penalty, ac, roll, hit, critical, damageRoll is null ? null : new(damageRoll.Expression.ToString(), damageRoll.Rolls, damageRoll.Total), damageModifier, damage, health, resources, options, weapon.Source);
    }
}
