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

public sealed record SpellReplacement(string ClassId, string FromSpellId, string ToSpellId);

public enum SpellPreparationMoment { LongRest, ClassLevelGained }

public static class SpellPreparation
{
    public static PreparedSpell[] Replace(PreparedSpell[] current, ClassLevel[] classes,
        SpellDefinition[] catalog, SpellReplacement[] replacements, SpellPreparationMoment moment,
        string? gainedClassId = null)
    {
        if (replacements.Length == 0) return current;
        foreach (var group in replacements.GroupBy(x => x.ClassId))
        {
            if (!classes.Any(x => x.ClassId == group.Key))
                throw new RuleViolation("Spell replacement class is not owned.");
            var maximum = moment switch
            {
                SpellPreparationMoment.LongRest when group.Key is "cleric" or "druid" => int.MaxValue,
                SpellPreparationMoment.LongRest when group.Key is "paladin" or "ranger" => 1,
                SpellPreparationMoment.ClassLevelGained when group.Key == gainedClassId &&
                    group.Key is "bard" or "sorcerer" or "warlock" => 1,
                _ => 0
            };
            if (group.Count() > maximum)
                throw new RuleViolation("This class cannot replace that many spells at this time.");
        }
        if (replacements.Select(x => (x.ClassId,x.FromSpellId)).Distinct().Count() != replacements.Length)
            throw new RuleViolation("A prepared spell cannot be replaced twice.");
        foreach (var replacement in replacements)
        {
            if (replacement.FromSpellId == replacement.ToSpellId ||
                !current.Any(x => x.ClassId == replacement.ClassId && x.SpellId == replacement.FromSpellId))
                throw new RuleViolation("The spell being replaced is not prepared for this class.");
            var target = catalog.SingleOrDefault(x => x.Id == replacement.ToSpellId);
            if (target is null || target.Level != 1 || !target.ClassIds.Contains(replacement.ClassId))
                throw new RuleViolation("Replacement spell is not an eligible level 1 class spell in this pack.");
        }
        var updated = current.Select(x => replacements.FirstOrDefault(r =>
            r.ClassId == x.ClassId && r.FromSpellId == x.SpellId) is { } replacement
            ? x with { SpellId=replacement.ToSpellId } : x).ToArray();
        if (updated.Select(x => (x.ClassId,x.SpellId)).Distinct().Count() != updated.Length)
            throw new RuleViolation("A spell cannot be prepared twice for the same class.");
        return updated;
    }
}
