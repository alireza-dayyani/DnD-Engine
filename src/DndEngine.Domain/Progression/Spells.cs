namespace DndEngine.Domain.Progression;

public enum SpellEffectKind { SelfHealing }

public static class SpellPackVersions
{
    public const string Initial = "1";
    public const string Current = "2";
}

public sealed record SpellDefinition(string Id, string Name, int Level, string[] ClassIds,
    string CastingTime, string Range, string Components, SpellEffectKind Effect, string Source,
    int DicePerSlotLevel = 0, int DieSides = 0);

public sealed record PreparedSpell(string ClassId, string SpellId);
