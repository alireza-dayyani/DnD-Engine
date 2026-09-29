namespace DndEngine.Domain;

public sealed record CheckOptions(int Dc, bool Advantage = false, bool Disadvantage = false,
    int OtherModifier = 0, bool VoluntaryFailure = false);

public sealed record CheckResult(Guid CharacterId, CheckKind Kind, string? Skill, Ability Ability,
    IReadOnlyList<int> Rolls, int? SelectedRoll, int AbilityModifier, int ProficiencyModifier, int OtherModifier,
    int? Total, int Dc, bool Success, AdvantageState AdvantageState, string? AutomaticOutcome);

public sealed class CheckResolver(IDiceRoller dice)
{
    public CheckResult Resolve(Character character, CheckKind kind, Ability ability, CheckOptions options, SkillDefinition? skill = null)
    {
        Guard.Defined(kind); Guard.Defined(ability);
        Guard.Range(options.Dc, 0, 1000000, "DC");
        Guard.Range(options.OtherModifier, -1000000, 1000000, "Other modifier");
        if ((kind == CheckKind.Skill) != (skill is not null)) throw new RuleViolation("Skill definition required only for skill checks.");
        if (options.VoluntaryFailure && kind != CheckKind.SavingThrow) throw new RuleViolation("Only saving throws support voluntary failure.");
        character.Health.RequireAlive();
        if (kind != CheckKind.SavingThrow && character.Health.State.Unconscious)
            throw new RuleViolation("An unconscious character cannot attempt an active ability check.");
        var abilityModifier = character.AbilityModifier(ability);
        var proficiency = kind switch {
            CheckKind.Skill => character.SkillProficiencies.Contains(skill!.Id),
            CheckKind.SavingThrow => character.SavingThrowProficiencies.Contains(ability),
            _ => false };
        var proficiencyModifier = proficiency ? character.Level.ProficiencyBonus : 0;
        var state = options.Advantage == options.Disadvantage ? AdvantageState.Normal :
            options.Advantage ? AdvantageState.Advantage : AdvantageState.Disadvantage;
        var automatic = options.VoluntaryFailure ? "VoluntaryFailure" :
            kind == CheckKind.SavingThrow && character.Health.State.Unconscious && ability is Ability.Strength or Ability.Dexterity
                ? "Unconscious" : null;
        if (automatic is not null)
            return new(character.Id, kind, skill?.Id, ability, [], null, abilityModifier, proficiencyModifier,
                options.OtherModifier, null, options.Dc, false, state, automatic);
        var rolls = dice.Roll(new(state == AdvantageState.Normal ? 1 : 2, 20)).Rolls;
        var selected = state == AdvantageState.Disadvantage ? rolls.Min() : rolls.Max();
        var total = selected + abilityModifier + proficiencyModifier + options.OtherModifier;
        return new(character.Id, kind, skill?.Id, ability, rolls, selected, abilityModifier, proficiencyModifier,
            options.OtherModifier, total, options.Dc, total >= options.Dc, state, null);
    }
}
