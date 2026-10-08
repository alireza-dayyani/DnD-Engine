using System.Security.Claims;
using System.Text.Json;
using DndEngine.Domain.World;

namespace DndEngine.Application;

public sealed record ContextKnowledge(Guid FactId,KnowledgeStatus Status,int Confidence,
    WorldValue? Belief,long AcquiredGameSeconds);
public sealed record ContextTruncation(string Section,int Available,int Returned,int Clipped=0);
public sealed record DmContextView(CampaignView Campaign,CharacterView? Character,
    object? Encounter,Guid? LocationId,object[] Npcs,object[] Factions,
    object[] Relationships,object[] Quests,object[] Facts,
    object[] Knowledge,object[] Events,
    object[] PendingConsequences,ContextTruncation[] Truncation);

/// <summary>Deterministic, bounded context assembly; all selections follow server-side access checks.</summary>
public sealed class DmContextService(CampaignAccessService access,CampaignService campaigns,
    CharacterService characters,CombatService combat,IEncounterIndex encounters,
    WorldService world,NarrativeConsequenceService consequences)
{
    public async Task<DmContextView> AssembleAsync(ClaimsPrincipal principal,
        Guid campaignId,Guid? characterId=null,Guid? locationId=null,long afterEvent=0,
        CancellationToken ct=default)
    {
        var role=await access.RequireMemberAsync(principal,campaignId,ct);
        if (characterId is { } character)
            await access.RequireCharacterAsync(principal,campaignId,character,ct);
        if (afterEvent<0) throw new Domain.RuleViolation("Invalid event cursor.");
        var campaign=await campaigns.GetAsync(campaignId,ct);
        var sheet=characterId is { } selected ? await characters.GetAsync(selected,ct) : null;
        if (sheet is not null && sheet.CampaignId!=campaignId) throw new AccessDeniedException();
        var full=(await world.GetDmAsync(campaignId,ct)).State;
        var visible=role==CampaignRole.Dm ? null : await world.GetPublicAsync(campaignId,ct);
        var currentLocation=locationId ?? (characterId is { } owner
            ? full.Npcs.FirstOrDefault(x=>x.MechanicalCharacterId==owner)?.LocationId : null);
        if (role==CampaignRole.Player && currentLocation is { } place &&
            !visible!.Locations.Any(x=>x.Id==place)) currentLocation=null;
        var npcSource=role==CampaignRole.Dm
            ? full.Npcs.Cast<object>().ToArray() : visible!.Npcs.Cast<object>().ToArray();
        var factionSource=role==CampaignRole.Dm
            ? full.Factions.Cast<object>().ToArray() : visible!.Factions.Cast<object>().ToArray();
        var questSource=role==CampaignRole.Dm
            ? full.Quests.Where(x=>x.Status==QuestStatus.Active).Cast<object>().ToArray()
            : visible!.Quests.Where(x=>x.Status==QuestStatus.Active).Cast<object>().ToArray();
        var factSource=role==CampaignRole.Dm
            ? full.Facts.Cast<object>().ToArray() : visible!.Facts.Cast<object>().ToArray();
        var relatedNpcs=npcSource.OrderBy(x=> x switch {
                NarrativeNpc n when n.LocationId==currentLocation=>0,
                PublicNpc n when n.LocationId==currentLocation=>0,_=>1 })
            .ThenBy(x=>x switch { NarrativeNpc n=>n.Id,PublicNpc n=>n.Id,_=>Guid.Empty })
            .ToArray();
        var relatedFactions=factionSource.OrderBy(x=>x switch {
            WorldFaction f=>f.Id,PublicFaction f=>f.Id,_=>Guid.Empty }).ToArray();
        var relationships=role==CampaignRole.Dm
            ? full.Relationships.OrderBy(x=>x.Id).Cast<object>().ToArray() : [];
        var quests=questSource.OrderBy(x=>x switch {
            WorldQuest q=>q.Id,PublicQuest q=>q.Id,_=>Guid.Empty }).ToArray();
        var facts=factSource.OrderBy(x=>x switch {
            WorldFact f=>f.Id,PublicFact f=>f.Id,_=>Guid.Empty }).ToArray();
        var knowledge=characterId is { } owned
            ? full.Knowledge.Where(x=>x.Holder.Kind==WorldEntityKind.Character &&
                x.Holder.Id==owned).OrderBy(x=>x.FactId)
                .Select(x=>new ContextKnowledge(x.FactId,x.Status,x.Confidence,
                    x.BeliefValue ?? (x.Status==KnowledgeStatus.Known
                        ? full.Facts.Single(f=>f.Id==x.FactId).Value : null),
                    x.AcquiredGameSeconds)).Take(20).ToArray() : [];
        object[] eventSource=role==CampaignRole.Dm
            ? (await campaigns.EventsAsync(campaignId,afterEvent,21,ct)).Cast<object>().ToArray()
            : (await world.GetPublicTimelineAsync(campaignId,afterEvent,100,ct))
                .Where(x=>characterId is { } owner && x.CharacterId==owner)
                .Take(21).Select(x=>(object)new {
                    x.Sequence,x.EventId,x.Type,x.OccurredAtUtc,x.CharacterId }).ToArray();
        var pending=role==CampaignRole.Dm
            ? await consequences.PendingAsync(campaignId,0,10,ct) : [];
        var activeId=await encounters.CurrentAsync(campaignId,ct);
        object? encounter=null;
        if (activeId is { } id)
        {
            var view=await combat.GetAsync(id,ct);
            encounter=role==CampaignRole.Dm ? view : new {
                view.Encounter.Id,view.Encounter.Status,view.Encounter.Round,
                view.Encounter.TurnNumber,view.CurrentCombatantId,
                combatants=view.Encounter.Combatants.Select(x=>new { x.Id,x.Kind }).ToArray() };
        }
        var sections=new[] {
            BoundSection("npcs",relatedNpcs,10,768),
            BoundSection("factions",relatedFactions,5,768),
            BoundSection("relationships",relationships,10,768),
            BoundSection("quests",quests,10,768),
            BoundSection("facts",facts,10,768),
            BoundSection("knowledge",knowledge.Cast<object>().ToArray(),20,512),
            BoundSection("events",eventSource,20,1024),
            BoundSection("pendingConsequences",pending.Cast<object>().ToArray(),10,1024) };
        var clippedEncounter=encounter is null ? null : Bound(encounter,2048).Item;
        var truncation=sections.Select(x=>x.Truncation).ToList();
        if (encounter is not null && !ReferenceEquals(clippedEncounter,encounter))
            truncation.Add(new("encounter",1,1,1));
        return new(campaign,sheet,clippedEncounter,currentLocation,
            sections[0].Items,sections[1].Items,sections[2].Items,
            sections[3].Items,sections[4].Items,sections[5].Items,
            sections[6].Items,sections[7].Items,truncation.ToArray());
    }

    private static (object[] Items,ContextTruncation Truncation) BoundSection(
        string name,object[] source,int count,int maxChars)
    {
        var selected=source.Take(count).Select(x=>Bound(x,maxChars)).ToArray();
        return (selected.Select(x=>x.Item).ToArray(),
            new(name,source.Length,selected.Length,selected.Count(x=>x.Clipped)));
    }

    private static (object Item,bool Clipped) Bound(object value,int maxChars)
    {
        var json=JsonSerializer.Serialize(value);
        if (json.Length<=maxChars) return (value,false);
        return (new { truncated=true,originalChars=json.Length,
            preview=json[..Math.Min(240,json.Length)] },true);
    }
}
