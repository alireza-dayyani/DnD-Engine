using System.Text.Json;
using DndEngine.Domain;
using DndEngine.Domain.Progression;

namespace DndEngine.Application;

public sealed class MechanicsService(ICampaignStore store, IRulesCatalog catalog, IDiceRoller dice, TimeProvider clock,
    IProgressionStore? progressions = null, ICharacterRulesCatalog? characterRules = null,
    ICombatStore? combatStore = null)
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
        if (progressions is not null && characterRules is not null)
        {
            var progression = await progressions.GetAsync(id,ct);
            if (progression is not null)
            {
                var rules = await characterRules.GetAsync(campaign.Ruleset,ct);
                var equipped = progression.Inventory.Where(x => x.Equipped)
                    .Select(x => rules.Items.SingleOrDefault(i => i.Id == x.DefinitionId))
                    .Where(x => x is not null).ToArray();
                if (kind == CheckKind.Skill && skillId == "stealth" && (ability ?? skill!.Ability) == Ability.Dexterity &&
                    equipped.Any(x => x!.Kind == ItemKind.Armor && x.StealthDisadvantage))
                    options = options with { Disadvantage = true };
                if ((ability ?? skill?.Ability) is Ability.Strength or Ability.Dexterity &&
                    equipped.Any(x => x!.Kind == ItemKind.Armor &&
                        !HasArmorTraining(progression,rules,x!)))
                    options = options with { Disadvantage = true };
            }
        }
        var result = new CheckResolver(dice).Resolve(character, kind, ability ?? skill!.Ability, options, skill);
        await SaveAsync(character, campaign, kind == CheckKind.SavingThrow ? "SavingThrowMade" : "AbilityCheckMade", result, ct);
        return result;
    }
    private static bool HasArmorTraining(ProgressionState state, CharacterRules rules, ItemDefinition item)
    {
        var first = rules.Classes.Single(x => x.Id == state.Classes[0].ClassId);
        var kind = item.ArmorKind!.Value.ToString().ToLowerInvariant();
        if (first.ArmorTraining.Contains(kind)) return true;
        return state.Classes.Skip(1).Any(x => rules.Classes.Single(c => c.Id == x.ClassId).MulticlassArmorTraining.Contains(kind));
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
        if (combatStore is not null && await combatStore.IsEnrolledAsync(id,ct))
            throw new StateConflictException("Use encounter commands while the character is enrolled in combat.");
        var campaign = await store.GetCampaignAsync(character.CampaignId, ct) ?? throw new NotFoundException("Campaign not found.");
        campaign.Ruleset.RequireSupported();
        await catalog.RequireRulesetAsync(campaign.Ruleset, ct);
        return (character, campaign);
    }
    private Task SaveAsync<T>(Character character, Campaign campaign, string eventType, T result, CancellationToken ct) =>
        store.SaveCharacterAsync(character, new(0, Guid.NewGuid(), campaign.Id, character.Id, eventType,
            clock.GetUtcNow(), campaign.Ruleset, TimelineSerialization.SchemaVersion, character.Revision + 1, TimelineSerialization.Serialize(result)), ct);
}
