using DndEngine.Domain.Inventory;
using DndEngine.Domain.Progression;

namespace DndEngine.Application;

public interface IEncounterItemCatalog
{
    Task<EncounterItemPack> GetAsync(DndEngine.Domain.Ruleset ruleset,
        string packVersion, CancellationToken ct);
}

public interface IInventoryStore
{
    Task<InventoryState?> GetAsync(Guid ownerId, CancellationToken ct);
    Task SaveAsync(IReadOnlyList<InventoryOwnerUpdate> updates,
        IReadOnlyList<CampaignEvent> events, CancellationToken ct,
        IReadOnlyList<DroppedItem>? drops = null,
        IReadOnlyList<Guid>? pickups = null);
    Task<DroppedItem?> GetDroppedAsync(Guid itemId,CancellationToken ct);
    Task<IReadOnlyList<DroppedItem>> ListDroppedAsync(Guid campaignId,CancellationToken ct);
}

public sealed record DroppedItem(Guid Id,Guid CampaignId,Guid SourceOwnerId,
    string DefinitionId,int Quantity);

public sealed record InventoryOwnerUpdate(DndEngine.Domain.Character Character,
    InventoryState Inventory, ProgressionState? Progression,
    DndEngine.Domain.Combat.CombatProfile? CombatProfile);
public sealed record InventoryView(Guid OwnerId, InventoryItem[] Items,
    long CopperPieces, long Revision);
public sealed record AddInventoryItem(string DefinitionId, int Quantity,
    long ExpectedRevision);
public sealed record TransferInventoryItem(Guid TargetId, Guid ItemId,
    int Quantity, long ExpectedSourceRevision, long ExpectedTargetRevision);
public sealed record RemoveInventoryItem(Guid ItemId, int Quantity,
    long ExpectedRevision);
public sealed record SetEquipment(Guid ItemId, bool Equipped,long ExpectedRevision);
public sealed record ChangeCurrency(long DeltaCopper,long ExpectedRevision);
public sealed record UseInventoryItem(Guid ItemId,Guid TargetId,long ExpectedRevision);
public sealed record DropInventoryItem(Guid ItemId,int Quantity,long ExpectedRevision);
public sealed record PickupInventoryItem(Guid DroppedItemId,long ExpectedRevision);
public sealed record InventoryUseResult(InventoryView Inventory,int[] Rolls,int HitPointsRegained);
