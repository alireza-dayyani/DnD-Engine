using System.Text.Json;
using DndEngine.Domain;

namespace DndEngine.Application;

public sealed class CampaignService(ICampaignStore store, IRulesCatalog catalog, TimeProvider clock)
{
    public async Task<CampaignView> CreateAsync(CreateCampaign request, CancellationToken ct = default)
    {
        var ruleset = new Ruleset(request.RulesetId, request.SrdVersion);
        ruleset.RequireSupported();
        await catalog.RequireRulesetAsync(ruleset, ct);
        var campaign = new Campaign(Guid.NewGuid(), request.Name, ruleset);
        var view = new CampaignView(campaign.Id, campaign.Name, ruleset);
        await store.CreateCampaignAsync(campaign, new(0, Guid.NewGuid(), campaign.Id, null, "CampaignCreated",
            clock.GetUtcNow(), ruleset, TimelineSerialization.SchemaVersion, null, TimelineSerialization.Serialize(view)), ct);
        return view;
    }
    public async Task<CampaignView> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await store.GetCampaignAsync(id, ct) ?? throw new NotFoundException("Campaign not found.");
        return new(c.Id, c.Name, c.Ruleset);
    }
    public async Task<IReadOnlyList<CampaignEvent>> EventsAsync(Guid id, long after = 0, int limit = 100, CancellationToken ct = default)
    {
        if (after < 0) throw new RuleViolation("Event cursor must be nonnegative.");
        Guard.Range(limit, 1, 500, "Event limit");
        await GetAsync(id, ct);
        return await store.GetEventsAsync(id, after, limit, ct);
    }
}
