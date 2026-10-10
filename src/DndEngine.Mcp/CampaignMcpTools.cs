using System.ComponentModel;
using System.Security.Claims;
using System.Text.Json;
using System.Text.Json.Serialization;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.World;
using DndEngine.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using ModelContextProtocol.Server;

namespace DndEngine.Mcp;

[McpServerToolType]
[Authorize]
public sealed class CampaignMcpTools(CampaignAccessService access,CampaignService campaigns,
    CharacterService characters,CombatService combat,InventoryService inventory,
    MechanicsService mechanics,WorldService world,DmContextService context,
    IEncounterIndex encounters,IDiceRoller dice,DurableMcpCommandRunner commands,
    NarrativeConsequenceService consequences)
{
    private static readonly JsonSerializerOptions JsonOptions=new(JsonSerializerDefaults.Web)
        { Converters={ new JsonStringEnumConverter() } };
    private static JsonElement Json(object? value) =>
        JsonSerializer.SerializeToElement(value,JsonOptions);
    private async Task<JsonElement> Run<T>(ClaimsPrincipal principal,Guid operationId,
        string tool,object input,Func<CancellationToken,Task<T>> action,CancellationToken ct) =>
        JsonDocument.Parse(await commands.RunAsync(operationId,
            CampaignAccessService.Subject(principal),tool,input,action,ct)).RootElement.Clone();

    private async Task<CharacterView> Character(ClaimsPrincipal principal,Guid campaignId,
        Guid characterId,CancellationToken ct)
    {
        await access.RequireCharacterAsync(principal,campaignId,characterId,ct);
        var sheet=await characters.GetAsync(characterId,ct);
        if (sheet.CampaignId!=campaignId) throw new AccessDeniedException();
        return sheet;
    }

    private async Task<CombatView> Encounter(ClaimsPrincipal principal,Guid campaignId,
        Guid encounterId,Guid? actorId,CancellationToken ct)
    {
        await access.RequireMemberAsync(principal,campaignId,ct);
        var view=await combat.GetAsync(encounterId,ct);
        if (view.Encounter.CampaignId!=campaignId) throw new AccessDeniedException();
        if (actorId is { } actor)
        {
            var combatant=view.Encounter.Combatants.SingleOrDefault(x=>x.Id==actor)
                ?? throw new RuleViolation("Combatant is absent.");
            await Character(principal,campaignId,combatant.CharacterId,ct);
        }
        return view;
    }

    private async Task RequireReactionControl(ClaimsPrincipal principal,Guid campaignId,
        CombatView view,Guid defenderId,CancellationToken ct)
    {
        var defender=view.Encounter.Combatants.SingleOrDefault(x=>x.Id==defenderId)
            ?? throw new RuleViolation("Combatant is absent.");
        await Character(principal,campaignId,defender.CharacterId,ct);
    }

    [McpServerTool(Name="get_campaign_summary",UseStructuredContent=true),
     Description("Read campaign identity, pinned ruleset and authoritative game time. Requires membership in the campaign.")]
    public async Task<JsonElement> GetCampaignSummary(ClaimsPrincipal user,Guid campaignId,
        CancellationToken ct)
    {
        await access.RequireMemberAsync(user,campaignId,ct);
        return Json(await campaigns.GetAsync(campaignId,ct));
    }

    [McpServerTool(Name="get_character_sheet",UseStructuredContent=true),
     Description("Read an owned character sheet, or any character in the campaign as DM.")]
    public async Task<JsonElement> GetCharacterSheet(ClaimsPrincipal user,Guid campaignId,
        Guid characterId,CancellationToken ct) => Json(await Character(user,campaignId,characterId,ct));

    [McpServerTool(Name="get_inventory",UseStructuredContent=true),
     Description("Read inventory for an owned character, or for a campaign character as DM.")]
    public async Task<JsonElement> GetInventory(ClaimsPrincipal user,Guid campaignId,
        Guid characterId,CancellationToken ct)
    {
        await Character(user,campaignId,characterId,ct);
        return Json(await inventory.GetAsync(characterId,ct));
    }

    [McpServerTool(Name="get_active_encounter",UseStructuredContent=true),
     Description("Read current encounter and turn. Player output omits other combatants' profiles and sheets.")]
    public async Task<JsonElement> GetActiveEncounter(ClaimsPrincipal user,Guid campaignId,
        CancellationToken ct)
    {
        var role=await access.RequireMemberAsync(user,campaignId,ct);
        var id=await encounters.CurrentAsync(campaignId,ct);
        if (id is null) return Json(new { active=false });
        var view=await combat.GetAsync(id.Value,ct);
        return role==CampaignRole.Dm ? Json(view) : Json(new {
            active=true,view.Encounter.Id,view.Encounter.Status,view.Encounter.Round,
            view.Encounter.TurnNumber,view.CurrentCombatantId,
            combatants=view.Encounter.Combatants.Select(x=>new { x.Id,x.Kind }).ToArray() });
    }

    [McpServerTool(Name="get_available_actions",UseStructuredContent=true),
     Description("List supported command names and current owned turn resources. The rules engine still validates every action.")]
    public async Task<JsonElement> GetAvailableActions(ClaimsPrincipal user,Guid campaignId,
        Guid characterId,CancellationToken ct)
    {
        await Character(user,campaignId,characterId,ct);
        var id=await encounters.CurrentAsync(campaignId,ct);
        if (id is null) return Json(new { encounterId=(Guid?)null,
            supported=new[] { "make_skill_check","make_saving_throw","roll_dice" } });
        var view=await combat.GetAsync(id.Value,ct);
        var actor=view.Encounter.Combatants.FirstOrDefault(x=>x.CharacterId==characterId);
        return Json(new { encounterId=id,actorId=actor?.Id,
            isTurn=actor is not null && view.CurrentCombatantId==actor.Id,
            resources=actor?.Resources,
            supported=new[] { "perform_attack","cast_spell","use_item","end_turn" } });
    }

    [McpServerTool(Name="get_visible_world",UseStructuredContent=true),
     Description("Read a server-filtered world projection. DM sees full state; players see only public/discovered data.")]
    public async Task<JsonElement> GetVisibleWorld(ClaimsPrincipal user,Guid campaignId,
        CancellationToken ct)
    {
        var role=await access.RequireMemberAsync(user,campaignId,ct);
        return Json(role==CampaignRole.Dm ? await world.GetDmAsync(campaignId,ct)
            : await world.GetPublicAsync(campaignId,ct));
    }

    [McpServerTool(Name="get_npc",UseStructuredContent=true),
     Description("Read one NPC. Players may read only the public NPC projection.")]
    public async Task<JsonElement> GetNpc(ClaimsPrincipal user,Guid campaignId,Guid npcId,
        CancellationToken ct)
    {
        var role=await access.RequireMemberAsync(user,campaignId,ct);
        if (role==CampaignRole.Dm)
            return Json((await world.GetDmAsync(campaignId,ct)).State.Npcs
                .SingleOrDefault(x=>x.Id==npcId) ?? throw new NotFoundException("NPC not found."));
        return Json((await world.GetPublicAsync(campaignId,ct)).Npcs
            .SingleOrDefault(x=>x.Id==npcId) ?? throw new NotFoundException("NPC not found."));
    }

    [McpServerTool(Name="get_location",UseStructuredContent=true),
     Description("Read one location. Players may read only discovered locations.")]
    public async Task<JsonElement> GetLocation(ClaimsPrincipal user,Guid campaignId,
        Guid locationId,CancellationToken ct)
    {
        var role=await access.RequireMemberAsync(user,campaignId,ct);
        var locations=role==CampaignRole.Dm
            ? (await world.GetDmAsync(campaignId,ct)).State.Locations
            : (await world.GetPublicAsync(campaignId,ct)).Locations;
        return Json(locations.SingleOrDefault(x=>x.Id==locationId)
            ?? throw new NotFoundException("Location not found."));
    }

    [McpServerTool(Name="get_active_quests",UseStructuredContent=true),
     Description("Read active quests. Player output contains only public quest fields.")]
    public async Task<JsonElement> GetActiveQuests(ClaimsPrincipal user,Guid campaignId,
        CancellationToken ct)
    {
        var role=await access.RequireMemberAsync(user,campaignId,ct);
        return role==CampaignRole.Dm
            ? Json((await world.GetDmAsync(campaignId,ct)).State.Quests.Where(x=>x.Status==QuestStatus.Active).ToArray())
            : Json((await world.GetPublicAsync(campaignId,ct)).Quests.Where(x=>x.Status==QuestStatus.Active).ToArray());
    }

    [McpServerTool(Name="get_character_knowledge",UseStructuredContent=true),
     Description("Read one owned character's beliefs. DM may read any character holder in the campaign; truth is returned only for facts the holder knows.")]
    public async Task<JsonElement> GetCharacterKnowledge(ClaimsPrincipal user,Guid campaignId,
        Guid characterId,CancellationToken ct)
    {
        await Character(user,campaignId,characterId,ct);
        var state=(await world.GetDmAsync(campaignId,ct)).State;
        return Json(state.Knowledge.Where(x=>x.Holder.Kind==WorldEntityKind.Character &&
            x.Holder.Id==characterId).OrderBy(x=>x.FactId).Take(100).Select(x=>new ContextKnowledge(
                x.FactId,x.Status,x.Confidence,x.BeliefValue ??
                    (x.Status==KnowledgeStatus.Known
                        ? state.Facts.Single(f=>f.Id==x.FactId).Value : null),
                x.AcquiredGameSeconds)).ToArray());
    }

    [McpServerTool(Name="get_recent_events",UseStructuredContent=true),
     Description("Read at most 20 audit events after a sequence. DM sees full events; players see only event metadata for their owned character.")]
    public async Task<JsonElement> GetRecentEvents(ClaimsPrincipal user,Guid campaignId,
        long after=0,Guid? characterId=null,CancellationToken ct=default)
    {
        var role=await access.RequireMemberAsync(user,campaignId,ct);
        if (after<0) throw new RuleViolation("Invalid event cursor.");
        if (role==CampaignRole.Dm)
            return Json(await campaigns.EventsAsync(campaignId,after,20,ct));
        if (characterId is not { } owned) throw new AccessDeniedException();
        await Character(user,campaignId,owned,ct);
        var events=await world.GetPublicTimelineAsync(campaignId,after,100,ct);
        return Json(events.Where(x=>x.CharacterId==owned).Take(20).Select(x=>new {
            x.Sequence,x.EventId,x.Type,x.OccurredAtUtc,x.CharacterId }).ToArray());
    }

    [McpServerTool(Name="get_relevant_history",UseStructuredContent=true),
     Description("Read a bounded event page from the requested sequence. Player results remain limited to owned-character metadata.")]
    public Task<JsonElement> GetRelevantHistory(ClaimsPrincipal user,Guid campaignId,
        long after=0,Guid? characterId=null,CancellationToken ct=default) =>
        GetRecentEvents(user,campaignId,after,characterId,ct);

    [McpServerTool(Name="get_dm_context",UseStructuredContent=true),
     Description("Assemble bounded authoritative context with explicit truncation. DM may see secrets; player context is filtered and requires ownership for a selected character.")]
    public async Task<JsonElement> GetDmContext(ClaimsPrincipal user,Guid campaignId,
        Guid? characterId=null,Guid? locationId=null,long afterEvent=0,
        CancellationToken ct=default) => Json(await context.AssembleAsync(user,campaignId,
            characterId,locationId,afterEvent,ct));

    [McpServerTool(Name="make_skill_check",UseStructuredContent=true),
     Description("Roll an owned character's supported skill check. Requires a stable operationId; replay returns the original roll.")]
    public async Task<JsonElement> MakeSkillCheck(ClaimsPrincipal user,Guid campaignId,
        Guid characterId,SkillCheckRequest request,Guid operationId,CancellationToken ct)
    {
        await Character(user,campaignId,characterId,ct);
        return await Run(user,operationId,"make_skill_check",new { campaignId,characterId,request },
            token=>mechanics.SkillCheckAsync(characterId,request,token),ct);
    }

    [McpServerTool(Name="make_saving_throw",UseStructuredContent=true),
     Description("Roll an owned character's saving throw. Requires stable operationId and replays the original result.")]
    public async Task<JsonElement> MakeSavingThrow(ClaimsPrincipal user,Guid campaignId,
        Guid characterId,CheckRequest request,Guid operationId,CancellationToken ct)
    {
        await Character(user,campaignId,characterId,ct);
        return await Run(user,operationId,"make_saving_throw",new { campaignId,characterId,request },
            token=>mechanics.SavingThrowAsync(characterId,request,token),ct);
    }

    [McpServerTool(Name="roll_dice",UseStructuredContent=true),
     Description("Roll a supported dice expression for a campaign member. Requires stable operationId; this standalone roll does not apply game effects.")]
    public async Task<JsonElement> RollDice(ClaimsPrincipal user,Guid campaignId,string expression,
        Guid operationId,CancellationToken ct)
    {
        await access.RequireMemberAsync(user,campaignId,ct);
        return await Run(user,operationId,"roll_dice",new { campaignId,expression },
            _=>Task.FromResult(dice.Roll(DiceExpression.Parse(expression))),ct);
    }

    [McpServerTool(Name="start_encounter",UseStructuredContent=true),
     Description("DM only: start an existing prepared encounter after initiative. Does not create or populate an encounter; requires stable operationId.")]
    public async Task<JsonElement> StartEncounter(ClaimsPrincipal user,Guid campaignId,
        Guid encounterId,StartCombat request,Guid operationId,CancellationToken ct)
    {
        await access.RequireDmAsync(user,campaignId,ct);
        await Encounter(user,campaignId,encounterId,null,ct);
        return await Run(user,operationId,"start_encounter",new { campaignId,encounterId,request },
            token=>combat.StartAsync(encounterId,request,token),ct);
    }

    [McpServerTool(Name="perform_attack",UseStructuredContent=true),
     Description("Perform a supported weapon attack on the current turn. A declared defender reaction requires control of that defender and the current encounter revision.")]
    public async Task<JsonElement> PerformAttack(ClaimsPrincipal user,Guid campaignId,
        Guid encounterId,AttackCombatant request,Guid operationId,CancellationToken ct)
    {
        var view=await Encounter(user,campaignId,encounterId,request.CombatantId,ct);
        if (request.Reaction is not null && request.Attack is not null)
            await RequireReactionControl(user,campaignId,view,request.Attack.TargetId,ct);
        return await Run(user,operationId,"perform_attack",new { campaignId,encounterId,request },
            token=>combat.AttackAsync(encounterId,request,token),ct);
    }

    [McpServerTool(Name="cast_spell",UseStructuredContent=true),
     Description("Cast an already supported combat spell on the current turn. A declared defender reaction requires control of that defender.")]
    public async Task<JsonElement> CastSpell(ClaimsPrincipal user,Guid campaignId,
        Guid encounterId,CastCombatSpell request,Guid operationId,CancellationToken ct)
    {
        var view=await Encounter(user,campaignId,encounterId,request.CombatantId,ct);
        if (request.Reaction is not null && request.Targets is { Length: 1 })
            await RequireReactionControl(user,campaignId,view,request.Targets[0].CombatantId,ct);
        return await Run(user,operationId,"cast_spell",new { campaignId,encounterId,request },
            token=>combat.CastSpellAsync(encounterId,request,token),ct);
    }

    [McpServerTool(Name="use_item",UseStructuredContent=true),
     Description("Use a supported consumable in an active encounter; requires actor ownership, current turn, expected revision and stable operationId.")]
    public async Task<JsonElement> UseItem(ClaimsPrincipal user,Guid campaignId,
        Guid encounterId,UseCombatItem request,Guid operationId,CancellationToken ct)
    {
        await Encounter(user,campaignId,encounterId,request.CombatantId,ct);
        return await Run(user,operationId,"use_item",new { campaignId,encounterId,request },
            token=>combat.UseItemAsync(encounterId,request,token),ct);
    }

    [McpServerTool(Name="end_turn",UseStructuredContent=true),
     Description("End an owned combatant's current turn, or DM-controlled turn. Requires stable operationId.")]
    public async Task<JsonElement> EndTurn(ClaimsPrincipal user,Guid campaignId,
        Guid encounterId,CombatActor request,Guid operationId,CancellationToken ct)
    {
        await Encounter(user,campaignId,encounterId,request.CombatantId,ct);
        return await Run(user,operationId,"end_turn",new { campaignId,encounterId,request },
            token=>combat.EndTurnAsync(encounterId,request,token),ct);
    }

    [McpServerTool(Name="complete_encounter",UseStructuredContent=true),
     Description("DM only: complete an encounter using the engine's supported outcome and expected revision. Requires stable operationId.")]
    public async Task<JsonElement> CompleteEncounter(ClaimsPrincipal user,Guid campaignId,
        Guid encounterId,CompleteEncounter request,Guid operationId,CancellationToken ct)
    {
        await access.RequireDmAsync(user,campaignId,ct);
        await Encounter(user,campaignId,encounterId,null,ct);
        return await Run(user,operationId,"complete_encounter",new { campaignId,encounterId,request },
            token=>combat.CompleteAsync(encounterId,request,token),ct);
    }

    [McpServerTool(Name="apply_world_changes",UseStructuredContent=true),
     Description("DM only: atomically apply 1–50 validated narrative changes at expected world revision, with explicit cause. Requires stable operationId.")]
    public async Task<JsonElement> ApplyWorldChanges(ClaimsPrincipal user,Guid campaignId,
        ApplyWorldChanges request,Guid operationId,CancellationToken ct)
    {
        await access.RequireDmAsync(user,campaignId,ct);
        return await Run(user,operationId,"apply_world_changes",new { campaignId,request },
            token=>world.ApplyAsync(campaignId,request,token),ct);
    }

    [McpServerTool(Name="update_npc",UseStructuredContent=true),
     Description("DM only: update an existing narrative NPC via Phase 6 validation. Requires expected world revision, cause and stable operationId.")]
    public Task<JsonElement> UpdateNpc(ClaimsPrincipal user,Guid campaignId,NarrativeNpc npc,
        long expectedRevision,string cause,Guid operationId,CancellationToken ct) =>
        ApplyWorldChanges(user,campaignId,new(expectedRevision,cause,
            [new(WorldChangeKind.UpdateNpc,Npc:npc)]),operationId,ct);

    [McpServerTool(Name="update_relationship",UseStructuredContent=true),
     Description("DM only: set a directional relationship using validated dimensions. Requires expected world revision, cause and stable operationId.")]
    public Task<JsonElement> UpdateRelationship(ClaimsPrincipal user,Guid campaignId,
        WorldRelationship relationship,long expectedRevision,string cause,Guid operationId,
        CancellationToken ct) => ApplyWorldChanges(user,campaignId,new(expectedRevision,cause,
            [new(WorldChangeKind.SetRelationship,Relationship:relationship)]),operationId,ct);

    [McpServerTool(Name="grant_knowledge",UseStructuredContent=true),
     Description("DM only: give one holder a known, suspected or believed fact without changing world truth. Requires expected world revision, cause and stable operationId.")]
    public Task<JsonElement> GrantKnowledge(ClaimsPrincipal user,Guid campaignId,
        KnowledgeRecord knowledge,long expectedRevision,string cause,Guid operationId,
        CancellationToken ct) => ApplyWorldChanges(user,campaignId,new(expectedRevision,cause,
            [new(WorldChangeKind.GrantKnowledge,Knowledge:knowledge)]),operationId,ct);

    [McpServerTool(Name="update_quest",UseStructuredContent=true),
     Description("DM only: update quest status/objectives through validated transitions. Requires expected world revision, cause and stable operationId.")]
    public Task<JsonElement> UpdateQuest(ClaimsPrincipal user,Guid campaignId,
        WorldQuest quest,long expectedRevision,string cause,Guid operationId,CancellationToken ct) =>
        ApplyWorldChanges(user,campaignId,new(expectedRevision,cause,
            [new(WorldChangeKind.UpdateQuest,Quest:quest)]),operationId,ct);

    [McpServerTool(Name="get_pending_consequences",UseStructuredContent=true),
     Description("DM only: list bounded eligible mechanical events and unresolved narrative proposals. No event causes automatic world changes.")]
    public async Task<JsonElement> GetPendingConsequences(ClaimsPrincipal user,
        Guid campaignId,long after=0,int limit=20,CancellationToken ct=default)
    {
        await access.RequireDmAsync(user,campaignId,ct);
        return Json(await consequences.PendingAsync(campaignId,after,limit,ct));
    }

    [McpServerTool(Name="propose_narrative_consequence",UseStructuredContent=true),
     Description("DM only: create or revise an unresolved typed world-change proposal for one eligible mechanical event. No world change occurs yet. Requires stable operationId.")]
    public async Task<JsonElement> ProposeNarrativeConsequence(ClaimsPrincipal user,
        Guid campaignId,Guid sourceEventId,ApplyWorldChanges proposal,
        Guid operationId,CancellationToken ct)
    {
        await access.RequireDmAsync(user,campaignId,ct);
        return await Run(user,operationId,"propose_narrative_consequence",
            new { campaignId,sourceEventId,proposal },
            token=>consequences.ProposeAsync(campaignId,sourceEventId,proposal,token),ct);
    }

    [McpServerTool(Name="record_narrative_consequence",UseStructuredContent=true),
     Description("DM only: apply or dismiss the exact proposal identified by its reviewToken. If the proposal changed, reread it before resolving. Requires stable operationId.")]
    public async Task<JsonElement> RecordNarrativeConsequence(ClaimsPrincipal user,
        Guid campaignId,Guid sourceEventId,bool apply,string reviewToken,
        Guid operationId,CancellationToken ct)
    {
        await access.RequireDmAsync(user,campaignId,ct);
        return await Run(user,operationId,"record_narrative_consequence",
            new { campaignId,sourceEventId,apply,reviewToken },
            token=>consequences.ResolveAsync(campaignId,sourceEventId,apply,reviewToken,token),ct);
    }
}
