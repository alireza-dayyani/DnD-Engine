namespace DndEngine.Domain.Combat;

/// <summary>Derived mechanical facets; source instances remain independent. No scripting language.</summary>
public sealed class ConditionEffects(Character character, CombatProfile profile)
{
    public bool Has(ConditionKind kind) => kind switch {
        ConditionKind.Poisoned when profile.Has(ConditionKind.Petrified) => false,
        ConditionKind.Unconscious => profile.Has(kind) || character.Health.State.Unconscious,
        ConditionKind.Prone => profile.Has(kind) || character.Health.State.Prone || Has(ConditionKind.Unconscious),
        ConditionKind.Incapacitated => profile.Has(kind) || Has(ConditionKind.Unconscious) ||
            profile.Has(ConditionKind.Paralyzed) || profile.Has(ConditionKind.Petrified) || profile.Has(ConditionKind.Stunned),
        _ => profile.Has(kind)
    };
    public bool CanAct => !character.Health.State.Dead && !Has(ConditionKind.Incapacitated);
    public int Exhaustion => profile.State.Conditions.Count(x => x.Kind == ConditionKind.Exhaustion);
    public int D20Penalty => -2 * Exhaustion;
    public int Speed => character.Health.State.Dead || Has(ConditionKind.Grappled) || Has(ConditionKind.Restrained) ||
        Has(ConditionKind.Paralyzed) || Has(ConditionKind.Petrified) || Has(ConditionKind.Unconscious)
        ? 0 : Math.Max(0, profile.State.Capabilities.Speed - 5 * Exhaustion);
    public bool PhysicalSaveFailure => Has(ConditionKind.Paralyzed) || Has(ConditionKind.Petrified) || Has(ConditionKind.Stunned) || Has(ConditionKind.Unconscious);
    public bool AttacksAgainstHaveAdvantage => Has(ConditionKind.Blinded) || Has(ConditionKind.Restrained) ||
        Has(ConditionKind.Paralyzed) || Has(ConditionKind.Petrified) || Has(ConditionKind.Stunned) || Has(ConditionKind.Unconscious);
    public bool OwnAttacksHaveDisadvantage => Has(ConditionKind.Blinded) || Has(ConditionKind.Poisoned) || Has(ConditionKind.Prone) || Has(ConditionKind.Restrained);
    public bool CriticalWithinFiveFeet => Has(ConditionKind.Paralyzed) || Has(ConditionKind.Unconscious);
    public bool FearVisible(IReadOnlyCollection<Guid> visibleSources) => profile.State.Conditions.Any(x => x.Kind == ConditionKind.Frightened && x.SourceCharacterId is Guid id && visibleSources.Contains(id));
    public bool CannotAttack(Guid target) => profile.State.Conditions.Any(x => x.Kind == ConditionKind.Charmed && x.SourceCharacterId == target);
    public bool GrappleDisadvantage(Guid target) => profile.State.Conditions.Any(x => x.Kind == ConditionKind.Grappled && x.SourceCharacterId != target);
    public bool Resists(DamageType type) => Has(ConditionKind.Petrified) || profile.State.Capabilities.Resistances.Contains(type);
    public void RequireAction() { if (!CanAct) throw new RuleViolation("Combatant cannot take actions, bonus actions or reactions."); }
}
