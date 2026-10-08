namespace DndEngine.Infrastructure;

public sealed class InventoryStateRow
{
    public Guid OwnerId { get; set; }
    public string ItemsJson { get; set; } = "";
    public long CopperPieces { get; set; }
}
