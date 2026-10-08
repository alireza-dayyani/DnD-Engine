namespace DndEngine.Application;

public interface IEncounterIndex
{
    Task<Guid?> CurrentAsync(Guid campaignId,CancellationToken ct);
}
