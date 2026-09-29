using System.Text.Json;
using DndEngine.Domain;

namespace DndEngine.Application;

public sealed class CharacterService(ICampaignStore store, IRulesCatalog catalog, TimeProvider clock)
{
    public async Task<CharacterView> GetAsync(Guid id, CancellationToken ct = default) => CharacterView.From(
        await store.GetCharacterAsync(id, ct) ?? throw new NotFoundException("Character not found."));

    public async Task<CharacterView> CreateAsync(CreateCharacter request, CancellationToken ct = default)
    {
        var campaign = await store.GetCampaignAsync(request.CampaignId, ct) ?? throw new NotFoundException("Campaign not found.");
        campaign.Ruleset.RequireSupported();
        await catalog.RequireRulesetAsync(campaign.Ruleset, ct);
        if (request.Abilities is null || request.SkillProficiencies is null || request.SavingThrowProficiencies is null)
            throw new RuleViolation("Abilities and proficiency lists are required.");
        foreach (var score in request.Abilities.Values) Guard.Range(score, 1, 20, "Imported character ability score");
        foreach (var skill in request.SkillProficiencies) await catalog.GetSkillAsync(campaign.Ruleset, skill, ct);
        if (!request.Abilities.TryGetValue(Ability.Dexterity, out var dexterity)) throw new RuleViolation("Dexterity is required.");
        var c = new Character(Guid.NewGuid(), campaign.Id, request.Name, request.Level, request.Abilities,
            request.SkillProficiencies, request.SavingThrowProficiencies,
            request.ArmorClass ?? 10 + new AbilityScore(dexterity).Modifier, new(request.MaximumHp));
        var view = CharacterView.From(c);
        await store.CreateCharacterAsync(c, new(0, Guid.NewGuid(), campaign.Id, c.Id, "CharacterCreated", clock.GetUtcNow(),
            campaign.Ruleset, TimelineSerialization.SchemaVersion, 0, TimelineSerialization.Serialize(view)), ct);
        return view;
    }
}
