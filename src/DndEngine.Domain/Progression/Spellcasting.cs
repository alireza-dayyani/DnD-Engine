using DndEngine.Domain.Combat;

namespace DndEngine.Domain.Progression;

public sealed record SpellSlotPool(int SpellLevel, int Maximum, int Current, string Source, RecoveryKind Recovery);
public sealed record ClassSpellcasting(string ClassId, int ClassLevel, Ability Ability, int AbilityModifier,
    int SpellAttackBonus, int SpellSaveDc);
public sealed record SpellcastingSummary(ClassSpellcasting[] Classes, SpellSlotPool[] SharedSlots,
    SpellSlotPool? PactMagicSlots);

/// <summary>SRD 5.2.1 slot progression. Spell descriptions and cast effects are resolved by later Phase 4 work.</summary>
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
        if (casters.Length == 0) return null;
        var classes = casters.Select(x =>
        {
            var ability = AbilityFor(x.ClassId)!.Value;
            var modifier = character.AbilityModifier(ability);
            return new ClassSpellcasting(x.ClassId,x.Level,ability,modifier,character.Level.ProficiencyBonus+modifier,8+character.Level.ProficiencyBonus+modifier);
        }).ToArray();
        var slotClasses = casters.Where(x => x.ClassId != "warlock").ToArray();
        var casterLevel = slotClasses.Sum(x => x.ClassId is "paladin" or "ranger" ? (x.Level+1)/2 : x.Level);
        var shared = (casterLevel == 0 ? [] : FullCasterSlots[Math.Clamp(casterLevel,0,20)])
            .Select((maximum,index) => (maximum,index)).Where(x => x.maximum > 0)
            .Select(x => new SpellSlotPool(x.index+1,x.maximum,x.maximum,"multiclass-spellcasting",RecoveryKind.LongRest)).ToArray();
        var warlock = state.Classes.SingleOrDefault(x => x.ClassId == "warlock");
        SpellSlotPool? pact = null;
        if (warlock is not null)
        {
            var (count,level) = PactSlots(warlock.Level);
            if (count > 0) pact = new(level,count,count,"warlock-pact-magic",RecoveryKind.ShortRest);
        }
        return new(classes,shared,pact);
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
