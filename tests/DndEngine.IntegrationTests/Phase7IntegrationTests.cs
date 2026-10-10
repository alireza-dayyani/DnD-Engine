extern alias mcp;
using System.Security.Claims;
using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Progression;
using DndEngine.Domain.World;
using DndEngine.Infrastructure;
using CampaignMcpTools = mcp::DndEngine.Mcp.CampaignMcpTools;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndEngine.IntegrationTests;

public sealed class Phase7IntegrationTests
{
    private static ServiceProvider Provider(string path) => new ServiceCollection().AddLogging()
        .AddDndEngine(path).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true });
    private static ClaimsPrincipal Principal(Guid subject) => new(new ClaimsIdentity(
        [new Claim("sub",subject.ToString("D"))],"test"));

    [Fact]
    public async Task PlayerCannotDeclareAnotherCharactersReaction()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.McpReaction",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Reactions"));
        var characters=services.GetRequiredService<CharacterService>();
        CreateCharacter Sheet(string name)=>new(campaign.Id,name,1,
            Enum.GetValues<Ability>().ToDictionary(x=>x,_=>12),[],[],20);
        var attacker=await characters.CreateAsync(Sheet("Attacker"));
        var defender=await characters.CreateAsync(Sheet("Defender"));
        var combat=services.GetRequiredService<CombatService>();
        var capabilities=new CombatCapabilities(30,["longsword"],[],[],[],[]);
        await combat.ImportAsync(attacker.Id,capabilities);
        await combat.ImportAsync(defender.Id,capabilities);
        var sword=await combat.GrantWeaponAsync(attacker.Id,new("longsword"));
        var encounter=await combat.CreateAsync(campaign.Id,new("Duel"));
        var actor=await combat.AddAsync(encounter.Id,new(attacker.Id));
        var target=await combat.AddAsync(encounter.Id,new(defender.Id));
        var subject=Guid.NewGuid();
        var db=services.GetRequiredService<CampaignDbContext>();
        db.CampaignAccess.Add(new CampaignAccessRow {
            CampaignId=campaign.Id,SubjectId=subject,Role="Player" });
        db.CharacterOwnership.Add(new CharacterOwnershipRow {
            CampaignId=campaign.Id,SubjectId=subject,CharacterId=attacker.Id });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var tools=ActivatorUtilities.CreateInstance<CampaignMcpTools>(services);
        var reaction=new HellishRebukeReaction(SpellSlotPoolKind.PactMagic,1,true,true);
        var attack=new AttackCombatant(actor.Result.Id,
            new(target.Result.Id,sword.Id,AttackMode.Melee,new(5,true,true)),reaction);
        await Assert.ThrowsAsync<AccessDeniedException>(()=>tools.PerformAttack(
            Principal(subject),campaign.Id,encounter.Id,attack,Guid.NewGuid(),default));
        var spell=new CastCombatSpell(actor.Result.Id,"warlock","eldritch-blast",null,0,
            [new(target.Result.Id,5,true)],true,true,true,encounter.Revision,
            Reaction:reaction);
        await Assert.ThrowsAsync<AccessDeniedException>(()=>tools.CastSpell(
            Principal(subject),campaign.Id,encounter.Id,spell,Guid.NewGuid(),default));
        Assert.Empty(await db.IdempotencyOperations.ToArrayAsync());
    }

    [Fact]
    public async Task ExistingPhase6CampaignUpgradesWithoutLosingWorldState()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.McpUpgrade",
            Guid.NewGuid().ToString("N"));
        Guid campaignId; Guid factId=Guid.NewGuid();
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var db=services.GetRequiredService<CampaignDbContext>();
            await db.Database.MigrateAsync("20261008230000_Phase6WorldState");
            Assert.Equal(6,(await db.Database.GetAppliedMigrationsAsync()).Count());
            campaignId=(await services.GetRequiredService<CampaignService>()
                .CreateAsync(new("Older world"))).Id;
            await services.GetRequiredService<WorldService>().ApplyAsync(campaignId,
                new(0,"Established before MCP",[
                    new(WorldChangeKind.EstablishFact,Fact:new(factId,"old-truth",
                        new("Tomb","contains","silver seal"),"archivist",WorldVisibility.Secret))]));
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var db=services.GetRequiredService<CampaignDbContext>();
            Assert.Equal(8,(await db.Database.GetAppliedMigrationsAsync()).Count());
            Assert.False(db.Database.HasPendingModelChanges());
            var state=(await services.GetRequiredService<WorldService>()
                .GetDmAsync(campaignId)).State;
            Assert.Equal(factId,Assert.Single(state.Facts).Id);
            Assert.Equal("silver seal",state.Facts[0].Value.Object);
        }
    }

    [Fact]
    public async Task AccessAndContextKeepCrossCampaignAndUnknownSecretsHidden()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.McpAccess",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaigns=services.GetRequiredService<CampaignService>();
        var a=await campaigns.CreateAsync(new("A")); var b=await campaigns.CreateAsync(new("B"));
        var owner=Guid.NewGuid(); var dm=Guid.NewGuid(); var stranger=Guid.NewGuid();
        var hero=await services.GetRequiredService<CharacterService>().CreateAsync(new(a.Id,
            "Hero",1,Enum.GetValues<Ability>().ToDictionary(x=>x,_=>12),[],[],12));
        var other=await services.GetRequiredService<CharacterService>().CreateAsync(new(a.Id,
            "Other",1,Enum.GetValues<Ability>().ToDictionary(x=>x,_=>12),[],[],12));
        var db=services.GetRequiredService<CampaignDbContext>();
        db.CampaignAccess.AddRange(
            new CampaignAccessRow { CampaignId=a.Id,SubjectId=owner,Role="Player" },
            new CampaignAccessRow { CampaignId=a.Id,SubjectId=dm,Role="Dm" },
            new CampaignAccessRow { CampaignId=b.Id,SubjectId=stranger,Role="Dm" });
        db.CharacterOwnership.Add(new CharacterOwnershipRow {
            CharacterId=hero.Id,CampaignId=a.Id,SubjectId=owner });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var access=services.GetRequiredService<CampaignAccessService>();
        Assert.Equal(CampaignRole.Player,await access.RequireMemberAsync(Principal(owner),a.Id));
        await Assert.ThrowsAsync<AccessDeniedException>(()=>access.RequireDmAsync(Principal(owner),a.Id));
        await Assert.ThrowsAsync<AccessDeniedException>(()=>access.RequireMemberAsync(Principal(owner),b.Id));
        await Assert.ThrowsAsync<AccessDeniedException>(()=>access.RequireCharacterAsync(
            Principal(owner),a.Id,other.Id));
        await Assert.ThrowsAsync<AccessDeniedException>(()=>access.RequireMemberAsync(
            Principal(stranger),a.Id));
        await access.RequireCharacterAsync(Principal(dm),a.Id,other.Id);

        var hidden=Guid.NewGuid(); var known=Guid.NewGuid();
        await services.GetRequiredService<WorldService>().ApplyAsync(a.Id,new(0,"Secret setup",[
            new(WorldChangeKind.EstablishFact,Fact:new(hidden,"hidden-holder",
                new("Wizard","hides","amber relic"),"private witness",WorldVisibility.Secret)),
            new(WorldChangeKind.EstablishFact,Fact:new(known,"known-key",
                new("Hero","holds","blue key"),"private witness",WorldVisibility.Secret)),
            new(WorldChangeKind.GrantKnowledge,Knowledge:new(Guid.NewGuid(),known,
                new(WorldEntityKind.Character,hero.Id),KnowledgeStatus.Known,100,null,0,"saw it"))]));
        var context=services.GetRequiredService<DmContextService>();
        var player=await context.AssembleAsync(Principal(owner),a.Id,hero.Id);
        var playerJson=JsonSerializer.Serialize(player);
        Assert.DoesNotContain("amber relic",playerJson);
        Assert.DoesNotContain("private witness",playerJson);
        Assert.Contains("blue key",playerJson);
        Assert.Empty(player.Facts);
        Assert.Single(player.Events);
        Assert.DoesNotContain("Data",JsonSerializer.Serialize(player.Events));
        var dmContext=await context.AssembleAsync(Principal(dm),a.Id);
        Assert.Contains("amber relic",JsonSerializer.Serialize(dmContext));
        await Assert.ThrowsAsync<AccessDeniedException>(()=>context.AssembleAsync(
            Principal(owner),a.Id,other.Id));

        var largeNpcs=Enumerable.Range(0,12).Select(i=>new WorldChange(
            WorldChangeKind.CreateNpc,Npc:new NarrativeNpc(Guid.NewGuid(),
                $"Witness {i}",new string('x',1900),"coat","traveler","calm",
                "safety","darkness","home",IsPublic:true))).ToArray();
        await services.GetRequiredService<WorldService>().ApplyAsync(a.Id,
            new(1,"Populate witnesses",largeNpcs));
        var bounded=await context.AssembleAsync(Principal(owner),a.Id,hero.Id);
        Assert.True(bounded.Truncation.Single(x=>x.Section=="npcs").Clipped>0);
        Assert.Equal(10,bounded.Npcs.Length);
        Assert.True(JsonSerializer.Serialize(bounded).Length<32_000);
        Assert.DoesNotContain("amber relic",JsonSerializer.Serialize(bounded));
    }

    [Fact]
    public async Task DirectCommandBoundaryReplaysAfterRestartAndRollsBackFailures()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.McpReplay",Guid.NewGuid().ToString("N"));
        var subject=Guid.NewGuid(); var operation=Guid.NewGuid(); var npcId=Guid.NewGuid();
        Guid campaignId; string first;
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            campaignId=(await services.GetRequiredService<CampaignService>().CreateAsync(new("Replay"))).Id;
            var runner=services.GetRequiredService<DurableMcpCommandRunner>();
            var world=services.GetRequiredService<WorldService>();
            var command=new ApplyWorldChanges(0,"Introduce guide",[
                new(WorldChangeKind.CreateNpc,Npc:new(npcId,"Guide","A guide","cloak",
                    "courier","careful","help","danger","guide"))]);
            first=await runner.RunAsync(operation,subject,"apply_world_changes",command,
                ct=>world.ApplyAsync(campaignId,command,ct));
            Assert.Equal(1,(await world.GetDmAsync(campaignId)).State.Revision);
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var runner=services.GetRequiredService<DurableMcpCommandRunner>();
            var world=services.GetRequiredService<WorldService>();
            var same=new ApplyWorldChanges(0,"Introduce guide",[
                new(WorldChangeKind.CreateNpc,Npc:new(npcId,"Guide","A guide","cloak",
                    "courier","careful","help","danger","guide"))]);
            Assert.Equal(first,await runner.RunAsync(operation,subject,"apply_world_changes",same,
                ct=>world.ApplyAsync(campaignId,same,ct)));
            await Assert.ThrowsAsync<StateConflictException>(()=>runner.RunAsync(operation,subject,
                "apply_world_changes",new { different=true },_=>Task.FromResult("bad")));
            var failedId=Guid.NewGuid();
            var invalid=new ApplyWorldChanges(1,"Invalid location",[
                new(WorldChangeKind.UpdateNpc,Npc:new(npcId,"Guide","A guide","cloak",
                    "courier","careful","help","danger","guide",Guid.NewGuid()))]);
            await Assert.ThrowsAsync<RuleViolation>(()=>runner.RunAsync(failedId,subject,
                "apply_world_changes",invalid,ct=>world.ApplyAsync(campaignId,invalid,ct)));
            Assert.Null(await services.GetRequiredService<CampaignDbContext>()
                .IdempotencyOperations.FindAsync(failedId));
            Assert.Equal(1,(await world.GetDmAsync(campaignId)).State.Revision);
            Assert.Single((await world.GetRecentEventsAsync(campaignId)),x=>x.Type=="NPCIntroduced");
        }
    }

    [Fact]
    public async Task ConcurrentDuplicateCommandReturnsOneCommittedResult()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.McpConcurrent",
            Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        var operation=Guid.NewGuid(); var subject=Guid.NewGuid();
        var entered=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release=new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        await using var firstScope=provider.CreateAsyncScope();
        await using var secondScope=provider.CreateAsyncScope();
        var first=firstScope.ServiceProvider.GetRequiredService<DurableMcpCommandRunner>();
        var second=secondScope.ServiceProvider.GetRequiredService<DurableMcpCommandRunner>();
        var call=first.RunAsync(operation,subject,"roll_dice",new { expression="1d20" },
            async _=>{ entered.SetResult(); await release.Task; return new { roll=17 }; });
        await entered.Task;
        var duplicate=Task.Run(()=>second.RunAsync(operation,subject,"roll_dice",
            new { expression="1d20" },_=>Task.FromResult(new { roll=3 })));
        release.SetResult();
        Assert.Equal(await call,await duplicate);
        await using var verify=provider.CreateAsyncScope();
        Assert.Single(await verify.ServiceProvider.GetRequiredService<CampaignDbContext>()
            .IdempotencyOperations.Where(x=>x.OperationId==operation).ToArrayAsync());
    }

    [Fact]
    public async Task PendingReviewFindsEligibleEventBeyondFirstEventPage()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.McpPending",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Long history"));
        var source=Guid.NewGuid(); var dm=Guid.NewGuid();
        var db=services.GetRequiredService<CampaignDbContext>();
        db.CampaignAccess.Add(new CampaignAccessRow {
            CampaignId=campaign.Id,SubjectId=dm,Role="Dm" });
        db.Events.AddRange(Enumerable.Range(0,510).Select(_=>new EventRow {
            EventId=Guid.NewGuid(),CampaignId=campaign.Id,Type="SkillCheckMade",
            OccurredAtUtc=DateTimeOffset.UtcNow,RulesetId=campaign.Ruleset.Id,
            SrdVersion=campaign.Ruleset.Version,SchemaVersion=1,DataJson="{}" }));
        db.Events.Add(new EventRow { EventId=source,CampaignId=campaign.Id,
            Type="EncounterCompleted",OccurredAtUtc=DateTimeOffset.UtcNow,
            RulesetId=campaign.Ruleset.Id,SrdVersion=campaign.Ruleset.Version,
            SchemaVersion=1,DataJson="{}" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var review=services.GetRequiredService<NarrativeConsequenceService>();
        Assert.Equal(source,Assert.Single(await review.PendingAsync(campaign.Id)).Source.EventId);
        var context=await services.GetRequiredService<DmContextService>()
            .AssembleAsync(Principal(dm),campaign.Id);
        Assert.Single(context.PendingConsequences);
    }

    [Fact]
    public async Task UnresolvedProposalCanBeRevisedAfterWorldChanges()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.McpRevise",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Revise"));
        var source=Guid.NewGuid(); var subject=Guid.NewGuid();
        var db=services.GetRequiredService<CampaignDbContext>();
        db.Events.Add(new EventRow { EventId=source,CampaignId=campaign.Id,
            Type="EncounterCompleted",OccurredAtUtc=DateTimeOffset.UtcNow,
            RulesetId=campaign.Ruleset.Id,SrdVersion=campaign.Ruleset.Version,
            SchemaVersion=1,DataJson="{}" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var review=services.GetRequiredService<NarrativeConsequenceService>();
        var runner=services.GetRequiredService<DurableMcpCommandRunner>();
        var world=services.GetRequiredService<WorldService>();
        var firstNpc=Guid.NewGuid(); var secondNpc=Guid.NewGuid();
        WorldChange Npc(Guid id,string name)=>new(WorldChangeKind.CreateNpc,
            Npc:new(id,name,"A witness","cloak","traveler","calm","help","fear","home"));
        var initial=new ApplyWorldChanges(0,"First reading",[Npc(firstNpc,"Witness")]);
        await runner.RunAsync(Guid.NewGuid(),subject,"propose",initial,
            ct=>review.ProposeAsync(campaign.Id,source,initial,ct));
        var initialToken=Assert.Single(await review.PendingAsync(campaign.Id)).Proposal!.ReviewToken;
        await world.ApplyAsync(campaign.Id,new(0,"Other event",[Npc(secondNpc,"Guide")]));
        await Assert.ThrowsAsync<StateConflictException>(()=>runner.RunAsync(
            Guid.NewGuid(),subject,"resolve",new { source,apply=true },
            ct=>review.ResolveAsync(campaign.Id,source,true,initialToken,ct)));
        var revised=new ApplyWorldChanges(1,"Revised reading",[Npc(firstNpc,"Witness")]);
        await runner.RunAsync(Guid.NewGuid(),subject,"propose",revised,
            ct=>review.ProposeAsync(campaign.Id,source,revised,ct));
        var pending=Assert.Single(await review.PendingAsync(campaign.Id));
        Assert.Equal(1,pending.Proposal!.ExpectedWorldRevision);
        Assert.Equal("Revised reading",pending.Proposal.Cause);
        Assert.NotEqual(initialToken,pending.Proposal.ReviewToken);
        await Assert.ThrowsAsync<StateConflictException>(()=>runner.RunAsync(
            Guid.NewGuid(),subject,"resolve",new { source,apply=true,initialToken },
            ct=>review.ResolveAsync(campaign.Id,source,true,initialToken,ct)));
        await runner.RunAsync(Guid.NewGuid(),subject,"resolve",new { source,apply=true },
            ct=>review.ResolveAsync(campaign.Id,source,true,pending.Proposal.ReviewToken,ct));
        Assert.Equal(2,(await world.GetDmAsync(campaign.Id)).State.Revision);
        Assert.Equal(2,(await world.GetDmAsync(campaign.Id)).State.Npcs.Length);
        await Assert.ThrowsAsync<StateConflictException>(()=>runner.RunAsync(
            Guid.NewGuid(),subject,"propose",revised,
            ct=>review.ProposeAsync(campaign.Id,source,revised,ct)));
    }

    [Fact]
    public async Task ReviewedConsequenceAppliesOnceAndRetainsSource()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.McpConsequence",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Review"));
        var eventId=Guid.NewGuid(); var npcId=Guid.NewGuid(); var subject=Guid.NewGuid();
        var db=services.GetRequiredService<CampaignDbContext>();
        db.Events.Add(new EventRow { EventId=eventId,CampaignId=campaign.Id,
            Type="EncounterCompleted",OccurredAtUtc=DateTimeOffset.UtcNow,
            RulesetId=campaign.Ruleset.Id,SrdVersion=campaign.Ruleset.Version,
            SchemaVersion=1,DataJson="{}" });
        await db.SaveChangesAsync(); db.ChangeTracker.Clear();
        var review=services.GetRequiredService<NarrativeConsequenceService>();
        var runner=services.GetRequiredService<DurableMcpCommandRunner>();
        var proposed=new ApplyWorldChanges(0,"After the battle",[
            new(WorldChangeKind.CreateNpc,Npc:new(npcId,"Witness","A witness","hood",
                "scribe","nervous","truth","capture","testify"))]);
        var proposalId=Guid.NewGuid(); var resolutionId=Guid.NewGuid();
        var proposedJson=await runner.RunAsync(proposalId,subject,"propose",proposed,
            ct=>review.ProposeAsync(campaign.Id,eventId,proposed,ct));
        Assert.Contains("Proposed",proposedJson);
        Assert.Equal(0,(await services.GetRequiredService<WorldService>()
            .GetDmAsync(campaign.Id)).State.Revision);
        var reviewToken=Assert.Single(await review.PendingAsync(campaign.Id)).Proposal!.ReviewToken;
        var appliedJson=await runner.RunAsync(resolutionId,subject,"resolve",
            new { campaign.Id,eventId,apply=true,reviewToken },
            ct=>review.ResolveAsync(campaign.Id,eventId,true,reviewToken,ct));
        Assert.Contains("Applied",appliedJson);
        Assert.Equal(appliedJson,await runner.RunAsync(resolutionId,subject,"resolve",
            new { campaign.Id,eventId,apply=true,reviewToken },
            ct=>review.ResolveAsync(campaign.Id,eventId,true,reviewToken,ct)));
        Assert.Equal(1,(await services.GetRequiredService<WorldService>()
            .GetDmAsync(campaign.Id)).State.Revision);
        Assert.Empty(await review.PendingAsync(campaign.Id));
        await Assert.ThrowsAsync<StateConflictException>(()=>runner.RunAsync(Guid.NewGuid(),subject,
            "resolve",new { campaign.Id,eventId,apply=true,reviewToken },
            ct=>review.ResolveAsync(campaign.Id,eventId,true,reviewToken,ct)));
        Assert.Single((await services.GetRequiredService<WorldService>()
            .GetDmAsync(campaign.Id)).State.Npcs);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
