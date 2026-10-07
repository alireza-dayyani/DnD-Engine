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
        var view = new CampaignView(campaign.Id, campaign.Name, ruleset,campaign.GameSeconds,campaign.Revision);
        await store.CreateCampaignAsync(campaign, new(0, Guid.NewGuid(), campaign.Id, null, "CampaignCreated",
            clock.GetUtcNow(), ruleset, TimelineSerialization.SchemaVersion, null, TimelineSerialization.Serialize(view)), ct);
        return view;
    }
    public async Task<CampaignView> GetAsync(Guid id, CancellationToken ct = default)
    {
        var c = await store.GetCampaignAsync(id, ct) ?? throw new NotFoundException("Campaign not found.");
        return new(c.Id, c.Name, c.Ruleset,c.GameSeconds,c.Revision);
    }
    public async Task<CampaignView> AdvanceTimeAsync(Guid id, AdvanceCampaignTime request, CancellationToken ct = default)
    {
        var before = await store.GetCampaignAsync(id,ct) ?? throw new NotFoundException("Campaign not found.");
        if (before.Revision != request.ExpectedRevision)
            throw new StateConflictException("Campaign time changed. Reload before advancing it.");
        if (await store.HasUnfinishedEncounterAsync(id,ct))
            throw new RuleViolation("Campaign time cannot advance during an unfinished encounter.");
        var after = before.AdvanceTime(request.Seconds);
        var view = new CampaignView(after.Id,after.Name,after.Ruleset,after.GameSeconds,after.Revision);
        await store.SaveCampaignTimeAsync(before,after,new(0,Guid.NewGuid(),id,null,"CampaignTimeAdvanced",
            clock.GetUtcNow(),after.Ruleset,TimelineSerialization.SchemaVersion,null,
            TimelineSerialization.Serialize(new { request.Seconds,after.GameSeconds,after.Revision })),ct);
        return view;
    }
    public async Task<IReadOnlyList<CampaignEvent>> EventsAsync(Guid id, long after = 0, int limit = 100, CancellationToken ct = default)
    {
        if (after < 0) throw new RuleViolation("Event cursor must be nonnegative.");
        Guard.Range(limit, 1, 500, "Event limit");
        await GetAsync(id, ct);
        return await store.GetEventsAsync(id, after, limit, ct);
    }
}
