using System.Text.Json;
using DndEngine.Domain;

namespace DndEngine.Application;

public sealed class MechanicsService(ICampaignStore store, IRulesCatalog catalog, IDiceRoller dice, TimeProvider clock)
{
    public Task<CheckResult> AbilityCheckAsync(Guid id, CheckRequest request, CancellationToken ct = default) =>
        CheckAsync(id, CheckKind.Ability, request.Ability, request.Options, null, ct);
    public Task<CheckResult> SavingThrowAsync(Guid id, CheckRequest request, CancellationToken ct = default) =>
        CheckAsync(id, CheckKind.SavingThrow, request.Ability, request.Options, null, ct);
    public Task<CheckResult> SkillCheckAsync(Guid id, SkillCheckRequest request, CancellationToken ct = default) =>
        CheckAsync(id, CheckKind.Skill, request.AbilityOverride,
            new(request.Dc, request.Advantage, request.Disadvantage, request.OtherModifier), request.SkillId, ct);

    private async Task<CheckResult> CheckAsync(Guid id, CheckKind kind, Ability? ability, CheckOptions options, string? skillId, CancellationToken ct)
    {
        var (character, campaign) = await LoadAsync(id, ct);
        var skill = kind == CheckKind.Skill ? await catalog.GetSkillAsync(campaign.Ruleset, skillId!, ct) : null;
        var result = new CheckResolver(dice).Resolve(character, kind, ability ?? skill!.Ability, options, skill);
        await SaveAsync(character, campaign, kind == CheckKind.SavingThrow ? "SavingThrowMade" : "AbilityCheckMade", result, ct);
        return result;
    }
    public Task<HealthResult> DamageAsync(Guid id, DamageRequest request, CancellationToken ct = default) =>
        ChangeHealthAsync(id, "CharacterDamaged", hp => hp.Damage(request.Amount, request.Critical), ct);
    public Task<HealthResult> HealAsync(Guid id, HealingRequest request, CancellationToken ct = default) =>
        ChangeHealthAsync(id, "CharacterHealed", hp => hp.Heal(request.Amount), ct);
    public Task<HealthResult> TemporaryHpAsync(Guid id, TemporaryHpRequest request, CancellationToken ct = default) =>
        ChangeHealthAsync(id, "TemporaryHitPointsGranted", hp => hp.GrantTemporary(request.Amount, request.ReplaceExisting), ct);
    public Task<HealthResult> DeathSaveAsync(Guid id, CancellationToken ct = default) =>
        ChangeHealthAsync(id, "DeathSavingThrowMade", hp => hp.DeathSave(dice), ct);
    private async Task<HealthResult> ChangeHealthAsync(Guid id, string eventType, Func<HitPoints, HealthChange> change, CancellationToken ct)
    {
        var (character, campaign) = await LoadAsync(id, ct);
        var result = new HealthResult(id, change(character.Health), character.Revision + 1);
        await SaveAsync(character, campaign, eventType, result, ct);
        return result;
    }
    private async Task<(Character, Campaign)> LoadAsync(Guid id, CancellationToken ct)
    {
        var character = await store.GetCharacterAsync(id, ct) ?? throw new NotFoundException("Character not found.");
        var campaign = await store.GetCampaignAsync(character.CampaignId, ct) ?? throw new NotFoundException("Campaign not found.");
        campaign.Ruleset.RequireSupported();
        await catalog.RequireRulesetAsync(campaign.Ruleset, ct);
        return (character, campaign);
    }
    private Task SaveAsync<T>(Character character, Campaign campaign, string eventType, T result, CancellationToken ct) =>
        store.SaveCharacterAsync(character, new(0, Guid.NewGuid(), campaign.Id, character.Id, eventType,
            clock.GetUtcNow(), campaign.Ruleset, TimelineSerialization.SchemaVersion, character.Revision + 1, TimelineSerialization.Serialize(result)), ct);
}
