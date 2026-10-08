using System.Text.Json.Serialization;
using DndEngine.Domain.Combat;

namespace DndEngine.Domain.Progression;

public enum SpellEffectKind { SelfHealing, SpellAttack, SavingThrowDamage, Blur, LanguageComprehension }

public static class SpellPackVersions
{
    public const string Initial = "1";
    public const string Second = "2";
    public const string Previous = "3";
    public const string Fourth = "4";
    public const string Fifth = "5";
    public const string Sixth = "6";
    public const string Current = "7";
}

public sealed record SpellDefinition(string Id, string Name, int Level, string[] ClassIds,
    string CastingTime, string Range, string Components, SpellEffectKind Effect, string Source,
    int DicePerSlotLevel = 0, int DieSides = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] SpellDamage? Damage = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int ConcentrationTurns = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] int MaterialCostGp = 0,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] string? MaterialItemId = null,
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)] bool Ritual = false);

public sealed record SpellDamage(DamageType Type, int BaseDice, int DieSides, Ability? SaveAbility = null,
    int DicePerHigherSlot = 0, bool HalfOnSave = false, bool IgnoreCover = false, bool Area = false);

public sealed record PreparedSpell(string ClassId, string SpellId);

public sealed record KnownCantrip(string ClassId, string SpellId);
public enum MetamagicOption { QuickenedSpell, SubtleSpell, DistantSpell, HeightenedSpell }

public static class SorceryPoints
{
    private static readonly int[] SlotCosts = [2,3,5,6,7];
    private static readonly int[] MinimumLevels = [2,3,5,7,9];
    public static int Maximum(ProgressionState state) =>
        state.Classes.SingleOrDefault(x => x.ClassId == "sorcerer") is { Level: >= 2 } sorcerer
            ? sorcerer.Level : 0;
    public static int Remaining(ProgressionState state)
    {
        var maximum = Maximum(state);
        if (state.SorceryPointsSpent < 0 || state.SorceryPointsSpent > maximum)
            throw new RuleViolation("Sorcery point expenditure exceeds the class allowance.");
        return maximum-state.SorceryPointsSpent;
    }
    public static ProgressionState Spend(ProgressionState state, MetamagicOption option)
    {
        Guard.Defined(option);
        if (!(state.MetamagicOptions ?? []).Contains(option))
            throw new RuleViolation("Metamagic option is not known.");
        var cost = option is MetamagicOption.QuickenedSpell or MetamagicOption.HeightenedSpell ? 2 : 1;
        if (Remaining(state) < cost) throw new RuleViolation("Insufficient Sorcery Points.");
        return state with { SorceryPointsSpent=state.SorceryPointsSpent+cost };
    }

    public static ProgressionState ConvertSlot(Character character,ProgressionState state,
        SpellSlotPoolKind pool,int spellLevel)
    {
        if (Maximum(state) == 0) throw new RuleViolation("Font of Magic requires Sorcerer level 2.");
        if (Remaining(state)+spellLevel > Maximum(state))
            throw new RuleViolation("Converting this slot would exceed maximum Sorcery Points.");
        var (usage,_) = SpellSlotCalculator.Spend(character,state,pool,spellLevel);
        return state with { SpellSlots=usage,SorceryPointsSpent=state.SorceryPointsSpent-spellLevel };
    }

    public static ProgressionState CreateSlot(ProgressionState state,int spellLevel)
    {
        Guard.Range(spellLevel,1,5,"Created slot level");
        var sorcererLevel = state.Classes.SingleOrDefault(x => x.ClassId == "sorcerer")?.Level ?? 0;
        if (sorcererLevel < MinimumLevels[spellLevel-1])
            throw new RuleViolation("Sorcerer level is too low to create this slot.");
        var cost = SlotCosts[spellLevel-1];
        if (Remaining(state) < cost) throw new RuleViolation("Insufficient Sorcery Points.");
        return state with { SorceryPointsSpent=state.SorceryPointsSpent+cost,
            SpellSlots=SpellSlotCalculator.CreateShared(state,spellLevel) };
    }
}

public static class FeatureSpells
{
    public static PreparedSpell[] AlwaysPrepared(ProgressionState state)
    {
        if (state.SpellPackVersion is not (SpellPackVersions.Previous or SpellPackVersions.Fourth or SpellPackVersions.Fifth or SpellPackVersions.Sixth or SpellPackVersions.Current)) return [];
        var warlock = state.Classes.SingleOrDefault(x => x.ClassId == "warlock");
        return warlock is { Level: >= 3 } &&
            (state.SubclassIds ?? []).GetValueOrDefault("warlock") == "fiend-patron"
            ? [new("warlock","burning-hands")] : [];
    }
}

public static class MysticArcanum
{
    public static int? GrantedSpellLevel(int warlockLevel) => warlockLevel switch
    {
        11 => 6, 13 => 7, 15 => 8, 17 => 9, _ => null
    };
    public static Dictionary<int,string> Choose(Dictionary<int,string> current,int warlockLevel,
        string spellId,SpellDefinition[] catalog)
    {
        var level = GrantedSpellLevel(warlockLevel)
            ?? throw new RuleViolation("This Warlock level grants no Mystic Arcanum.");
        var spell = catalog.SingleOrDefault(x => x.Id == spellId);
        if (current.ContainsKey(level) || spell is null || spell.Level != level ||
            !spell.ClassIds.Contains("warlock"))
            throw new RuleViolation("Mystic Arcanum choice is ineligible for this level and spell pack.");
        return new(current) { [level]=spellId };
    }
    public static ProgressionState Spend(ProgressionState state,int spellLevel,string spellId)
    {
        if (!(state.MysticArcanumChoices ?? []).TryGetValue(spellLevel,out var chosen) ||
            chosen != spellId || (state.MysticArcanumSpentLevels ?? []).Contains(spellLevel))
            throw new RuleViolation("Mystic Arcanum is not available.");
        return state with { MysticArcanumSpentLevels=[..state.MysticArcanumSpentLevels ?? [],spellLevel] };
    }
}

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
        return table is null || (classLevel == 1 && classId is "paladin" or "ranger") ? 0 : table[classLevel-1];
    }

    public static int MaximumSpellLevel(string classId, int classLevel)
    {
        if (classLevel is < 1 or > 20) throw new RuleViolation("Class level must be between 1 and 20.");
        return classId switch
        {
            "paladin" or "ranger" => classLevel == 1 ? 0 : Math.Min(5,(classLevel+3)/4),
            "warlock" => Math.Min(5,(classLevel+1)/2),
            "bard" or "cleric" or "druid" or "sorcerer" or "wizard" => Math.Min(9,(classLevel+1)/2),
            _ => 0
        };
    }

    public static PreparedSpell[] Add(PreparedSpell[] current, ClassLevel classLevel,
        SpellDefinition[] catalog, string[] spellIds, string[]? wizardSpellbook = null)
    {
        if (spellIds.Length == 0) return current;
        if (classLevel.ClassId == "wizard" && wizardSpellbook is null)
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
                !spell.ClassIds.Contains(classLevel.ClassId) ||
                classLevel.ClassId == "wizard" && !wizardSpellbook!.Contains(id))
                throw new RuleViolation("Additional spell is not eligible for this class level and spell pack.");
        }
        return [..current,..spellIds.Select(id => new PreparedSpell(classLevel.ClassId,id))];
    }

    public static PreparedSpell[] Replace(PreparedSpell[] current, ClassLevel[] classes,
        SpellDefinition[] catalog, SpellReplacement[] replacements, SpellPreparationMoment moment,
        string? gainedClassId = null, string[]? wizardSpellbook = null)
    {
        if (replacements.Length == 0) return current;
        foreach (var group in replacements.GroupBy(x => x.ClassId))
        {
            if (!classes.Any(x => x.ClassId == group.Key))
                throw new RuleViolation("Spell replacement class is not owned.");
            var maximum = moment switch
            {
                SpellPreparationMoment.LongRest when group.Key is "cleric" or "druid" => int.MaxValue,
                SpellPreparationMoment.LongRest when group.Key == "wizard" && wizardSpellbook is not null => int.MaxValue,
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
                !target.ClassIds.Contains(replacement.ClassId) ||
                replacement.ClassId == "wizard" && !(wizardSpellbook ?? []).Contains(target.Id))
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

public static class WizardSpellbook
{
    public static string[] Add(string[] current, ClassLevel wizard, SpellDefinition[] catalog,
        string[] spellIds, int maximumNew)
    {
        if (spellIds.Length == 0) return current;
        if (wizard.ClassId != "wizard" || spellIds.Length > maximumNew ||
            spellIds.Distinct(StringComparer.Ordinal).Count() != spellIds.Length ||
            spellIds.Any(current.Contains))
            throw new RuleViolation("Wizard spellbook additions are duplicate or exceed the allowance.");
        foreach (var id in spellIds)
        {
            var spell = catalog.SingleOrDefault(x => x.Id == id);
            if (spell is null || spell.Level < 1 || spell.Level > SpellPreparation.MaximumSpellLevel("wizard",wizard.Level) ||
                !spell.ClassIds.Contains("wizard"))
                throw new RuleViolation("Spell is not eligible for this wizard's spellbook.");
        }
        return [..current,..spellIds];
    }
}

public static class CantripKnowledge
{
    public static int Capacity(string classId, int classLevel)
    {
        if (classLevel is < 1 or > 20) throw new RuleViolation("Class level must be between 1 and 20.");
        var increase = classLevel >= 10 ? 2 : classLevel >= 4 ? 1 : 0;
        return classId switch
        {
            "bard" or "druid" or "warlock" => 2 + increase,
            "cleric" or "wizard" => 3 + increase,
            "sorcerer" => 4 + increase,
            _ => 0
        };
    }

    public static KnownCantrip[] Add(KnownCantrip[] current, ClassLevel classLevel,
        SpellDefinition[] catalog, string[] spellIds)
    {
        if (spellIds.Length == 0) return current;
        var capacity = Capacity(classLevel.ClassId,classLevel.Level);
        var existing = current.Where(x => x.ClassId == classLevel.ClassId).Select(x => x.SpellId).ToHashSet();
        if (capacity == 0 || existing.Count + spellIds.Length > capacity ||
            spellIds.Distinct(StringComparer.Ordinal).Count() != spellIds.Length || spellIds.Any(existing.Contains))
            throw new RuleViolation("Cantrip choices are duplicate or exceed the class allowance.");
        foreach (var id in spellIds)
        {
            var spell = catalog.SingleOrDefault(x => x.Id == id);
            if (spell is null || spell.Level != 0 || !spell.ClassIds.Contains(classLevel.ClassId))
                throw new RuleViolation("Cantrip is not on this class's list in the pinned spell pack.");
        }
        return [..current,..spellIds.Select(id => new KnownCantrip(classLevel.ClassId,id))];
    }

    public static KnownCantrip[] Replace(KnownCantrip[] current, ClassLevel[] classes,
        SpellDefinition[] catalog, SpellReplacement? replacement, string? gainedClassId, bool longRest)
    {
        if (replacement is null) return current;
        if ((!longRest && (replacement.ClassId != gainedClassId || replacement.ClassId == "wizard")) ||
            (longRest && replacement.ClassId != "wizard") ||
            !classes.Any(x => x.ClassId == replacement.ClassId) ||
            replacement.FromSpellId == replacement.ToSpellId ||
            !current.Any(x => x.ClassId == replacement.ClassId && x.SpellId == replacement.FromSpellId) ||
            current.Any(x => x.ClassId == replacement.ClassId && x.SpellId == replacement.ToSpellId))
            throw new RuleViolation("Cantrip cannot be replaced at this time.");
        var target = catalog.SingleOrDefault(x => x.Id == replacement.ToSpellId);
        if (target is null || target.Level != 0 || !target.ClassIds.Contains(replacement.ClassId))
            throw new RuleViolation("Replacement cantrip is not on this class's list in the pinned spell pack.");
        return current.Select(x => x.ClassId == replacement.ClassId && x.SpellId == replacement.FromSpellId
            ? x with { SpellId = replacement.ToSpellId } : x).ToArray();
    }
}
