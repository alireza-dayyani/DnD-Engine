namespace DndEngine.Domain.Progression;

public enum SpellEffectKind { SelfHealing }

public sealed record SpellDefinition(string Id, string Name, int Level, string[] ClassIds,
    string CastingTime, string Range, string Components, SpellEffectKind Effect, string Source);

public sealed record PreparedSpell(string ClassId, string SpellId);
