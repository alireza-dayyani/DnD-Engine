using DndEngine.Domain;
using DndEngine.Domain.Progression;

namespace DndEngine.Application;

public sealed record CreateSrdCharacter(Guid CampaignId, string Name, string SpeciesId, string? SpeciesVariantId,
    string Size, string BackgroundId, string ClassId, Dictionary<Ability,int> BaseAbilities,
    Dictionary<Ability,int> BackgroundBonuses, string[] ClassSkills, string? HumanOriginFeat = null,
    string[]? StartingItemIds = null, string[]? MasteredWeaponIds = null, string? SpeciesSkill = null,
    string? FightingStyleFeat = null, string? BackgroundToolId = null, string[]? ClassTools = null,
    Proficiency[]? FeatProficiencies = null);
public sealed record LevelUpCharacter(string ClassId, long ExpectedRevision, string HpMethod = "Fixed",
    string? FeatId = null, Dictionary<Ability,int>? AbilityIncreases = null, string? MulticlassSkill = null,
    string? SubclassId = null, string? FightingStyleFeat = null, string? MulticlassTool = null,
    Proficiency[]? FeatProficiencies = null);
public sealed record ItemChange(string DefinitionId, long ExpectedRevision);
public sealed record EquipItem(Guid ItemId, long ExpectedRevision);
public sealed record ShortRestRequest(int[] HitDieSides, long ExpectedRevision);
public sealed record LongRestRequest(long ExpectedRevision, string[]? MasteredWeaponIds = null);
public sealed record SpendResource(string ResourceId, int Amount, long ExpectedRevision);
public sealed record RestResult(CharacterSheet Sheet, int[] HitDieRolls, int HitPointsRegained,
    ResourceState[] ResourceChanges, string[] OtherChanges);
public sealed record CharacterChoices(CharacterRules Rules);

public interface ICharacterRulesCatalog
{
    Task<CharacterRules> GetAsync(Ruleset ruleset, CancellationToken ct);
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
