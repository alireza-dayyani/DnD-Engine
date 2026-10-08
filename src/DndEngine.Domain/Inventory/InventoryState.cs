using DndEngine.Domain.Progression;

namespace DndEngine.Domain.Inventory;

public sealed record InventoryState(InventoryItem[] Items, long CopperPieces)
{
    public void Validate()
    {
        if (Items is null || CopperPieces<0 ||
            Items.Select(x=>x.Id).Distinct().Count()!=Items.Length ||
            Items.Any(x=>x.Id==Guid.Empty || string.IsNullOrWhiteSpace(x.DefinitionId) ||
                x.Quantity is < 1 or > 1_000_000))
            throw new RuleViolation("Invalid inventory state.");
    }
}
