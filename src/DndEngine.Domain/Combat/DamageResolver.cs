namespace DndEngine.Domain.Combat;

public sealed record DamageResolution(DamageType Type, int RawDamage, int FlatAdjustment, int AdjustedDamage,
    bool Immune, bool Resistant, bool Vulnerable, int AfterResistance, int AppliedDamage);

public static class DamageResolver
{
    public static DamageResolution Resolve(int raw, int adjustment, DamageType type, bool immune, bool resistant, bool vulnerable)
    {
        Guard.Defined(type); Guard.Range(raw, 0, 100000, "Raw damage"); Guard.Range(adjustment, -100000, 100000, "Damage adjustment");
        var adjusted = Math.Max(0, raw + adjustment);
        var resisted = immune ? 0 : resistant ? adjusted / 2 : adjusted;
        return new(type, raw, adjustment, adjusted, immune, resistant, vulnerable, resisted, vulnerable ? resisted * 2 : resisted);
    }
}
