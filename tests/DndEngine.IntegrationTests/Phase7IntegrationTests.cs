using System.Security.Claims;
using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.World;
using DndEngine.Infrastructure;
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
        Assert.Single(await review.PendingAsync(campaign.Id));
        var appliedJson=await runner.RunAsync(resolutionId,subject,"resolve",
            new { campaign.Id,eventId,apply=true },ct=>review.ResolveAsync(campaign.Id,eventId,true,ct));
        Assert.Contains("Applied",appliedJson);
        Assert.Equal(appliedJson,await runner.RunAsync(resolutionId,subject,"resolve",
            new { campaign.Id,eventId,apply=true },ct=>review.ResolveAsync(campaign.Id,eventId,true,ct)));
        Assert.Equal(1,(await services.GetRequiredService<WorldService>()
            .GetDmAsync(campaign.Id)).State.Revision);
        Assert.Empty(await review.PendingAsync(campaign.Id));
        await Assert.ThrowsAsync<StateConflictException>(()=>runner.RunAsync(Guid.NewGuid(),subject,
            "resolve",new { campaign.Id,eventId,apply=true },
            ct=>review.ResolveAsync(campaign.Id,eventId,true,ct)));
        Assert.Single((await services.GetRequiredService<WorldService>()
            .GetDmAsync(campaign.Id)).State.Npcs);
        Assert.False(db.Database.HasPendingModelChanges());
    }
}
