using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain.Inventory;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class DroppedItemRow
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public Guid SourceOwnerId { get; set; }
    public string DefinitionId { get; set; } = "";
    public int Quantity { get; set; }
}

public sealed class SqliteInventoryStore(CampaignDbContext db) : IInventoryStore
{
    public async Task<InventoryState?> GetAsync(Guid ownerId,CancellationToken ct)
    {
        var row=await db.InventoryStates.AsNoTracking().SingleOrDefaultAsync(x=>x.OwnerId==ownerId,ct);
        return row is null ? null : new(JsonSerializer.Deserialize<DndEngine.Domain.Progression.InventoryItem[]>(
            row.ItemsJson,CombatCatalog.Json)!,row.CopperPieces);
    }

    public async Task<DroppedItem?> GetDroppedAsync(Guid itemId,CancellationToken ct)
    {
        var row=await db.DroppedItems.AsNoTracking().SingleOrDefaultAsync(x=>x.Id==itemId,ct);
        return row is null ? null : new(row.Id,row.CampaignId,row.SourceOwnerId,
            row.DefinitionId,row.Quantity);
    }
    public async Task<IReadOnlyList<DroppedItem>> ListDroppedAsync(Guid campaignId,CancellationToken ct) =>
        await db.DroppedItems.AsNoTracking().Where(x=>x.CampaignId==campaignId)
            .Select(x=>new DroppedItem(x.Id,x.CampaignId,x.SourceOwnerId,x.DefinitionId,x.Quantity))
            .ToArrayAsync(ct);

    public async Task SaveAsync(IReadOnlyList<InventoryOwnerUpdate> updates,
        IReadOnlyList<CampaignEvent> events,CancellationToken ct,
        IReadOnlyList<DroppedItem>? drops=null,IReadOnlyList<Guid>? pickups=null)
    {
        if (updates.Count==0 || updates.Select(x=>x.Character.Id).Distinct().Count()!=updates.Count ||
            updates.Select(x=>x.Character.CampaignId).Distinct().Count()!=1 ||
            events.Any(x=>x.CampaignId!=updates[0].Character.CampaignId))
            throw new DndEngine.Domain.RuleViolation("Invalid inventory transaction.");
        try
        {
            foreach (var update in updates)
            {
                update.Inventory.Validate();
                var character=SqliteCampaignStore.ToRow(update.Character);
                db.Attach(character);
                character.Revision=checked(update.Character.Revision+1);
                db.Entry(character).State=EntityState.Modified;
                db.Entry(character).Property(x=>x.Revision).OriginalValue=update.Character.Revision;
                var inventory=await db.InventoryStates.SingleOrDefaultAsync(x=>x.OwnerId==character.Id,ct);
                if (inventory is null)
                {
                    inventory=new() { OwnerId=character.Id }; db.InventoryStates.Add(inventory);
                }
                inventory.ItemsJson=JsonSerializer.Serialize(update.Inventory.Items,CombatCatalog.Json);
                inventory.CopperPieces=update.Inventory.CopperPieces;
                if (update.Progression is not null)
                {
                    var progression=await db.Progressions.SingleAsync(x=>x.CharacterId==character.Id,ct);
                    progression.StateJson=JsonSerializer.Serialize(update.Progression,CombatCatalog.Json);
                }
                if (update.CombatProfile is not null)
                {
                    var profile=await db.CombatProfiles.SingleOrDefaultAsync(x=>x.CharacterId==character.Id,ct);
                    if (profile is null)
                    {
                        profile=new() { CharacterId=character.Id }; db.CombatProfiles.Add(profile);
                    }
                    profile.StateJson=JsonSerializer.Serialize(update.CombatProfile.State,CombatCatalog.Json);
                }
            }
            foreach (var dropped in drops ?? [])
            {
                if (dropped.CampaignId!=updates[0].Character.CampaignId || dropped.Quantity<1 ||
                    string.IsNullOrWhiteSpace(dropped.DefinitionId))
                    throw new DndEngine.Domain.RuleViolation("Invalid dropped item.");
                db.DroppedItems.Add(new() { Id=dropped.Id,CampaignId=dropped.CampaignId,
                    SourceOwnerId=dropped.SourceOwnerId,DefinitionId=dropped.DefinitionId,
                    Quantity=dropped.Quantity });
            }
            foreach (var pickedId in pickups ?? [])
            {
                var picked=await db.DroppedItems.SingleOrDefaultAsync(x=>x.Id==pickedId,ct)
                    ?? throw new StateConflictException("Dropped item was already picked up.");
                if (picked.CampaignId!=updates[0].Character.CampaignId)
                    throw new DndEngine.Domain.RuleViolation("Dropped item belongs to another campaign.");
                db.DroppedItems.Remove(picked);
            }
            db.Events.AddRange(events.Select(SqliteCampaignStore.ToRow));
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateConcurrencyException)
        { throw new StateConflictException("Inventory owner changed concurrently. Reload before trying again."); }
        catch (DbUpdateException ex) when (ex.InnerException is SqliteException { SqliteErrorCode: 19 })
        { throw new StateConflictException("Inventory write conflicted with existing state. Reload before trying again."); }
        finally { db.ChangeTracker.Clear(); }
    }
}
