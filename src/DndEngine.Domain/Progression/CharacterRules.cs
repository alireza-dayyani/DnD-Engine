using DndEngine.Domain.Combat;

namespace DndEngine.Domain.Progression;

public enum ProficiencyKind { Skill, Save, Weapon, Armor, Tool }
public enum EffectKind { Proficiency, SpeedBonus, HitPointsPerLevel, Resistance, InitiativeBonus, InitiativeAdvantage, ExtraAttack, ArmorClassBonus, Resource, WeaponMasterySlots, UnarmoredDefense, Darkvision, ResourceRecovery }
public enum ItemKind { Weapon, Armor, Shield, Gear }
public enum ArmorKind { Light, Medium, Heavy, Shield }
public enum RecoveryKind { LongRest, ShortRest, OneOnShortRest }
public enum SpellSlotPoolKind { Shared, PactMagic }
public enum FeatKind { Origin, General, FightingStyle, EpicBoon }

public sealed record FeatureEffect(EffectKind Kind, string Target = "", int Amount = 0, int[]? Values = null,
    RecoveryKind Recovery = RecoveryKind.LongRest, Ability? ScalingAbility = null);
public sealed record FeatureDefinition(string Id, string Name, int Level, FeatureEffect[] Effects, bool RequiresChoice = false, bool Deferred = false);
public sealed record SpeciesDefinition(string Id, string Name, string[] Sizes, int Speed, FeatureDefinition[] Features, VariantDefinition[] Variants,
    string[]? SkillChoices = null, int SkillChoiceCount = 0);
public sealed record VariantDefinition(string Id, string Name, FeatureDefinition[] Features);
public sealed record BackgroundDefinition(string Id, string Name, Ability[] AbilityChoices, string[] Skills, string Tool, string OriginFeat, string[] Equipment, FeatureDefinition[] Features,
    string[]? ToolChoices = null);
public sealed record SubclassDefinition(string Id, string Name, FeatureDefinition[] Features);
public sealed record ClassDefinition(string Id, string Name, int HitDie, Ability[] PrimaryAbilities, Ability[] Saves,
    string[] SkillChoices, int SkillChoiceCount, string[] WeaponTraining, string[] ArmorTraining,
    string[] MulticlassWeaponTraining, string[] MulticlassArmorTraining, string[] StartingEquipment,
    FeatureDefinition[] Features, bool AnyPrimaryAbility = false, int MulticlassSkillCount = 0, string[]? MulticlassTools = null,
    SubclassDefinition[]? Subclasses = null, string[]? ToolTraining = null, int ToolChoiceCount = 0,
    string[]? ToolChoiceOptions = null, int MulticlassToolChoiceCount = 0);
public sealed record FeatDefinition(string Id, string Name, FeatKind Kind, int MinimumLevel, Ability? AbilityPrerequisite,
    int MinimumAbility, bool Repeatable, FeatureEffect[] Effects, bool Deferred = false,
    Ability[]? AnyAbilityPrerequisites = null, Ability[]? AbilityBoostOptions = null,
    int AbilityBoostAmount = 0, int AbilityBoostCap = 20, int ProficiencyChoiceCount = 0);
public sealed record ItemDefinition(string Id, string Name, ItemKind Kind, ArmorKind? ArmorKind = null,
    int BaseAc = 0, int MaxDexterityBonus = 99, int StrengthRequired = 0, bool StealthDisadvantage = false);
public sealed record CharacterRules(SpeciesDefinition[] Species, BackgroundDefinition[] Backgrounds,
    ClassDefinition[] Classes, FeatDefinition[] Feats, ItemDefinition[] Items, string[]? ToolIds = null);

public sealed record Proficiency(ProficiencyKind Kind, string Id, string Source);
public sealed record FeatureGrant(string Id, string Name, string Source, bool Deferred);
public sealed record ResourceState(string Id, string Source, int Current, int Maximum, RecoveryKind Recovery);
public sealed record HitDiePool(int Sides, int Total, int Available);
public sealed record InventoryItem(Guid Id, string DefinitionId, bool Equipped = false);
public sealed record ClassLevel(string ClassId, int Level);
public sealed record SpellSlotUsage(int[] SharedSpentByLevel, int PactSpent);

public sealed record ProgressionState(string SpeciesId, string? SpeciesVariantId, string Size, string BackgroundId,
    Dictionary<Ability, int> BaseAbilities, Dictionary<Ability, int> BackgroundBonuses, Dictionary<Ability, int> AdvancementBonuses,
    ClassLevel[] Classes, string[] ClassSkills, string[] FeatIds, int MaximumHp,
    HitDiePool[] HitDice, ResourceState[] Resources, InventoryItem[] Inventory, string[] MasteredWeaponIds,
    string? SpeciesSkill = null, DateTimeOffset? LastLongRestAtUtc = null, Proficiency[]? ExtraProficiencies = null,
    Dictionary<string,string>? SubclassIds = null, string? BackgroundToolId = null, string[]? ClassTools = null,
    SpellSlotUsage? SpellSlots = null, PreparedSpell[]? PreparedSpells = null, string? SpellPackVersion = null,
    KnownCantrip[]? KnownCantrips = null, string[]? WizardSpellbookIds = null,
    MetamagicOption[]? MetamagicOptions = null, int SorceryPointsSpent = 0,
    Dictionary<int,string>? MysticArcanumChoices = null, int[]? MysticArcanumSpentLevels = null);

public sealed record StatisticPart(string Source, int Value);
public sealed record DerivedStatistic(int Total, StatisticPart[] Parts);
public sealed record DerivedSkill(string Id, Ability Ability, DerivedStatistic Modifier);
public sealed record DerivedSave(Ability Ability, DerivedStatistic Modifier);
public sealed record CharacterSheet(Guid Id, Guid CampaignId, string Name, long Revision, string RulesetVersion,
    string SpeciesId, string? SpeciesVariantId, string Size, string BackgroundId, ClassLevel[] Classes,
    int Level, int ProficiencyBonus, IReadOnlyDictionary<Ability, int> Abilities,
    IReadOnlyDictionary<Ability, int> AbilityModifiers, DerivedSave[] Saves, DerivedSkill[] Skills,
    int MaximumHp, int CurrentHp, int TemporaryHp, HitDiePool[] HitDice, DerivedStatistic ArmorClass,
    int Speed, Proficiency[] Proficiencies, FeatureGrant[] Features, string[] Feats,
    ResourceState[] Resources, InventoryItem[] Inventory, string[] WeaponMasteries,
    CombatCapabilities CombatCapabilities, IReadOnlyDictionary<string,string>? SubclassIds = null,
    IReadOnlyDictionary<Ability,DerivedStatistic>? AbilityBreakdowns = null, DerivedStatistic? SpeedBreakdown = null,
    bool UntrainedArmorPenalty = false, bool SpellcastingBlockedByArmor = false, int DarkvisionFeet = 0,
    SpellcastingSummary? Spellcasting = null, PreparedSpell[]? PreparedSpells = null,
    string SpellPackVersion = SpellPackVersions.Initial, KnownCantrip[]? KnownCantrips = null,
    string[]? WizardSpellbookIds = null, MetamagicOption[]? MetamagicOptions = null,
    int SorceryPointsCurrent = 0, int SorceryPointsMaximum = 0,
    PreparedSpell[]? AlwaysPreparedSpells = null,
    IReadOnlyDictionary<int,string>? MysticArcanumChoices = null, int[]? MysticArcanumSpentLevels = null);
