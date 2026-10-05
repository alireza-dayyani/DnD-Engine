using DndEngine.Domain;
using DndEngine.Domain.Progression;

namespace DndEngine.Application;

public sealed record CreateSrdCharacter(Guid CampaignId, string Name, string SpeciesId, string? SpeciesVariantId,
    string Size, string BackgroundId, string ClassId, Dictionary<Ability,int> BaseAbilities,
    Dictionary<Ability,int> BackgroundBonuses, string[] ClassSkills, string? HumanOriginFeat = null,
    string[]? StartingItemIds = null, string[]? MasteredWeaponIds = null, string? SpeciesSkill = null,
    string? FightingStyleFeat = null, string? BackgroundToolId = null, string[]? ClassTools = null,
    Proficiency[]? FeatProficiencies = null, string[]? PreparedSpellIds = null,
    string[]? KnownCantripIds = null, string[]? WizardSpellbookIds = null);
public sealed record LevelUpCharacter(string ClassId, long ExpectedRevision, string HpMethod = "Fixed",
    string? FeatId = null, Dictionary<Ability,int>? AbilityIncreases = null, string? MulticlassSkill = null,
    string? SubclassId = null, string? FightingStyleFeat = null, string? MulticlassTool = null,
    Proficiency[]? FeatProficiencies = null, SpellReplacement? SpellReplacement = null,
    string[]? AdditionalPreparedSpellIds = null, string[]? AdditionalCantripIds = null,
    SpellReplacement? CantripReplacement = null, string[]? AdditionalWizardSpellbookIds = null,
    MetamagicOption[]? AdditionalMetamagicOptions = null, string? MysticArcanumSpellId = null);
public sealed record ItemChange(string DefinitionId, long ExpectedRevision);
public sealed record EquipItem(Guid ItemId, long ExpectedRevision);
public sealed record ShortRestRequest(int[] HitDieSides, long ExpectedRevision,
    int[]? ArcaneRecoverySlotLevels = null, int SorceryPointsToRestore = 0,
    SpellReplacement? MemorizeSpell = null);
public sealed record LongRestRequest(long ExpectedRevision, string[]? MasteredWeaponIds = null,
    SpellReplacement[]? SpellReplacements = null, SpellReplacement? CantripReplacement = null);
public sealed record AdoptSpellPack(string PackVersion, long ExpectedRevision, KnownCantrip[]? KnownCantrips = null);
public sealed record SpendResource(string ResourceId, int Amount, long ExpectedRevision);
public sealed record SpendSpellSlot(SpellSlotPoolKind Pool, int SpellLevel, long ExpectedRevision);
public sealed record ConvertSpellSlot(SpellSlotPoolKind Pool, int SpellLevel, long ExpectedRevision);
public sealed record CreateSorcerySlot(int SpellLevel, long ExpectedRevision);
public sealed record CastPreparedSpell(string ClassId, string SpellId, SpellSlotPoolKind Pool, int SpellLevel,
    long ExpectedRevision, bool ComponentsAvailable);
public sealed record SpellCastResult(CharacterSheet Sheet, string ClassId, string SpellId, SpellSlotPoolKind Pool,
    int SpellLevel, int[] Rolls, int AbilityModifier, int HitPointsRegained);
public sealed record RestResult(CharacterSheet Sheet, int[] HitDieRolls, int HitPointsRegained,
    ResourceState[] ResourceChanges, string[] OtherChanges);
public sealed record CharacterChoices(CharacterRules Rules);

public interface ICharacterRulesCatalog
{
    Task<CharacterRules> GetAsync(Ruleset ruleset, CancellationToken ct);
}
public interface ISpellCatalog
{
    Task<SpellDefinition[]> GetAsync(Ruleset ruleset, string packVersion, CancellationToken ct);
}
public interface IProgressionStore
{
    Task<ProgressionState?> GetAsync(Guid characterId, CancellationToken ct);
    Task CreateAsync(Character character, ProgressionState state, DndEngine.Domain.Combat.CombatProfile profile,
        CampaignEvent entry, CancellationToken ct);
    Task SaveAsync(Character character, ProgressionState state, DndEngine.Domain.Combat.CombatProfile profile,
        CampaignEvent entry, CancellationToken ct);
    Task<bool> IsEnrolledAsync(Guid characterId, CancellationToken ct);
}
