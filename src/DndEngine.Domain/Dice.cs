using System.Text.RegularExpressions;

namespace DndEngine.Domain;

public sealed partial record DiceExpression
{
    public int Count { get; }
    public int Sides { get; }
    public int Modifier { get; }
    public DiceExpression(int count, int sides, int modifier = 0)
    {
        Count = Guard.Range(count, 1, 1000, "Dice count");
        if (sides is not (4 or 6 or 8 or 10 or 12 or 20 or 100)) throw new RuleViolation("Unsupported die size.");
        Sides = sides;
        Modifier = Guard.Range(modifier, -1000000, 1000000, "Dice modifier");
    }
    public static DiceExpression Parse(string? text)
    {
        if (text is null || text.Length > 64) throw new RuleViolation("Invalid dice expression.");
        var match = Pattern().Match(text.Trim());
        if (!match.Success) throw new RuleViolation("Expected NdS optionally followed by +M or -M.");
        var countText = match.Groups[1].Value;
        if (!int.TryParse(countText.Length == 0 ? "1" : countText, out var count) ||
            !int.TryParse(match.Groups[2].Value, out var sides) ||
            !int.TryParse(match.Groups[3].Success ? match.Groups[3].Value : "0", out var modifier))
            throw new RuleViolation("Dice expression is out of range.");
        return new(count, sides, modifier);
    }
    public override string ToString() => $"{Count}d{Sides}" + (Modifier == 0 ? "" : Modifier > 0 ? $"+{Modifier}" : $"{Modifier}");
    [GeneratedRegex(@"^(\d*)[dD](\d+)([+-]\d+)?$", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();
}

public sealed record DiceResult
{
    public DiceExpression Expression { get; }
    public IReadOnlyList<int> Rolls { get; }
    public int Total => Rolls.Sum() + Expression.Modifier;
    public DiceResult(DiceExpression expression, IEnumerable<int> rolls)
    {
        var values = rolls.ToArray();
        if (values.Length != expression.Count || values.Any(x => x < 1 || x > expression.Sides))
            throw new RuleViolation("Roller returned invalid dice.");
        Expression = expression; Rolls = Array.AsReadOnly(values);
    }
}

public interface IDiceRoller { DiceResult Roll(DiceExpression expression); }

/// <summary>Deterministic scripted dice for tests and reproducible simulations. Never registered in production.</summary>
public sealed class FixedDiceRoller(params int[] values) : IDiceRoller
{
    private readonly Queue<int> remaining = new(values);
    public DiceResult Roll(DiceExpression expression)
    {
        if (remaining.Count < expression.Count) throw new RuleViolation("Fixed dice exhausted.");
        return new(expression, Enumerable.Range(0, expression.Count).Select(_ => remaining.Dequeue()));
    }
}
