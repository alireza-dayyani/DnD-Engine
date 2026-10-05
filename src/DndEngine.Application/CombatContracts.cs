using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Progression;

namespace DndEngine.Application;

public interface ICombatCatalog
{
    Task<CombatContent> GetAsync(Ruleset ruleset, CancellationToken ct);
}
public interface ICombatStore
{
    Task<CombatProfile?> GetProfileAsync(Guid characterId, CancellationToken ct);
    Task<CombatEncounter?> GetEncounterAsync(Guid id, CancellationToken ct);
    Task<bool> IsEnrolledAsync(Guid characterId, CancellationToken ct);
    Task SaveProfileAsync(Character character, CombatProfile profile, CampaignEvent entry, CancellationToken ct);
    Task SaveEncounterAsync(CombatEncounter encounter, IReadOnlyList<Character> characters,
        IReadOnlyList<CombatProfile> profiles, IReadOnlyList<CampaignEvent> events, bool create, CancellationToken ct,
        IReadOnlyDictionary<Guid, ProgressionState>? progressionUpdates = null);
}
public sealed record CreateCombat(string Name);
public sealed record AddCombatant(Guid CharacterId, CombatantKind Kind = CombatantKind.PlayerCharacter,
    ZeroHpPolicy ZeroHpPolicy = ZeroHpPolicy.DeathSaves, bool Surprised = false, string? InitiativeGroup = null);
public sealed record GrantWeapon(string DefinitionId, int Ammunition = 0);
public sealed record StartCombat(Guid[]? Order = null);
public sealed record InitiativeContext(Dictionary<Guid, Guid[]>? VisibleFearSources = null);
public sealed record CombatActor(Guid CombatantId);
public sealed record MoveCombatant(Guid CombatantId, int Distance, MovementMode Mode = MovementMode.Walk,
    bool DifficultTerrain = false, bool ApproachesFear = false);
public sealed record TakeCombatAction(Guid CombatantId, CombatAction Action);
public sealed record AttackCombatant(Guid CombatantId, WeaponAttackOptions Attack);
public sealed record CastCombatSpell(Guid CombatantId, string ClassId, string SpellId,
    SpellSlotPoolKind? Pool, int SpellLevel, SpellTargetContext[] Targets,
    bool VerbalAvailable, bool SomaticAvailable, bool MaterialAvailable, long ExpectedRevision,
    MetamagicOption? Metamagic = null, int? AreaCenterDistanceFeet = null,
    Guid? MetamagicTargetId = null);
public sealed record CombatConvertSpellSlot(Guid CombatantId, SpellSlotPoolKind Pool,
    int SpellLevel, long ExpectedRevision);
public sealed record CombatCreateSorcerySlot(Guid CombatantId, int SpellLevel, long ExpectedRevision);
public sealed record FontOfMagicResult(Guid CombatantId, int SorceryPointsAfter,
    SpellcastingSummary Spellcasting, TurnResources Resources);
public sealed record ApplyCombatCondition(Guid CombatantId, ConditionKind Kind, string Source,
    Guid? SourceCharacterId = null, ExpiryBoundary Expiry = ExpiryBoundary.Manual, long? ExpiresOnTurn = null);
public sealed record CombatSavingThrow(Guid CombatantId, CheckRequest Check);
public sealed record CombatSaveResult(Guid CombatantId, Ability Ability, int Dc, bool AutomaticFailure, bool Success, D20Roll? Roll);
public sealed record CombatView(EncounterState Encounter, Guid? CurrentCombatantId, InitiativeTie[] Ties,
    CharacterView[] Characters, CombatProfileState[] Profiles);
public sealed record CombatCommandResult<T>(Guid EncounterId, long Revision, int Round, long TurnNumber, T Result);
