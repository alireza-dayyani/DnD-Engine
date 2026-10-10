using System.Text.Json;
using DndEngine.Application;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class NarrativeConsequenceRow
{
    public Guid SourceEventId { get; set; }
    public Guid CampaignId { get; set; }
    public string Status { get; set; } = "";
    public long ExpectedWorldRevision { get; set; }
    public string Cause { get; set; } = "";
    public string ChangesJson { get; set; } = "";
    public long? AppliedWorldRevision { get; set; }
    public DateTimeOffset? ResolvedAtUtc { get; set; }
}

public sealed class SqliteNarrativeConsequenceStore(CampaignDbContext db)
    : INarrativeConsequenceStore
{
    public async Task<NarrativeConsequence?> GetAsync(Guid sourceEventId,CancellationToken ct)
    {
        var row=await db.NarrativeConsequences.AsNoTracking().SingleOrDefaultAsync(
            x=>x.SourceEventId==sourceEventId,ct);
        return row is null ? null : Project(row);
    }

    public async Task<NarrativeConsequence[]> ListAsync(Guid campaignId,CancellationToken ct) =>
        (await db.NarrativeConsequences.AsNoTracking()
            .Where(x=>x.CampaignId==campaignId).ToArrayAsync(ct)).Select(Project).ToArray();

    public async Task ProposeAsync(NarrativeConsequence proposal,CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Consequence proposal requires a command transaction.");
        db.NarrativeConsequences.Add(ToRow(proposal));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task ReplaceProposedAsync(NarrativeConsequence proposal,CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Consequence revision requires a command transaction.");
        var row=await db.NarrativeConsequences.SingleAsync(x=>
            x.SourceEventId==proposal.SourceEventId,ct);
        if (row.CampaignId!=proposal.CampaignId || row.Status!="Proposed")
            throw new StateConflictException("Consequence is already resolved.");
        row.ExpectedWorldRevision=proposal.ExpectedWorldRevision;
        row.Cause=proposal.Cause;
        row.ChangesJson=JsonSerializer.Serialize(proposal.Changes,CombatCatalog.Json);
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    public async Task ResolveAsync(NarrativeConsequence resolution,CancellationToken ct)
    {
        if (db.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Consequence resolution requires a command transaction.");
        var row=await db.NarrativeConsequences.SingleAsync(x=>
            x.SourceEventId==resolution.SourceEventId,ct);
        if (row.CampaignId!=resolution.CampaignId || row.Status!="Proposed")
            throw new StateConflictException("Consequence is already resolved.");
        row.Status=resolution.Status.ToString();
        row.AppliedWorldRevision=resolution.AppliedWorldRevision;
        row.ResolvedAtUtc=resolution.ResolvedAtUtc;
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }

    private static NarrativeConsequence Project(NarrativeConsequenceRow row) =>
        new(row.SourceEventId,row.CampaignId,Enum.Parse<ConsequenceStatus>(row.Status),
            row.ExpectedWorldRevision,row.Cause,
            JsonSerializer.Deserialize<WorldChange[]>(row.ChangesJson,CombatCatalog.Json)!,
            row.AppliedWorldRevision,row.ResolvedAtUtc);
    private static NarrativeConsequenceRow ToRow(NarrativeConsequence x) => new()
    {
        SourceEventId=x.SourceEventId,CampaignId=x.CampaignId,Status=x.Status.ToString(),
        ExpectedWorldRevision=x.ExpectedWorldRevision,Cause=x.Cause,
        ChangesJson=JsonSerializer.Serialize(x.Changes,CombatCatalog.Json),
        AppliedWorldRevision=x.AppliedWorldRevision,ResolvedAtUtc=x.ResolvedAtUtc
    };
}
