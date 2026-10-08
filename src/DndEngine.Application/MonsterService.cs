using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Monsters;
using DndEngine.Domain.Inventory;
using DndEngine.Domain.Progression;

namespace DndEngine.Application;

public sealed class MonsterService(ICampaignStore campaigns, ICombatStore combatStore,
    IMonsterCatalog catalog, IMonsterStore store, CombatService combat,
    TimeProvider clock)
{
    public async Task<MonsterPack> DefinitionsAsync(Guid campaignId,
        string packVersion = "1", CancellationToken ct = default)
    {
        var campaign = await campaigns.GetCampaignAsync(campaignId,ct)
            ?? throw new NotFoundException("Campaign not found.");
        return await catalog.GetAsync(campaign.Ruleset,packVersion,ct);
    }

    public async Task<MonsterView> GetAsync(Guid id, CancellationToken ct = default)
    {
        var instance = await store.GetAsync(id,ct) ?? throw new NotFoundException("Monster not found.");
        var campaign = await campaigns.GetCampaignAsync(instance.CampaignId,ct)
            ?? throw new NotFoundException("Campaign not found.");
        var definition = (await catalog.GetAsync(campaign.Ruleset,instance.PackVersion,ct))
            .Monsters.Single(x=>x.Id==instance.DefinitionId);
        var character = await campaigns.GetCharacterAsync(id,ct)
            ?? throw new NotFoundException("Monster character state not found.");
        var profile = await combatStore.GetProfileAsync(id,ct)
            ?? throw new NotFoundException("Monster combat profile not found.");
        return new(instance,definition,CharacterView.From(character),profile.State);
    }

    public async Task<MonsterView> CreateAsync(CreateMonsterInstance request,
        CancellationToken ct = default)
    {
        var campaign = await campaigns.GetCampaignAsync(request.CampaignId,ct)
            ?? throw new NotFoundException("Campaign not found.");
        var definition = (await catalog.GetAsync(campaign.Ruleset,request.PackVersion,ct))
            .Monsters.SingleOrDefault(x=>x.Id==request.DefinitionId)
            ?? throw new RuleViolation("Monster definition is not in the selected pack.");
        var supported = definition.Actions.Where(x=>x.Supported).ToArray();
        var weaponIds = supported.Select(x=>x.WeaponId!).Distinct(StringComparer.Ordinal).ToArray();
        var ammunition = request.Ammunition ?? new Dictionary<string,int>();
        if (ammunition.Keys.Any(x=>!weaponIds.Contains(x,StringComparer.Ordinal)) ||
            ammunition.Values.Any(x=>x is < 0 or > 100_000))
            throw new RuleViolation("Ammunition must name a supported monster weapon and be nonnegative.");
        var id = Guid.NewGuid();
        var uses=definition.Spells.Where(x=>x.LimitedUseGroup is not null)
            .GroupBy(x=>x.LimitedUseGroup!).ToDictionary(x=>x.Key,x=>x.Max(y=>y.UsesPerDay));
        var instance = new MonsterInstance(id,campaign.Id,definition.Id,request.PackVersion,
            DndEngine.Domain.Progression.SpellPackVersions.Current,uses);
        var character = new Character(id,campaign.Id,request.Name ?? definition.Name,1,
            definition.Abilities,[],[],definition.ArmorClass,new(definition.HitPoints));
        var capabilities = new CombatCapabilities(definition.Speed,weaponIds,
            definition.Resistances,definition.Immunities,definition.Vulnerabilities,
            definition.ConditionImmunities);
        var initialItems=definition.Gear.SelectMany(x=>Enumerable.Range(0,x.Quantity)
            .Select(_=>new InventoryItem(Guid.NewGuid(),x.DefinitionId,
                x.DefinitionId=="chain-shirt"))).ToArray();
        var profile = new CombatProfile(new(id,capabilities,
            initialItems.Where(x=>weaponIds.Contains(x.DefinitionId,StringComparer.Ordinal))
                .Select(x=>new OwnedWeapon(x.Id,x.DefinitionId,
                    ammunition.GetValueOrDefault(x.DefinitionId))).ToArray(),[]));
        var entry = new CampaignEvent(0,Guid.NewGuid(),campaign.Id,id,"MonsterSpawned",
            clock.GetUtcNow(),campaign.Ruleset,TimelineSerialization.SchemaVersion,0,
            TimelineSerialization.Serialize(new { MonsterId=id,definition.Id,
                instance.PackVersion,character.Name,definition.HitPoints,
                WeaponIds=weaponIds }));
        await store.CreateAsync(instance,character,profile,new InventoryState(initialItems,0),entry,ct);
        return new(instance,definition,CharacterView.From(character),profile.State);
    }

    public async Task<CombatCommandResult<CombatantState>> AddToEncounterAsync(
        Guid encounterId, AddMonsterToEncounter request, CancellationToken ct = default)
    {
        var monster = await store.GetAsync(request.MonsterId,ct)
            ?? throw new NotFoundException("Monster not found.");
        var encounter = await combatStore.GetEncounterAsync(encounterId,ct)
            ?? throw new NotFoundException("Encounter not found.");
        if (monster.CampaignId != encounter.State.CampaignId)
            throw new RuleViolation("Monster belongs to another campaign.");
        return await combat.AddAsync(encounterId,new(monster.Id,CombatantKind.Monster,
            ZeroHpPolicy.Die,request.Surprised,request.InitiativeGroup),ct);
    }
}
