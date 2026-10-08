using DndEngine.Domain.Combat;

namespace DndEngine.Domain.Monsters;

public sealed record MonsterActionDefinition(string Id, string Name, string? WeaponId,
    int AttackBonus, string? DamageDice, int DamageModifier, DamageType? DamageType,
    bool Supported, string? DeferredReason = null, bool Reaction = false,
    int? UsesPerDay = null, int ReachFeet = 0, int NormalRangeFeet = 0,
    int LongRangeFeet = 0);

public sealed record MonsterSpellDefinition(string SpellId, string Frequency,
    bool Supported, string? DeferredReason = null,
    string? LimitedUseGroup = null, int UsesPerDay = 0);
public sealed record MonsterGearDefinition(string DefinitionId, int Quantity);

public sealed record MonsterDefinition(string Id, string Name, string Type, string Size,
    string Alignment, string ChallengeRating, int ExperiencePoints,
    Dictionary<Ability, int> Abilities, int HitPoints, int ArmorClass, int Speed,
    Dictionary<Ability, int> SavingThrows, Dictionary<string, int> Skills,
    string[] Senses, string[] Languages, DamageType[] Resistances,
    DamageType[] Immunities, DamageType[] Vulnerabilities,
    ConditionKind[] ConditionImmunities, MonsterGearDefinition[] Gear,
    string[] Traits, MonsterActionDefinition[] Actions,
    MonsterSpellDefinition[] Spells)
{
    public void Validate()
    {
        Guard.Name(Id); Guard.Name(Name); Guard.Name(Type); Guard.Name(Size);
        Guard.Name(Alignment); Guard.Name(ChallengeRating);
        Guard.Range(ExperiencePoints, 0, 200_000, "Monster XP");
        Guard.Range(HitPoints, 1, 100_000, "Monster HP");
        Guard.Range(ArmorClass, 1, 100, "Monster AC");
        Guard.Range(Speed, 0, 1000, "Monster speed");
        if (Abilities is null || Abilities.Count != 6 ||
            Enum.GetValues<Ability>().Any(x => !Abilities.ContainsKey(x)) ||
            Abilities.Values.Any(x => x is < 1 or > 30) ||
            SavingThrows is null || Skills is null || Senses is null || Languages is null ||
            Resistances is null || Immunities is null || Vulnerabilities is null ||
            ConditionImmunities is null || Gear is null || Traits is null ||
            Actions is null || Spells is null)
            throw new RuleViolation("Invalid monster definition.");
        if (SavingThrows.Keys.Any(x=>!Enum.IsDefined(x)) ||
            SavingThrows.Values.Any(x=>x is < -20 or > 30) ||
            Skills.Keys.Any(string.IsNullOrWhiteSpace) ||
            Skills.Values.Any(x=>x is < -20 or > 30) ||
            Senses.Any(string.IsNullOrWhiteSpace) || Languages.Any(string.IsNullOrWhiteSpace) ||
            Traits.Any(string.IsNullOrWhiteSpace))
            throw new RuleViolation("Invalid monster statistics.");
        if (Actions.Select(x => x.Id).Distinct(StringComparer.Ordinal).Count() != Actions.Length ||
            Spells.Select(x => x.SpellId).Distinct(StringComparer.Ordinal).Count() != Spells.Length ||
            Gear.Select(x=>x.DefinitionId).Distinct(StringComparer.Ordinal).Count() != Gear.Length ||
            Gear.Any(x=>string.IsNullOrWhiteSpace(x.DefinitionId) || x.Quantity is < 1 or > 1000))
            throw new RuleViolation("Duplicate monster ability or gear.");
        foreach (var type in Resistances.Concat(Immunities).Concat(Vulnerabilities)) Guard.Defined(type);
        foreach (var condition in ConditionImmunities) Guard.Defined(condition);
        foreach (var action in Actions)
        {
            Guard.Name(action.Id); Guard.Name(action.Name);
            if (action.Supported && (action.WeaponId is null || action.DamageDice is null ||
                action.DamageType is null || action.DeferredReason is not null) ||
                !action.Supported && string.IsNullOrWhiteSpace(action.DeferredReason))
                throw new RuleViolation("Monster action support must be explicit.");
            if (action.DamageDice is not null) DiceExpression.Parse(action.DamageDice);
            if (action.ReachFeet < 0 || action.NormalRangeFeet < 0 ||
                action.LongRangeFeet < action.NormalRangeFeet)
                throw new RuleViolation("Invalid monster action range.");
        }
        foreach (var spell in Spells)
        {
            Guard.Name(spell.SpellId); Guard.Name(spell.Frequency);
            if (!spell.Supported && string.IsNullOrWhiteSpace(spell.DeferredReason))
                throw new RuleViolation("Deferred monster spells need a reason.");
            if (spell.LimitedUseGroup is not null && spell.UsesPerDay < 1)
                throw new RuleViolation("Limited-use monster spell needs a daily use count.");
        }
    }
}

public sealed record MonsterPack(string Version, MonsterDefinition[] Monsters);
public sealed record MonsterInstance(Guid Id, Guid CampaignId, string DefinitionId,
    string PackVersion, string SpellPackVersion = "6",
    Dictionary<string,int>? LimitedUsesRemaining = null, long Revision = 0);
