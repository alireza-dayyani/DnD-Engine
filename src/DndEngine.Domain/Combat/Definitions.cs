namespace DndEngine.Domain.Combat;

public enum DamageType { Acid, Bludgeoning, Cold, Fire, Force, Lightning, Necrotic, Piercing, Poison, Psychic, Radiant, Slashing, Thunder }
public enum WeaponCategory { Simple, Martial }
public enum WeaponKind { Melee, Ranged }
public enum WeaponProperty { Ammunition, Finesse, Heavy, Light, Loading, Reach, Thrown, TwoHanded, Versatile }
public enum ConditionKind { Blinded, Charmed, Deafened, Exhaustion, Frightened, Grappled, Incapacitated, Invisible, Paralyzed, Petrified, Poisoned, Prone, Restrained, Stunned, Unconscious }
public enum EncounterStatus { Created, Initiative, Active, Completed }
public enum CombatantKind { PlayerCharacter, Monster }
public enum ZeroHpPolicy { DeathSaves, Die }
public enum AttackUse { Action, LightBonus, Opportunity }
public enum AttackMode { Melee, Ranged, Thrown }
public enum Cover { None, Half, ThreeQuarters, Total }
public enum CombatAction { Dash, Disengage, Dodge }
public enum MovementMode { Walk, Crawl }
public enum ExpiryBoundary { Manual, TurnStart, TurnEnd }

public sealed record WeaponDefinition(string Id, string Name, WeaponCategory Category, WeaponKind Kind,
    string? DamageDice, int FixedDamage, DamageType DamageType, WeaponProperty[] Properties,
    string Mastery, int NormalRange, int LongRange, string? VersatileDice, string? Ammunition,
    bool MountedOneHanded, string Source)
{
    public bool Has(WeaponProperty property) => Properties.Contains(property);
    public void Validate()
    {
        Guard.Name(Id); Guard.Name(Name); Guard.Defined(Category); Guard.Defined(Kind); Guard.Defined(DamageType);
        foreach (var p in Properties) Guard.Defined(p);
        if (Properties.Distinct().Count() != Properties.Length) throw new RuleViolation("Duplicate weapon properties.");
        if (DamageDice is not null) { var dice = DiceExpression.Parse(DamageDice); if (dice.Modifier != 0) throw new RuleViolation("Weapon dice must be unmodified."); }
        if ((DamageDice is null) == (FixedDamage == 0)) throw new RuleViolation("Weapon must have dice or fixed damage.");
        Guard.Range(FixedDamage, 0, 100, "Fixed weapon damage");
        if (Has(WeaponProperty.Versatile) != (VersatileDice is not null)) throw new RuleViolation("Invalid versatile definition.");
        if (VersatileDice is not null) DiceExpression.Parse(VersatileDice);
        var ranged = Kind == WeaponKind.Ranged || Has(WeaponProperty.Thrown);
        if (ranged && (NormalRange <= 0 || LongRange < NormalRange) || !ranged && (NormalRange != 0 || LongRange != 0))
            throw new RuleViolation("Invalid weapon range.");
        if (Has(WeaponProperty.Ammunition) != (Ammunition is not null)) throw new RuleViolation("Invalid ammunition definition.");
    }
}
public sealed record ConditionDefinition(ConditionKind Id, string Source, string Summary);
public sealed record MasteryDefinition(string Id, string Source, string Summary, bool Automated = false);
public sealed record CombatContent(WeaponDefinition[] Weapons, ConditionDefinition[] Conditions, MasteryDefinition[] Masteries);

public sealed record CombatCapabilities(int Speed, string[] WeaponProficiencies, DamageType[] Resistances,
    DamageType[] Immunities, DamageType[] Vulnerabilities, ConditionKind[] ConditionImmunities,
    int AttacksPerAction = 1, int InitiativeBonus = 0, bool InitiativeAdvantage = false, bool InitiativeDisadvantage = false)
{
    public void Validate()
    {
        Guard.Range(Speed, 0, 1000, "Speed"); Guard.Range(AttacksPerAction, 1, 10, "Attacks per action");
        Guard.Range(InitiativeBonus, -100, 100, "Initiative bonus");
        if (WeaponProficiencies is null || Resistances is null || Immunities is null || Vulnerabilities is null || ConditionImmunities is null)
            throw new RuleViolation("Combat capability lists are required.");
        foreach (var type in Resistances.Concat(Immunities).Concat(Vulnerabilities)) Guard.Defined(type);
        foreach (var condition in ConditionImmunities) Guard.Defined(condition);
        if (WeaponProficiencies.Any(string.IsNullOrWhiteSpace) || WeaponProficiencies.Distinct().Count() != WeaponProficiencies.Length)
            throw new RuleViolation("Invalid weapon proficiency IDs.");
    }
}

public sealed record OwnedWeapon(Guid Id, string DefinitionId, int AmmunitionRemaining, bool Available = true);
public sealed record ActiveCondition(Guid Id, ConditionKind Kind, string Source, Guid? SourceCharacterId,
    ExpiryBoundary Expiry = ExpiryBoundary.Manual, Guid? EncounterId = null, long? ExpiresOnTurn = null);
public sealed record CombatProfileState(Guid CharacterId, CombatCapabilities Capabilities, OwnedWeapon[] Weapons, ActiveCondition[] Conditions);

public sealed class CombatProfile
{
    public CombatProfileState State { get; private set; }
    public CombatProfile(CombatProfileState state)
    {
        state.Capabilities.Validate();
        if (state.CharacterId == Guid.Empty || state.Weapons is null || state.Conditions is null) throw new RuleViolation("Invalid combat profile.");
        if (state.Weapons.Select(x => x.Id).Distinct().Count() != state.Weapons.Length || state.Conditions.Select(x => x.Id).Distinct().Count() != state.Conditions.Length)
            throw new RuleViolation("Duplicate mechanical instance IDs.");
        foreach (var w in state.Weapons) { Guard.Range(w.AmmunitionRemaining, 0, 100000, "Ammunition"); Guard.Name(w.DefinitionId); }
        foreach (var c in state.Conditions) { Guard.Defined(c.Kind); Guard.Defined(c.Expiry); Guard.Name(c.Source); }
        State = state;
    }
    public void ImportCapabilities(CombatCapabilities capabilities) { capabilities.Validate(); State = State with { Capabilities = capabilities }; }
    public OwnedWeapon Weapon(Guid id) => State.Weapons.SingleOrDefault(x => x.Id == id) ?? throw new RuleViolation("Weapon is not owned by this character.");
    public void GrantWeapon(OwnedWeapon weapon)
    {
        if (State.Weapons.Any(x => x.Id == weapon.Id)) throw new RuleViolation("Weapon instance already exists.");
        State = State with { Weapons = [.. State.Weapons, weapon] };
    }
    public void SpendWeapon(Guid id, bool ammunition, bool thrown)
    {
        var weapon = Weapon(id);
        if (!weapon.Available || ammunition && weapon.AmmunitionRemaining <= 0) throw new RuleViolation("Weapon or ammunition unavailable.");
        State = State with { Weapons = State.Weapons.Select(x => x.Id != id ? x : x with {
            Available = !thrown, AmmunitionRemaining = x.AmmunitionRemaining - (ammunition ? 1 : 0) }).ToArray() };
    }
    public void AddCondition(ActiveCondition condition, Character character)
    {
        Guard.Defined(condition.Kind); Guard.Defined(condition.Expiry); Guard.Name(condition.Source);
        character.Health.RequireAlive();
        if (State.Capabilities.ConditionImmunities.Contains(condition.Kind) || condition.Kind == ConditionKind.Poisoned && Has(ConditionKind.Petrified))
            throw new RuleViolation("Target is immune to this condition.");
        if (condition.Kind is ConditionKind.Charmed or ConditionKind.Frightened or ConditionKind.Grappled && condition.SourceCharacterId is null)
            throw new RuleViolation("This condition requires a source character.");
        State = State with { Conditions = [.. State.Conditions, condition] };
        if (condition.Kind == ConditionKind.Unconscious) character.Health.SetProne(true);
        if (State.Conditions.Count(x => x.Kind == ConditionKind.Exhaustion) >= 6) character.Health.Die();
    }
    public ActiveCondition RemoveCondition(Guid id)
    {
        var condition = State.Conditions.SingleOrDefault(x => x.Id == id) ?? throw new RuleViolation("Active condition not found.");
        State = State with { Conditions = State.Conditions.Where(x => x.Id != id).ToArray() };
        return condition;
    }
    public bool Has(ConditionKind kind) => State.Conditions.Any(x => x.Kind == kind);
    public void RemoveProne() => State = State with { Conditions = State.Conditions.Where(x => x.Kind != ConditionKind.Prone).ToArray() };
    public ActiveCondition[] Expire(Guid encounter, long turn, ExpiryBoundary boundary)
    {
        var expired = State.Conditions.Where(x => x.EncounterId == encounter && x.Expiry == boundary && x.ExpiresOnTurn <= turn).ToArray();
        foreach (var condition in expired) RemoveCondition(condition.Id);
        return expired;
    }
}
