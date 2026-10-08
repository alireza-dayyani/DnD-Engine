using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Inventory;
using DndEngine.Domain.Progression;

namespace DndEngine.Application;

public sealed class InventoryService(ICampaignStore campaigns,IInventoryStore inventories,
    IProgressionStore progressions,ICombatStore combatStore,ICharacterRulesCatalog characterCatalog,
    ICombatCatalog combatCatalog,IEncounterItemCatalog itemCatalog,IDiceRoller dice,TimeProvider clock)
{
    private sealed record Owner(Character Character,Campaign Campaign,InventoryState Inventory,
        ProgressionState? Progression,CombatProfile? Profile,CharacterRules Rules,
        CombatContent Combat,EncounterItemPack Extra);

    public async Task<EncounterItemPack> DefinitionsAsync(Guid campaignId,CancellationToken ct=default)
    {
        var campaign=await campaigns.GetCampaignAsync(campaignId,ct)
            ?? throw new NotFoundException("Campaign not found.");
        return await itemCatalog.GetAsync(campaign.Ruleset,"1",ct);
    }
    public async Task<InventoryView> GetAsync(Guid ownerId,CancellationToken ct=default) =>
        View(await Load(ownerId,ct));

    public async Task<InventoryView> AddAsync(Guid ownerId,AddInventoryItem request,CancellationToken ct=default)
    {
        var owner=await LoadMutable(ownerId,request.ExpectedRevision,ct);
        Guard.Range(request.Quantity,1,1_000_000,"Quantity");
        var definition=Definition(owner,request.DefinitionId);
        if (owner.Progression is null && definition.Kind!=ItemKind.Gear)
            throw new RuleViolation("Only choice-based characters can acquire combat equipment through this inventory command.");
        var items=AddItems(owner.Inventory.Items,request.DefinitionId,request.Quantity,Stackable(owner,request.DefinitionId));
        var next=owner.Inventory with { Items=items };
        await Save([owner with { Inventory=next }],[Event(owner,"ItemAcquired",new {
            ownerId,request.DefinitionId,request.Quantity })],ct);
        return View(owner with { Inventory=next },1);
    }

    public async Task<InventoryView> RemoveAsync(Guid ownerId,RemoveInventoryItem request,CancellationToken ct=default)
    {
        var owner=await LoadMutable(ownerId,request.ExpectedRevision,ct);
        var (items,item)=Take(owner.Inventory.Items,request.ItemId,request.Quantity);
        var next=owner.Inventory with { Items=items };
        await Save([owner with { Inventory=next }],[Event(owner,"ItemRemoved",new {
            ownerId,itemId=item.Id,item.DefinitionId,request.Quantity })],ct);
        return View(owner with { Inventory=next },1);
    }

    public async Task<IReadOnlyList<DroppedItem>> ListDroppedAsync(Guid campaignId,CancellationToken ct=default)
    {
        if (await campaigns.GetCampaignAsync(campaignId,ct) is null)
            throw new NotFoundException("Campaign not found.");
        return await inventories.ListDroppedAsync(campaignId,ct);
    }

    public async Task<DroppedItem> DropAsync(Guid ownerId,DropInventoryItem request,CancellationToken ct=default)
    {
        var owner=await LoadMutable(ownerId,request.ExpectedRevision,ct);
        var (remaining,item)=Take(owner.Inventory.Items,request.ItemId,request.Quantity);
        var dropped=new DroppedItem(item.Quantity==request.Quantity ? item.Id : Guid.NewGuid(),
            owner.Campaign.Id,ownerId,item.DefinitionId,request.Quantity);
        var next=owner with { Inventory=owner.Inventory with { Items=remaining } };
        await Save([next],[Event(owner,"ItemDropped",new { ownerId,dropped })],ct,
            [dropped]);
        return dropped;
    }

    public async Task<InventoryView> PickupAsync(Guid ownerId,PickupInventoryItem request,CancellationToken ct=default)
    {
        var owner=await LoadMutable(ownerId,request.ExpectedRevision,ct);
        var dropped=await inventories.GetDroppedAsync(request.DroppedItemId,ct)
            ?? throw new NotFoundException("Dropped item not found.");
        if (dropped.CampaignId!=owner.Campaign.Id)
            throw new RuleViolation("Dropped item belongs to another campaign.");
        var definition=Definition(owner,dropped.DefinitionId);
        if (owner.Progression is null && definition.Kind!=ItemKind.Gear)
            throw new RuleViolation("Monster equipment cannot be assigned here.");
        var existingStack=owner.Inventory.Items.FirstOrDefault(x=>x.DefinitionId==dropped.DefinitionId &&
            !x.Equipped && Stackable(owner,dropped.DefinitionId));
        var items=existingStack is not null ?
            AddItems(owner.Inventory.Items,dropped.DefinitionId,dropped.Quantity,true) :
            [..owner.Inventory.Items,new InventoryItem(dropped.Id,dropped.DefinitionId,false,dropped.Quantity)];
        var next=owner with { Inventory=owner.Inventory with { Items=items } };
        await Save([next],[Event(owner,"ItemAcquired",new { ownerId,droppedItemId=dropped.Id,
            dropped.SourceOwnerId,dropped.DefinitionId,dropped.Quantity })],ct,null,[dropped.Id]);
        return View(next,1);
    }

    public Task<InventoryView> TransferAsync(Guid sourceId,TransferInventoryItem request,CancellationToken ct=default) =>
        TransferCoreAsync(sourceId,request,"ItemTransferred",null,ct);

    internal Task<InventoryView> TransferLootAsync(Guid encounterId,Guid monsterId,
        TransferInventoryItem request,CancellationToken ct) =>
        TransferCoreAsync(monsterId,request,"LootAwarded",encounterId,ct);

    private async Task<InventoryView> TransferCoreAsync(Guid sourceId,TransferInventoryItem request,
        string eventType,Guid? encounterId,CancellationToken ct)
    {
        if (sourceId==request.TargetId) throw new RuleViolation("Transfer requires a different owner.");
        var source=await LoadMutable(sourceId,request.ExpectedSourceRevision,ct);
        var target=await LoadMutable(request.TargetId,request.ExpectedTargetRevision,ct);
        if (source.Character.CampaignId!=target.Character.CampaignId)
            throw new RuleViolation("Transfer owners must be in the same campaign.");
        var (sourceItems,item)=Take(source.Inventory.Items,request.ItemId,request.Quantity);
        Definition(target,item.DefinitionId);
        if (target.Progression is null && Definition(target,item.DefinitionId).Kind!=ItemKind.Gear)
            throw new RuleViolation("Monster equipment cannot be reassigned without a supported combat projection.");
        var received=item.Quantity==request.Quantity ? item with { Equipped=false } :
            item with { Id=Guid.NewGuid(),Quantity=request.Quantity,Equipped=false };
        var stack=target.Inventory.Items.FirstOrDefault(x=>x.DefinitionId==item.DefinitionId &&
            !x.Equipped && Stackable(target,item.DefinitionId));
        var targetItems=stack is not null ? AddItems(target.Inventory.Items,item.DefinitionId,
            request.Quantity,true) : [..target.Inventory.Items,received];
        var nextSource=source with { Inventory=source.Inventory with { Items=sourceItems } };
        var nextTarget=target with { Inventory=target.Inventory with { Items=targetItems } };
        await Save([nextSource,nextTarget],[Event(source,eventType,new {
            encounterId,
            sourceId,request.TargetId,itemId=item.Id,item.DefinitionId,request.Quantity,
            receivedItemId=stack?.Id ?? received.Id })],ct);
        return View(nextSource,1);
    }

    public async Task<InventoryView> EquipAsync(Guid ownerId,SetEquipment request,CancellationToken ct=default)
    {
        var owner=await LoadMutable(ownerId,request.ExpectedRevision,ct);
        if (owner.Progression is null)
            throw new RuleViolation("Monster equipment uses its pinned stat block and cannot be changed here.");
        var item=owner.Inventory.Items.SingleOrDefault(x=>x.Id==request.ItemId)
            ?? throw new RuleViolation("Item is not owned.");
        var definition=Definition(owner,item.DefinitionId);
        if (definition.Kind==ItemKind.Gear || item.Quantity!=1)
            throw new RuleViolation("Item has no equipment slot.");
        var items=owner.Inventory.Items.Select(x=>x.Id==item.Id ? x with { Equipped=request.Equipped } :
            request.Equipped && definition.Kind is ItemKind.Armor or ItemKind.Shield &&
            Definition(owner,x.DefinitionId).Kind==definition.Kind ? x with { Equipped=false } : x).ToArray();
        var next=owner with { Inventory=owner.Inventory with { Items=items } };
        await Save([next],[Event(owner,"EquipmentChanged",new { ownerId,itemId=item.Id,
            item.DefinitionId,request.Equipped })],ct);
        return View(next,1);
    }

    public async Task<InventoryView> CurrencyAsync(Guid ownerId,ChangeCurrency request,CancellationToken ct=default)
    {
        var owner=await LoadMutable(ownerId,request.ExpectedRevision,ct);
        long after;
        try { after=checked(owner.Inventory.CopperPieces+request.DeltaCopper); }
        catch (OverflowException) { throw new RuleViolation("Currency amount exceeds supported range."); }
        if (after<0 || request.DeltaCopper==0) throw new RuleViolation("Invalid currency change.");
        var next=owner with { Inventory=owner.Inventory with { CopperPieces=after } };
        await Save([next],[Event(owner,"CurrencyChanged",new { ownerId,
            request.DeltaCopper,before=owner.Inventory.CopperPieces,after })],ct);
        return View(next,1);
    }

    public async Task<InventoryUseResult> UseAsync(Guid ownerId,UseInventoryItem request,CancellationToken ct=default)
    {
        var owner=await LoadMutable(ownerId,request.ExpectedRevision,ct);
        if (request.TargetId!=ownerId)
            throw new RuleViolation("Outside combat, Potion of Healing can only be drunk by its owner.");
        var item=owner.Inventory.Items.SingleOrDefault(x=>x.Id==request.ItemId)
            ?? throw new RuleViolation("Item is not owned.");
        var extra=owner.Extra.Items.SingleOrDefault(x=>x.Id==item.DefinitionId);
        if (extra?.Consumable!=true || extra.EffectId!="heal-2d4-plus-2")
            throw new RuleViolation("Item has no supported consumable effect.");
        owner.Character.Health.RequireAlive();
        var (items,_)=Take(owner.Inventory.Items,item.Id,1);
        var rolls=dice.Roll(new(2,4)).Rolls.ToArray();
        var change=owner.Character.Health.Heal(rolls.Sum()+2);
        var next=owner with { Inventory=owner.Inventory with { Items=items } };
        await Save([next],[Event(owner,"ItemConsumed",new { ownerId,itemId=item.Id,
            item.DefinitionId,targetId=request.TargetId,rolls,change.HitPointsRegained })],ct);
        return new(View(next,1),rolls,change.HitPointsRegained);
    }

    private async Task<Owner> Load(Guid id,CancellationToken ct)
    {
        var character=await campaigns.GetCharacterAsync(id,ct)
            ?? throw new NotFoundException("Inventory owner not found.");
        var campaign=await campaigns.GetCampaignAsync(character.CampaignId,ct)
            ?? throw new NotFoundException("Campaign not found.");
        var progression=await progressions.GetAsync(id,ct);
        var inventory=await inventories.GetAsync(id,ct) ?? new(progression?.Inventory ?? [],progression?.CurrencyCopper ?? 0);
        inventory.Validate();
        return new(character,campaign,inventory,progression,
            await combatStore.GetProfileAsync(id,ct),
            await characterCatalog.GetAsync(campaign.Ruleset,ct),
            await combatCatalog.GetAsync(campaign.Ruleset,ct),
            await itemCatalog.GetAsync(campaign.Ruleset,"1",ct));
    }
    private async Task<Owner> LoadMutable(Guid id,long expected,CancellationToken ct)
    {
        var owner=await Load(id,ct);
        if (owner.Character.Revision!=expected)
            throw new StateConflictException("Inventory owner revision changed. Reload before trying again.");
        if (await combatStore.IsEnrolledAsync(id,ct))
            throw new StateConflictException("Use encounter item commands while the owner is enrolled in combat.");
        return owner;
    }
    private static ItemDefinition Definition(Owner owner,string id)
    {
        var extra=owner.Extra.Items.SingleOrDefault(x=>x.Id==id);
        return extra is not null ? new(extra.Id,extra.Name,extra.Kind) :
            CharacterDeriver.FindItem(id,owner.Rules,owner.Combat);
    }
    private static bool Stackable(Owner owner,string id) =>
        owner.Extra.Items.SingleOrDefault(x=>x.Id==id)?.Stackable==true;
    private static InventoryItem[] AddItems(InventoryItem[] items,string definitionId,int quantity,bool stackable)
    {
        if (stackable)
        {
            var old=items.FirstOrDefault(x=>x.DefinitionId==definitionId && !x.Equipped);
            if (old is not null)
            {
                if ((long)old.Quantity+quantity>1_000_000) throw new RuleViolation("Stack quantity exceeds limit.");
                return items.Select(x=>x.Id==old.Id ? x with { Quantity=x.Quantity+quantity } : x).ToArray();
            }
            return [..items,new InventoryItem(Guid.NewGuid(),definitionId,false,quantity)];
        }
        if (quantity>100) throw new RuleViolation("Nonstackable acquisition is limited to 100 items per command.");
        return [..items,..Enumerable.Range(0,quantity).Select(_=>new InventoryItem(Guid.NewGuid(),definitionId))];
    }
    private static (InventoryItem[] Items,InventoryItem Item) Take(InventoryItem[] items,Guid id,int quantity)
    {
        Guard.Range(quantity,1,1_000_000,"Quantity");
        var item=items.SingleOrDefault(x=>x.Id==id) ?? throw new RuleViolation("Item is not owned.");
        if (item.Equipped || quantity>item.Quantity)
            throw new RuleViolation("Unequip the item and supply an owned quantity before moving it.");
        return (items.Where(x=>x.Id!=id).Append(item with { Quantity=item.Quantity-quantity })
            .Where(x=>x.Quantity>0).ToArray(),item);
    }
    private async Task Save(Owner[] owners,CampaignEvent[] events,CancellationToken ct,
        DroppedItem[]? drops=null,Guid[]? pickups=null)
    {
        var updates=new List<InventoryOwnerUpdate>();
        foreach (var owner in owners)
        {
            var state=owner.Progression is null ? null : owner.Progression with {
                Inventory=owner.Inventory.Items,CurrencyCopper=owner.Inventory.CopperPieces };
            var character=owner.Character;
            var profile=owner.Profile;
            if (state is not null)
            {
                character=ProgressionService.Materialize(character.Id,character.CampaignId,
                    character.Name,state,owner.Rules,owner.Combat,character.Health,character.Revision);
                var sheet=CharacterDeriver.Derive(character,state,owner.Rules,owner.Combat);
                profile=ProgressionService.Profile(character.Id,sheet,owner.Combat,profile);
            }
            else if (profile is not null)
            {
                var owned=owner.Inventory.Items.Select(x=>x.Id).ToHashSet();
                profile=new CombatProfile(profile.State with {
                    Weapons=profile.State.Weapons.Where(x=>owned.Contains(x.Id)).ToArray() });
            }
            updates.Add(new(character,owner.Inventory,state,profile));
        }
        await inventories.SaveAsync(updates,events,ct,drops,pickups);
    }
    private CampaignEvent Event(Owner owner,string type,object data) =>
        new(0,Guid.NewGuid(),owner.Campaign.Id,owner.Character.Id,type,clock.GetUtcNow(),
            owner.Campaign.Ruleset,TimelineSerialization.SchemaVersion,
            owner.Character.Revision+1,TimelineSerialization.Serialize(data));
    private static InventoryView View(Owner owner,int revisionIncrement=0) =>
        new(owner.Character.Id,owner.Inventory.Items,owner.Inventory.CopperPieces,
            owner.Character.Revision+revisionIncrement);
}
