using System.Security.Cryptography;
using System.Text.Json;
using DndEngine.Domain;

namespace DndEngine.Application;

public enum ConsequenceStatus { Proposed, Applied, Dismissed }
public sealed record NarrativeConsequence(Guid SourceEventId,Guid CampaignId,
    ConsequenceStatus Status,long ExpectedWorldRevision,string Cause,
    WorldChange[] Changes,long? AppliedWorldRevision,DateTimeOffset? ResolvedAtUtc)
{
    public string ReviewToken => Convert.ToHexString(SHA256.HashData(
        JsonSerializer.SerializeToUtf8Bytes(new {
            SourceEventId,CampaignId,ExpectedWorldRevision,Cause,Changes })));
}
public sealed record ConsequenceReview(CampaignEvent Source,NarrativeConsequence? Proposal);

public interface INarrativeConsequenceStore
{
    Task<NarrativeConsequence?> GetAsync(Guid sourceEventId,CancellationToken ct);
    Task<NarrativeConsequence[]> ListAsync(Guid campaignId,CancellationToken ct);
    Task ProposeAsync(NarrativeConsequence proposal,CancellationToken ct);
    Task ReplaceProposedAsync(NarrativeConsequence proposal,CancellationToken ct);
    Task ResolveAsync(NarrativeConsequence resolution,CancellationToken ct);
}

/// <summary>Reviews eligible mechanical events; interpretation remains an explicit DM decision.</summary>
public sealed class NarrativeConsequenceService(ICampaignStore campaigns,
    INarrativeConsequenceStore store,WorldService world,TimeProvider clock)
{
    private static readonly HashSet<string> EligibleTypes=["MonsterDefeated","EncounterCompleted"];

    public async Task<ConsequenceReview[]> PendingAsync(Guid campaignId,long after=0,
        int limit=20,CancellationToken ct=default)
    {
        if (after<0 || limit is < 1 or > 50) throw new RuleViolation("Invalid review cursor or limit.");
        if (await campaigns.GetCampaignAsync(campaignId,ct) is null)
            throw new NotFoundException("Campaign not found.");
        var proposals=(await store.ListAsync(campaignId,ct))
            .ToDictionary(x=>x.SourceEventId);
        var pending=new List<ConsequenceReview>(limit);
        while (pending.Count<limit)
        {
            var events=await campaigns.GetEventsAsync(campaignId,after,500,ct);
            foreach (var source in events)
            {
                if (!EligibleTypes.Contains(source.Type) ||
                    proposals.TryGetValue(source.EventId,out var proposal) &&
                    proposal.Status!=ConsequenceStatus.Proposed) continue;
                pending.Add(new(source,proposals.GetValueOrDefault(source.EventId)));
                if (pending.Count==limit) break;
            }
            if (events.Count<500) break;
            after=events[^1].Sequence;
        }
        return pending.ToArray();
    }

    public async Task<NarrativeConsequence> ProposeAsync(Guid campaignId,Guid sourceEventId,
        ApplyWorldChanges proposal,CancellationToken ct=default)
    {
        var source=await Source(campaignId,sourceEventId,ct);
        var existing=await store.GetAsync(source.EventId,ct);
        if (existing is not null && (existing.CampaignId!=campaignId ||
            existing.Status!=ConsequenceStatus.Proposed))
            throw new StateConflictException("This event already has a resolved consequence review.");
        if (proposal.Changes is null || proposal.Changes.Length is < 1 or > 50 ||
            string.IsNullOrWhiteSpace(proposal.Cause) || proposal.Cause.Length>500)
            throw new RuleViolation("A proposal needs 1–50 changes and a cause.");
        var current=await world.GetDmAsync(campaignId,ct);
        if (current.State.Revision!=proposal.ExpectedRevision)
            throw new StateConflictException("World revision changed before review.");
        var record=new NarrativeConsequence(sourceEventId,campaignId,ConsequenceStatus.Proposed,
            proposal.ExpectedRevision,proposal.Cause,proposal.Changes,null,null);
        if (existing is null) await store.ProposeAsync(record,ct);
        else await store.ReplaceProposedAsync(record,ct);
        return record;
    }

    public async Task<NarrativeConsequence> ResolveAsync(Guid campaignId,Guid sourceEventId,
        bool apply,string reviewToken,CancellationToken ct=default)
    {
        await Source(campaignId,sourceEventId,ct);
        var proposal=await store.GetAsync(sourceEventId,ct)
            ?? throw new NotFoundException("Consequence proposal not found.");
        if (proposal.CampaignId!=campaignId || proposal.Status!=ConsequenceStatus.Proposed)
            throw new StateConflictException("Consequence is already resolved.");
        if (!string.Equals(proposal.ReviewToken,reviewToken,StringComparison.Ordinal))
            throw new StateConflictException("Consequence proposal changed. Review it again before resolving.");
        long? revision=null;
        if (apply)
        {
            var result=await world.ApplyAsync(campaignId,new(proposal.ExpectedWorldRevision,
                proposal.Cause,proposal.Changes),ct);
            revision=result.State.Revision;
        }
        var resolved=proposal with { Status=apply ? ConsequenceStatus.Applied :
            ConsequenceStatus.Dismissed,AppliedWorldRevision=revision,
            ResolvedAtUtc=clock.GetUtcNow() };
        await store.ResolveAsync(resolved,ct);
        return resolved;
    }

    private async Task<CampaignEvent> Source(Guid campaignId,Guid eventId,CancellationToken ct)
    {
        if (eventId==Guid.Empty) throw new RuleViolation("Source event is required.");
        var after=0L;
        while (true)
        {
            var page=await campaigns.GetEventsAsync(campaignId,after,500,ct);
            if (page.Count==0) break;
            var found=page.FirstOrDefault(x=>x.EventId==eventId);
            if (found is not null)
            {
                if (!EligibleTypes.Contains(found.Type))
                    throw new RuleViolation("Source event is not eligible for narrative review.");
                return found;
            }
            after=page[^1].Sequence;
        }
        throw new NotFoundException("Source event not found.");
    }
}
