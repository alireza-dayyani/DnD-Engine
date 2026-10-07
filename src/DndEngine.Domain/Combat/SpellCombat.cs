using DndEngine.Domain.Progression;

namespace DndEngine.Domain.Combat;

// As with weapon attacks, the caller adjudicates positions and sight. The engine validates
// the supplied facts against spell range and resolves every target before one atomic save.
public sealed record SpellTargetContext(Guid CombatantId, int DistanceFeet, bool CasterCanSeeTarget,
    bool TargetCanSeeCaster = true, Cover Cover = Cover.None, bool IgnoreBlur = false,
    int? DistanceFromAreaCenterFeet = null, bool CloseRangedThreat = false,
    Guid[]? VisibleFearSources = null, bool CasterCanHearTarget = false,
    bool TargetLocationCorrect = true);
public sealed record SpellTargetResult(Guid CombatantId, D20Roll? AttackRoll, D20Roll? SavingThrow,
    bool HitOrFailedSave, int[] DamageOrHealingRolls, DamageResolution? Damage, HealthChange? Health);
public sealed record CombatSpellResolution(string ClassId, string SpellId, int SpellLevel,
    SpellSlotPoolKind? Pool, int? SlotBefore, TurnResources Resources, SpellTargetResult[] Targets,
    ActiveSpellEffect? ActiveEffect = null, MetamagicOption? Metamagic = null,
    int? SorceryPointsAfter = null, bool MysticArcanumSpent = false,
    AttackPenaltyEffect[]? AppliedAttackPenalties = null);
public sealed record ReactionSpellResolution(string SpellId, int SpellLevel, SpellSlotPoolKind Pool,
    int SlotBefore, TurnResources Resources, SpellTargetResult Target);

public sealed class SpellCombatResolver(IDiceRoller dice)
{
    public SpellTargetResult[] Resolve(CombatEncounter encounter, Guid casterId, Character caster,
        CombatProfile casterProfile, IReadOnlyDictionary<Guid, Character> characters,
        IReadOnlyDictionary<Guid, CombatProfile> profiles, SpellDefinition spell,
        ClassSpellcasting casting, SpellTargetContext[] targets, int spellLevel,
        int? areaCenterDistanceFeet = null, bool distantSpell = false,
        Guid? heightenedTargetId = null)
    {
        if (targets is null || targets.Length == 0 || targets.Length > 100 ||
            spell.Id != "eldritch-blast" && targets.Select(x => x.CombatantId).Distinct().Count() != targets.Length)
            throw new RuleViolation("Spell targets must be distinct and nonempty, except Eldritch Blast beams.");
        var beamCount = caster.Level.Value >= 17 ? 4 : caster.Level.Value >= 11 ? 3 : caster.Level.Value >= 5 ? 2 : 1;
        if (spell.Id == "eldritch-blast" && targets.Length != beamCount)
            throw new RuleViolation("Eldritch Blast requires one target per beam.");
        if (spell.Damage?.Area != true && spell.Id != "eldritch-blast" && targets.Length != 1)
            throw new RuleViolation("This spell targets exactly one combatant.");
        var casterEffects = new ConditionEffects(caster,casterProfile);
        var maxDistance = spell.Range switch
        {
            "Touch" => 5,
            "Self" => 0,
            "60 feet" => 60,
            "30 feet" => 30,
            "120 feet" => 120,
            "150 feet" => 150,
            "Self (15-foot Cone)" => 15,
            _ => throw new RuleViolation("Spell range is not implemented in combat.")
        };
        if (distantSpell)
        {
            if (spell.Range.StartsWith("Self",StringComparison.Ordinal))
                throw new RuleViolation("Distant Spell cannot change a Self range.");
            maxDistance = spell.Range == "Touch" ? 30 : checked(maxDistance*2);
        }
        if (spell.Id == "circle-of-death" &&
            (areaCenterDistanceFeet is null or < 0 || areaCenterDistanceFeet > maxDistance ||
             targets.Any(x => x.DistanceFromAreaCenterFeet is null or < 0 or > 60)))
            throw new RuleViolation("Circle of Death requires a point within 150 feet and targets within its 60-foot radius.");
        foreach (var target in targets)
        {
            Guard.Range(target.DistanceFeet,0,10000,"Spell target distance"); Guard.Defined(target.Cover);
            var member = encounter.Combatant(target.CombatantId);
            if (!characters.ContainsKey(member.CharacterId) || !profiles.ContainsKey(member.CharacterId))
                throw new RuleViolation("Target state is unavailable.");
            if (spell.Id != "circle-of-death" && target.DistanceFeet > maxDistance ||
                target.Cover == Cover.Total ||
                spell.Id == "burning-hands" && target.CombatantId == casterId ||
                spell.Range == "Self" && target.CombatantId != casterId ||
                (spell.Id is "healing-word" or "sacred-flame" && !target.CasterCanSeeTarget) ||
                spell.Id == "vicious-mockery" && !(target.CasterCanSeeTarget || target.CasterCanHearTarget))
                throw new RuleViolation("Spell target is out of range, unseen, or behind total cover.");
            characters[member.CharacterId].Health.RequireAlive();
        }
        var areaRoll = spell.Damage?.Area == true ? RollDamage(spell,spellLevel,caster.Level.Value,false) : [];
        var results = new List<SpellTargetResult>();
        foreach (var target in targets)
        {
            var member = encounter.Combatant(target.CombatantId);
            var character = characters[member.CharacterId];
            var profile = profiles[member.CharacterId];
            var targetEffects = new ConditionEffects(character,profile);
            if (spell.Id == "eldritch-blast" && character.Health.State.Dead)
            {
                results.Add(new(member.Id,null,null,false,[],null,null));
                continue;
            }
            if (spell.Effect == SpellEffectKind.SelfHealing)
            {
                var healingRoll = dice.Roll(new(spell.DicePerSlotLevel*spellLevel,spell.DieSides)).Rolls.ToArray();
                var change = character.Health.Heal(Math.Max(0,healingRoll.Sum()+casting.AbilityModifier));
                results.Add(new(member.Id,null,null,true,healingRoll,null,change));
                continue;
            }
            if (spell.Effect == SpellEffectKind.Blur)
            {
                results.Add(new(member.Id,null,null,true,[],null,null));
                continue;
            }
            var damage = spell.Damage ?? throw new RuleViolation("Spell damage is not implemented.");
            D20Roll? attack = null, save = null;
            var critical = false;
            bool hit;
            if (spell.Effect == SpellEffectKind.SpellAttack)
            {
                if (casterEffects.CannotAttack(character.Id)) throw new RuleViolation("Cannot attack the charmer.");
                attack = D20Roll.Make(dice,casting.SpellAttackBonus+casterEffects.D20Penalty,
                    targetEffects.AttacksAgainstHaveAdvantage || !target.TargetCanSeeCaster ||
                    targetEffects.Has(ConditionKind.Prone) && target.DistanceFeet <= 5,
                    encounter.ConsumeNextAttackPenalty(casterId) || casterEffects.OwnAttacksHaveDisadvantage ||
                    !target.CasterCanSeeTarget ||
                    encounter.HasSpellEffect(member.Id,"blur") && !target.IgnoreBlur ||
                    member.Resources.Dodging && targetEffects.CanAct && targetEffects.Speed > 0 && target.TargetCanSeeCaster ||
                    target.DistanceFeet <= 5 || target.CloseRangedThreat ||
                    targetEffects.Has(ConditionKind.Prone) && target.DistanceFeet > 5 ||
                    casterEffects.FearVisible(target.VisibleFearSources ?? []));
                var ac = character.ArmorClass + (target.Cover == Cover.Half ? 2 : target.Cover == Cover.ThreeQuarters ? 5 : 0);
                hit = target.TargetLocationCorrect && attack.SelectedRoll != 1 &&
                    (attack.SelectedRoll == 20 || attack.Total >= ac);
                critical = hit && (attack.SelectedRoll == 20 ||
                    targetEffects.CriticalWithinFiveFeet && target.DistanceFeet <= 5);
            }
            else if (spell.Effect == SpellEffectKind.SavingThrowDamage && damage.SaveAbility is { } ability)
            {
                var automaticFailure = targetEffects.PhysicalSaveFailure && ability is Ability.Strength or Ability.Dexterity;
                if (!automaticFailure)
                {
                    var modifier = character.AbilityModifier(ability) +
                        (character.SavingThrowProficiencies.Contains(ability) ? character.Level.ProficiencyBonus : 0) +
                        targetEffects.D20Penalty + (damage.IgnoreCover ? 0 : target.Cover == Cover.Half ? 2 : target.Cover == Cover.ThreeQuarters ? 5 : 0);
                    save = D20Roll.Make(dice,modifier,
                        ability == Ability.Dexterity && member.Resources.Dodging && targetEffects.CanAct && targetEffects.Speed > 0,
                        target.CombatantId == heightenedTargetId ||
                        ability == Ability.Dexterity && targetEffects.Has(ConditionKind.Restrained));
                }
                hit = automaticFailure || save!.Total < casting.SpellSaveDc;
            }
            else throw new RuleViolation("Spell effect is not implemented in combat.");
            var rolled = hit || damage.HalfOnSave ? damage.Area ? areaRoll : RollDamage(spell,spellLevel,caster.Level.Value,
                critical) : [];
            DamageResolution? resolved = null; HealthChange? health = null;
            if (rolled.Length > 0)
            {
                var raw = hit ? rolled.Sum() : rolled.Sum()/2;
                var traits = profile.State.Capabilities;
                resolved = DamageResolver.Resolve(raw,0,damage.Type,traits.Immunities.Contains(damage.Type),
                    targetEffects.Resists(damage.Type),traits.Vulnerabilities.Contains(damage.Type));
                health = character.Health.Damage(resolved.AppliedDamage,critical);
                if (character.Health.State.Current == 0 && member.ZeroHpPolicy == ZeroHpPolicy.Die)
                {
                    character.Health.Die(); health = health with { After = character.Health.State };
                }
            }
            results.Add(new(member.Id,attack,save,hit,rolled,resolved,health));
            if (spell.Id == "vicious-mockery" && hit)
                encounter.ApplyNextAttackPenalty(member.Id,spell.Id);
        }
        return results.ToArray();
    }

    private int[] RollDamage(SpellDefinition spell,int slotLevel,int characterLevel,bool critical)
    {
        var damage = spell.Damage!;
        var diceCount = spell.Id == "eldritch-blast" ? 1 : spell.Level == 0
            ? damage.BaseDice * (characterLevel >= 17 ? 4 : characterLevel >= 11 ? 3 : characterLevel >= 5 ? 2 : 1)
            : damage.BaseDice + Math.Max(0,slotLevel-spell.Level)*damage.DicePerHigherSlot;
        return dice.Roll(new(diceCount*(critical ? 2 : 1),damage.DieSides)).Rolls.ToArray();
    }
}
