using DndEngine.Domain;
using DndEngine.Domain.Combat;

namespace DndEngine.Application;

public sealed class EncounterRewardService(IEncounterRewardStore rewards,ICombatStore combat,
    ICampaignStore campaigns,IInventoryStore inventories,InventoryService inventory,
    TimeProvider clock)
{
    public async Task<EncounterRewardsView> GetAsync(Guid encounterId,CancellationToken ct=default)
    {
        var state=await rewards.GetAsync(encounterId,ct)
            ?? throw new NotFoundException("Completed encounter rewards not found.");
        var loot=new Dictionary<Guid,DndEngine.Domain.Progression.InventoryItem[]>();
        foreach (var monsterId in state.DefeatedMonsterIds)
            loot[monsterId]=(await inventories.GetAsync(monsterId,ct))?.Items ?? [];
        return new(state,loot);
    }

    public async Task<EncounterRewardState> AwardExperienceAsync(Guid encounterId,
        AwardEncounterExperience request,CancellationToken ct=default)
    {
        var before=await rewards.GetAsync(encounterId,ct)
            ?? throw new NotFoundException("Completed encounter rewards not found.");
        if (before.Revision!=request.ExpectedRevision)
            throw new StateConflictException("Reward revision changed. Reload before awarding XP.");
        var encounter=await combat.GetEncounterAsync(encounterId,ct)
            ?? throw new NotFoundException("Encounter not found.");
        if (encounter.State.Status!=EncounterStatus.Completed)
            throw new RuleViolation("Experience requires encounter completion.");
        var awards=request.Awards ?? [];
        if (awards.Length==0 || awards.Length>100 || awards.Select(x=>x.CharacterId).Distinct().Count()!=awards.Length ||
            awards.Any(x=>x.Amount<=0 || !encounter.State.Combatants.Any(c=>
                c.CharacterId==x.CharacterId && c.Kind==CombatantKind.PlayerCharacter)) ||
            awards.Any(x=>before.Awards.Any(y=>y.CharacterId==x.CharacterId)) ||
            (long)before.Awards.Sum(x=>x.Amount)+awards.Sum(x=>(long)x.Amount)>before.AvailableExperience)
            throw new RuleViolation("Experience awards must be positive, unique, and within defeated monster XP.");
        var campaign=await campaigns.GetCampaignAsync(encounter.State.CampaignId,ct)
            ?? throw new NotFoundException("Campaign not found.");
        var after=before with { Awards=[..before.Awards,..awards],Revision=before.Revision+1 };
        var entry=new CampaignEvent(0,Guid.NewGuid(),campaign.Id,null,"ExperienceAwarded",
            clock.GetUtcNow(),campaign.Ruleset,TimelineSerialization.SchemaVersion,null,
            TimelineSerialization.Serialize(new { EncounterId=encounterId,awards,
                totalAwarded=after.Awards.Sum(x=>x.Amount),before.AvailableExperience }));
        await rewards.AwardExperienceAsync(before,after,entry,ct);
        return after;
    }

    public async Task<InventoryView> AwardLootAsync(Guid encounterId,AwardEncounterLoot request,
        CancellationToken ct=default)
    {
        var reward=await rewards.GetAsync(encounterId,ct)
            ?? throw new NotFoundException("Completed encounter rewards not found.");
        if (!reward.DefeatedMonsterIds.Contains(request.MonsterId))
            throw new RuleViolation("Loot source was not a defeated monster in this encounter.");
        var encounter=await combat.GetEncounterAsync(encounterId,ct)
            ?? throw new NotFoundException("Encounter not found.");
        if (!encounter.State.Combatants.Any(x=>x.CharacterId==request.RecipientId &&
            x.Kind==CombatantKind.PlayerCharacter))
            throw new RuleViolation("Loot recipient must be a participating player character.");
        return await inventory.TransferLootAsync(encounterId,request.MonsterId,
            new(request.RecipientId,request.ItemId,request.Quantity,
                request.ExpectedMonsterRevision,request.ExpectedRecipientRevision),ct);
    }
}
