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
    private static readonly int[] FullCasterPrepared =
        [4,5,6,7,9,10,11,12,14,15,16,16,17,17,18,18,19,20,21,22];
    private static readonly int[] HalfCasterPrepared =
        [2,3,4,5,6,6,7,7,9,9,10,10,11,11,12,12,14,14,15,15];
    private static readonly int[] SorcererPrepared =
        [2,4,6,7,9,10,11,12,14,15,16,16,17,17,18,18,19,20,21,22];
    private static readonly int[] WarlockPrepared =
        [2,3,4,5,6,7,8,9,10,10,11,11,12,12,13,13,14,14,15,15];
    private static readonly int[] WizardPrepared =
        [4,5,6,7,9,10,11,12,14,15,16,16,17,18,19,21,22,23,24,25];

    public static int Capacity(string classId, int classLevel)
    {
        if (classLevel is < 1 or > 20) throw new RuleViolation("Class level must be between 1 and 20.");
        var table = classId switch
        {
            "bard" or "cleric" or "druid" => FullCasterPrepared,
            "paladin" or "ranger" => HalfCasterPrepared,
            "sorcerer" => SorcererPrepared,
            "warlock" => WarlockPrepared,
            "wizard" => WizardPrepared,
            _ => null
        };
        return table is null ? 0 : table[classLevel-1];
    }

    public static int MaximumSpellLevel(string classId, int classLevel)
    {
        if (classLevel is < 1 or > 20) throw new RuleViolation("Class level must be between 1 and 20.");
        return classId switch
        {
            "paladin" or "ranger" => Math.Min(5,(classLevel+3)/4),
            "warlock" => Math.Min(5,(classLevel+1)/2),
            "bard" or "cleric" or "druid" or "sorcerer" or "wizard" => Math.Min(9,(classLevel+1)/2),
            _ => 0
        };
    }

    public static PreparedSpell[] Add(PreparedSpell[] current, ClassLevel classLevel,
        SpellDefinition[] catalog, string[] spellIds)
    {
        if (spellIds.Length == 0) return current;
        if (classLevel.ClassId == "wizard")
            throw new RuleViolation("Wizard preparation requires a modeled spellbook.");
        var capacity = Capacity(classLevel.ClassId,classLevel.Level);
        if (capacity == 0) throw new RuleViolation("This class cannot prepare spells.");
        var existing = current.Where(x => x.ClassId == classLevel.ClassId).Select(x => x.SpellId).ToHashSet();
        if (spellIds.Distinct(StringComparer.Ordinal).Count() != spellIds.Length ||
            spellIds.Any(existing.Contains) || existing.Count + spellIds.Length > capacity)
            throw new RuleViolation("Additional prepared spells are duplicate or exceed the class allowance.");
        var maximumLevel = MaximumSpellLevel(classLevel.ClassId,classLevel.Level);
        foreach (var id in spellIds)
        {
            var spell = catalog.SingleOrDefault(x => x.Id == id);
            if (spell is null || spell.Level < 1 || spell.Level > maximumLevel ||
                !spell.ClassIds.Contains(classLevel.ClassId))
                throw new RuleViolation("Additional spell is not eligible for this class level and spell pack.");
        }
        return [..current,..spellIds.Select(id => new PreparedSpell(classLevel.ClassId,id))];
    }

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
            var classLevel = classes.Single(x => x.ClassId == replacement.ClassId).Level;
            if (target is null || target.Level < 1 ||
                target.Level > MaximumSpellLevel(replacement.ClassId,classLevel) ||
                !target.ClassIds.Contains(replacement.ClassId))
                throw new RuleViolation("Replacement spell is not eligible for this class level and spell pack.");
        }
        var updated = current.Select(x => replacements.FirstOrDefault(r =>
            r.ClassId == x.ClassId && r.FromSpellId == x.SpellId) is { } replacement
            ? x with { SpellId=replacement.ToSpellId } : x).ToArray();
        if (updated.Select(x => (x.ClassId,x.SpellId)).Distinct().Count() != updated.Length)
            throw new RuleViolation("A spell cannot be prepared twice for the same class.");
        return updated;
    }
}
