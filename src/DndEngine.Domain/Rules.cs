namespace DndEngine.Domain;

public sealed class RuleViolation(string message) : Exception(message);

public static class Guard
{
    public static int Range(int value, int min, int max, string name)
    {
        if (value < min || value > max) throw new RuleViolation($"{name} must be between {min} and {max}.");
        return value;
    }
    public static T Defined<T>(T value) where T : struct, Enum =>
        Enum.IsDefined(value) ? value : throw new RuleViolation($"Unknown {typeof(T).Name}.");
    public static string Name(string? value) => !string.IsNullOrWhiteSpace(value) && value.Length <= 200
        ? value.Trim() : throw new RuleViolation("Name must contain 1–200 characters.");
}

public enum Ability { Strength, Dexterity, Constitution, Intelligence, Wisdom, Charisma }
public enum AdvantageState { Normal, Advantage, Disadvantage }
public enum CheckKind { Ability, Skill, SavingThrow }

public sealed record AbilityScore
{
    public int Value { get; }
    public int Modifier => (int)Math.Floor((Value - 10) / 2.0);
    public AbilityScore(int value) => Value = Guard.Range(value, 1, 30, "Ability score");
}

public sealed record CharacterLevel
{
    public int Value { get; }
    public int ProficiencyBonus => 2 + (Value - 1) / 4;
    public CharacterLevel(int value) => Value = Guard.Range(value, 1, 20, "Character level");
}

public sealed record Ruleset(string Id, string Version)
{
    public static Ruleset Current { get; } = new("dnd-5.5", "5.2.1");
    public void RequireSupported()
    {
        if (this != Current) throw new RuleViolation($"Unsupported ruleset {Id}/{Version}.");
    }
}

public sealed record SkillDefinition(string Id, string Name, Ability Ability, string Source);
public sealed record Campaign
{
    public Guid Id { get; }
    public string Name { get; }
    public Ruleset Ruleset { get; }
    public long GameSeconds { get; }
    public long Revision { get; }
    public Campaign(Guid id, string name, Ruleset ruleset, long gameSeconds = 0, long revision = 0)
    {
        if (id == Guid.Empty || gameSeconds < 0 || revision < 0) throw new RuleViolation("Invalid campaign state.");
        Id = id; Name = Guard.Name(name); Ruleset = ruleset;
        GameSeconds = gameSeconds; Revision = revision;
    }
    public Campaign AdvanceTime(long seconds)
    {
        if (seconds is < 1 or > 31_536_000) throw new RuleViolation("Time advance must be between 1 second and 1 year.");
        return new(Id,Name,Ruleset,checked(GameSeconds+seconds),checked(Revision+1));
    }
}
