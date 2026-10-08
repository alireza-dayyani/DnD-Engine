using DndEngine.Application;

namespace DndEngine.Api;

internal static class InventoryEndpoints
{
    public static void MapInventory(this WebApplication app)
    {
        app.MapGet("/campaigns/{id:guid}/item-definitions",(Guid id,InventoryService s,CancellationToken ct) =>
            s.DefinitionsAsync(id,ct));
        app.MapGet("/characters/{id:guid}/inventory/state",(Guid id,InventoryService s,CancellationToken ct) =>
            s.GetAsync(id,ct));
        app.MapGet("/campaigns/{id:guid}/dropped-items",(Guid id,InventoryService s,CancellationToken ct) =>
            s.ListDroppedAsync(id,ct));
        app.MapPost("/characters/{id:guid}/inventory/items",(Guid id,AddInventoryItem r,InventoryService s,CancellationToken ct) =>
            s.AddAsync(id,r,ct)).WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/characters/{id:guid}/inventory/transfer",(Guid id,TransferInventoryItem r,InventoryService s,CancellationToken ct) =>
            s.TransferAsync(id,r,ct)).WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/characters/{id:guid}/inventory/items/remove",(Guid id,RemoveInventoryItem r,InventoryService s,CancellationToken ct) =>
            s.RemoveAsync(id,r,ct)).WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/characters/{id:guid}/inventory/items/drop",(Guid id,DropInventoryItem r,InventoryService s,CancellationToken ct) =>
            s.DropAsync(id,r,ct)).WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/characters/{id:guid}/inventory/items/pickup",(Guid id,PickupInventoryItem r,InventoryService s,CancellationToken ct) =>
            s.PickupAsync(id,r,ct)).WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/characters/{id:guid}/inventory/equipment",(Guid id,SetEquipment r,InventoryService s,CancellationToken ct) =>
            s.EquipAsync(id,r,ct)).WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/characters/{id:guid}/inventory/currency",(Guid id,ChangeCurrency r,InventoryService s,CancellationToken ct) =>
            s.CurrencyAsync(id,r,ct)).WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/characters/{id:guid}/inventory/items/use",(Guid id,UseInventoryItem r,InventoryService s,CancellationToken ct) =>
            s.UseAsync(id,r,ct)).WithMetadata(new IdempotencyRequiredAttribute());
    }
}
