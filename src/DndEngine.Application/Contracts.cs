using System.Text.Json;
using DndEngine.Domain;

namespace DndEngine.Application;

public sealed class NotFoundException(string message) : Exception(message);
public sealed class StateConflictException(string message) : Exception(message);

public sealed record CreateCampaign(string Name, string RulesetId = "dnd-5.5", string SrdVersion = "5.2.1");
public sealed record CampaignView(Guid Id, string Name, Ruleset Ruleset);
public sealed record CreateCharacter(Guid CampaignId, string Name, int Level, Dictionary<Ability, int> Abilities,
    string[] SkillProficiencies, Ability[] SavingThrowProficiencies, int MaximumHp, int? ArmorClass = null);
public sealed record CharacterView(Guid Id, Guid CampaignId, string Name, int Level, int ProficiencyBonus,
    IReadOnlyDictionary<Ability, int> Abilities, IReadOnlyDictionary<Ability, int> AbilityModifiers,
    IReadOnlyList<string> SkillProficiencies, IReadOnlyList<Ability> SavingThrowProficiencies,
    int ArmorClass, HealthState Health, long Revision)
{
    public static CharacterView From(Character c, long? revision = null) => new(c.Id, c.CampaignId, c.Name,
        c.Level.Value, c.Level.ProficiencyBonus, c.Abilities.ToDictionary(x => x.Key, x => x.Value.Value),
        c.Abilities.ToDictionary(x => x.Key, x => x.Value.Modifier), c.SkillProficiencies.Order().ToArray(),
        c.SavingThrowProficiencies.Order().ToArray(), c.ArmorClass, c.Health.State, revision ?? c.Revision);
}
public sealed record CheckRequest(Ability Ability, int Dc, bool Advantage = false, bool Disadvantage = false,
    int OtherModifier = 0, bool VoluntaryFailure = false)
{
    public CheckOptions Options => new(Dc, Advantage, Disadvantage, OtherModifier, VoluntaryFailure);
}
public sealed record SkillCheckRequest(string SkillId, int Dc, bool Advantage = false, bool Disadvantage = false,
    int OtherModifier = 0, Ability? AbilityOverride = null);
public sealed record DamageRequest(int Amount, bool Critical = false);
public sealed record HealingRequest(int Amount);
public sealed record TemporaryHpRequest(int Amount, bool ReplaceExisting);
public sealed record HealthResult(Guid CharacterId, HealthChange Change, long Revision);
public sealed record CampaignEvent(long Sequence, Guid EventId, Guid CampaignId, Guid? CharacterId,
    string Type, DateTimeOffset OccurredAtUtc, Ruleset Ruleset, int SchemaVersion, long? CharacterRevision, JsonElement Data);

public interface IRulesCatalog
{
    Task RequireRulesetAsync(Ruleset ruleset, CancellationToken ct);
    Task<SkillDefinition> GetSkillAsync(Ruleset ruleset, string id, CancellationToken ct);
}

/// <summary>Each write commits state and the supplied audit event atomically.</summary>
public interface ICampaignStore
{
    Task<Campaign?> GetCampaignAsync(Guid id, CancellationToken ct);
    Task<Character?> GetCharacterAsync(Guid id, CancellationToken ct);
    Task CreateCampaignAsync(Campaign campaign, CampaignEvent entry, CancellationToken ct);
    Task CreateCharacterAsync(Character character, CampaignEvent entry, CancellationToken ct);
    Task SaveCharacterAsync(Character character, CampaignEvent entry, CancellationToken ct);
    Task<IReadOnlyList<CampaignEvent>> GetEventsAsync(Guid campaignId, long after, int limit, CancellationToken ct);
}
