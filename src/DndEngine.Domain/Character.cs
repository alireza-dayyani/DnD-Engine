using System.Collections.ObjectModel;

namespace DndEngine.Domain;

public sealed class Character
{
    public Guid Id { get; }
    public Guid CampaignId { get; }
    public string Name { get; }
    public CharacterLevel Level { get; }
    public IReadOnlyDictionary<Ability, AbilityScore> Abilities { get; }
    public IReadOnlySet<string> SkillProficiencies { get; }
    public IReadOnlySet<Ability> SavingThrowProficiencies { get; }
    public int ArmorClass { get; }
    public HitPoints Health { get; }
    public long Revision { get; }

    public Character(Guid id, Guid campaignId, string name, int level, IReadOnlyDictionary<Ability, int> abilities,
        IEnumerable<string> skills, IEnumerable<Ability> saves, int armorClass, HitPoints health, long revision = 0)
    {
        if (id == Guid.Empty || campaignId == Guid.Empty) throw new RuleViolation("Character and campaign IDs are required.");
        if (abilities.Count != 6 || Enum.GetValues<Ability>().Any(a => !abilities.ContainsKey(a)))
            throw new RuleViolation("Exactly six abilities are required.");
        Id = id; CampaignId = campaignId; Name = Guard.Name(name); Level = new(level);
        Abilities = new ReadOnlyDictionary<Ability, AbilityScore>(abilities.ToDictionary(x => x.Key, x => new AbilityScore(x.Value)));
        var skillList = skills.ToArray();
        var saveList = saves.ToArray();
        if (skillList.Any(string.IsNullOrWhiteSpace) || skillList.Distinct(StringComparer.Ordinal).Count() != skillList.Length ||
            saveList.Distinct().Count() != saveList.Length) throw new RuleViolation("Invalid or duplicate proficiencies.");
        foreach (var save in saveList) Guard.Defined(save);
        SkillProficiencies = new ReadOnlySet<string>(skillList.ToHashSet(StringComparer.Ordinal));
        SavingThrowProficiencies = new ReadOnlySet<Ability>(saveList.ToHashSet());
        ArmorClass = Guard.Range(armorClass, 1, 1000, "Armor class"); Health = health;
        if (revision < 0) throw new RuleViolation("Invalid revision.");
        Revision = revision;
    }
    public int AbilityModifier(Ability ability) => Abilities[Guard.Defined(ability)].Modifier;
    public int SkillModifier(SkillDefinition skill, Ability? ability = null) => AbilityModifier(ability ?? skill.Ability) +
        (SkillProficiencies.Contains(skill.Id) ? Level.ProficiencyBonus : 0);
    public int SavingThrowModifier(Ability ability) => AbilityModifier(ability) +
        (SavingThrowProficiencies.Contains(ability) ? Level.ProficiencyBonus : 0);
}
