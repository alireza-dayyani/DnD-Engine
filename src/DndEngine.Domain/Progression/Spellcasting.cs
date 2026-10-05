using DndEngine.Domain.Combat;

namespace DndEngine.Domain.Progression;

public sealed record SpellSlotPool(int SpellLevel, int Maximum, int Current, string Source, RecoveryKind Recovery);
public sealed record ClassSpellcasting(string ClassId, int ClassLevel, Ability Ability, int AbilityModifier,
    int SpellAttackBonus, int SpellSaveDc, int PreparedCount, int PreparedMaximum,
    int CantripCount = 0, int CantripMaximum = 0);
public sealed record SpellcastingSummary(ClassSpellcasting[] Classes, SpellSlotPool[] SharedSlots,
    SpellSlotPool? PactMagicSlots);

/// <summary>SRD 5.2.1 slot progression and expenditure shared by manual slot use and the initial cast path.</summary>
public static class SpellSlotCalculator
{
    private static readonly int[][] FullCasterSlots =
    [
        [0,0,0,0,0,0,0,0,0],
        [2,0,0,0,0,0,0,0,0], [3,0,0,0,0,0,0,0,0],
        [4,2,0,0,0,0,0,0,0], [4,3,0,0,0,0,0,0,0],
        [4,3,2,0,0,0,0,0,0], [4,3,3,0,0,0,0,0,0],
        [4,3,3,1,0,0,0,0,0], [4,3,3,2,0,0,0,0,0],
        [4,3,3,3,1,0,0,0,0], [4,3,3,3,2,0,0,0,0],
        [4,3,3,3,2,1,0,0,0], [4,3,3,3,2,1,0,0,0],
        [4,3,3,3,2,1,1,0,0], [4,3,3,3,2,1,1,0,0],
        [4,3,3,3,2,1,1,1,0], [4,3,3,3,2,1,1,1,0],
        [4,3,3,3,2,1,1,1,1], [4,3,3,3,3,1,1,1,1],
        [4,3,3,3,3,2,1,1,1], [4,3,3,3,3,2,2,1,1]
    ];

    public static SpellcastingSummary? Derive(Character character, ProgressionState state)
    {
        var casters = state.Classes.Where(x => AbilityFor(x.ClassId) is not null).ToArray();
        var spent = state.SpellSlots?.SharedSpentByLevel ?? new int[9];
        if (spent.Length != 9) throw new RuleViolation("Shared spell slot expenditure must have nine levels.");
        var pactSpent = state.SpellSlots?.PactSpent ?? 0;
        if (casters.Length == 0 && spent.All(x => x == 0) && pactSpent == 0) return null;
        var classes = casters.Select(x =>
        {
            var ability = AbilityFor(x.ClassId)!.Value;
            var modifier = character.AbilityModifier(ability);
            return new ClassSpellcasting(x.ClassId,x.Level,ability,modifier,
                character.Level.ProficiencyBonus+modifier,8+character.Level.ProficiencyBonus+modifier,
                (state.PreparedSpells ?? []).Count(s => s.ClassId == x.ClassId),
                SpellPreparation.Capacity(x.ClassId,x.Level),
                (state.KnownCantrips ?? []).Count(s => s.ClassId == x.ClassId),
                CantripKnowledge.Capacity(x.ClassId,x.Level));
        }).ToArray();
        var slotClasses = casters.Where(x => x.ClassId != "warlock").ToArray();
        var casterLevel = slotClasses.Sum(x => x.ClassId is "paladin" or "ranger" ? (x.Level+1)/2 : x.Level);
        var maxima = casterLevel == 0 ? new int[9] : FullCasterSlots[Math.Clamp(casterLevel,0,20)];
        if (spent.Where((value,index) => value < 0 || value > maxima[index]).Any())
            throw new RuleViolation("Shared spell slot expenditure exceeds available slots.");
        var shared = maxima
            .Select((maximum,index) => (maximum,index)).Where(x => x.maximum > 0)
            .Select(x => new SpellSlotPool(x.index+1,x.maximum,x.maximum-spent[x.index],"multiclass-spellcasting",RecoveryKind.LongRest)).ToArray();
        var warlock = state.Classes.SingleOrDefault(x => x.ClassId == "warlock");
        SpellSlotPool? pact = null;
        if (warlock is not null)
        {
            var (count,level) = PactSlots(warlock.Level);
            if (count > 0) pact = new(level,count,count-pactSpent,"warlock-pact-magic",RecoveryKind.ShortRest);
        }
        if (pactSpent < 0 || pactSpent > (pact?.Maximum ?? 0))
            throw new RuleViolation("Pact Magic expenditure exceeds available slots.");
        return new(classes,shared,pact);
    }

    public static (SpellSlotUsage Usage, int RemainingBefore) Spend(Character character, ProgressionState state,
        SpellSlotPoolKind poolKind, int spellLevel)
    {
        Guard.Defined(poolKind);
        Guard.Range(spellLevel,1,9,"Spell level");
        var summary = Derive(character,state) ?? throw new RuleViolation("Character has no spell slots.");
        var shared = (int[])(state.SpellSlots?.SharedSpentByLevel.Clone() ?? new int[9]);
        var pactSpent = state.SpellSlots?.PactSpent ?? 0;
        if (poolKind == SpellSlotPoolKind.Shared)
        {
            var slot = summary.SharedSlots.SingleOrDefault(x => x.SpellLevel == spellLevel)
                ?? throw new RuleViolation("Shared spell slot level is unavailable.");
            if (slot.Current == 0) throw new RuleViolation("No shared spell slot remains at that level.");
            shared[spellLevel-1]++;
            return (new(shared,pactSpent),slot.Current);
        }
        var pact = summary.PactMagicSlots;
        if (pact is null || pact.SpellLevel != spellLevel)
            throw new RuleViolation("Pact Magic slot level is unavailable.");
        if (pact.Current == 0) throw new RuleViolation("No Pact Magic slot remains.");
        return (new(shared,pactSpent+1),pact.Current);
    }

    private static Ability? AbilityFor(string id) => id switch
    {
        "bard" or "sorcerer" or "warlock" => Ability.Charisma,
        "cleric" or "druid" or "ranger" => Ability.Wisdom,
        "paladin" => Ability.Charisma,
        "wizard" => Ability.Intelligence,
        _ => null
    };
    private static (int Count,int Level) PactSlots(int level) => level switch
    {
        >= 17 => (4,5), >= 11 => (3,5), >= 9 => (2,5), >= 7 => (2,4),
        >= 5 => (2,3), >= 3 => (2,2), 2 => (2,1), 1 => (1,1), _ => (0,0)
    };
}
