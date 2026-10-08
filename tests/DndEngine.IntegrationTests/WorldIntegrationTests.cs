using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.World;
using DndEngine.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndEngine.IntegrationTests;

public sealed class WorldIntegrationTests
{
    private static ServiceProvider Provider(string path) => new ServiceCollection().AddLogging()
        .AddDndEngine(path).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true });
    private static NarrativeNpc Npc(Guid id,string name,Guid? location=null,bool visible=false,
        Guid? mechanical=null) => new(id,name,$"{name} description","dark cloak",
        "former courier","cautious","find the relic","exposure","protect a friend",
        location,PublicInformation:"local guide",PrivateInformation:"hides a cipher",
        MechanicalCharacterId:mechanical,IsPublic:visible);
    private static WorldChange C(WorldLocation x) => new(WorldChangeKind.CreateLocation,Location:x);

    [Fact]
    public async Task WorldConsequencesKnowledgeAndQuestsPersistWithoutLeakingSecrets()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.WorldTests",Guid.NewGuid().ToString("N"));
        var root=Guid.NewGuid(); var region=Guid.NewGuid(); var town=Guid.NewGuid();
        var mira=Guid.NewGuid(); var spy=Guid.NewGuid(); var faction=Guid.NewGuid();
        var fact=Guid.NewGuid(); var quest=Guid.NewGuid(); var first=Guid.NewGuid();
        var second=Guid.NewGuid(); Guid campaignId;
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            Assert.False(services.GetRequiredService<CampaignDbContext>().Database.HasPendingModelChanges());
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("World campaign"));
            campaignId=campaign.Id;
            var hero=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaignId,
                "Hero",1,Enum.GetValues<Ability>().ToDictionary(x=>x,_=>12),[],[],12));
            var world=services.GetRequiredService<WorldService>();
            var truth=new WorldValue("Vaelaris","possesses","the amber relic");
            var original=await world.ApplyAsync(campaignId,new(0,"Session zero",
            [
                C(new(root,"Elda","The known world",LocationKind.World,IsDiscovered:true)),
                C(new(region,"Westreach","Western realm",LocationKind.Region,root,IsDiscovered:true)),
                C(new(town,"Duskford","River town",LocationKind.Settlement,region,IsDiscovered:true)),
                new(WorldChangeKind.CreateNpc,Npc:Npc(mira,"Mira",town,true,hero.Id)),
                new(WorldChangeKind.CreateNpc,Npc:Npc(spy,"Spy",town)),
                new(WorldChangeKind.CreateFaction,Faction:new(faction,"Lantern Court","A court",
                    "protect the town","duty","active",mira,town,35,true)),
                new(WorldChangeKind.SetMembership,Membership:new(Guid.NewGuid(),faction,
                    new(WorldEntityKind.Npc,mira),"captain")),
                new(WorldChangeKind.SetRelationship,Relationship:new(Guid.NewGuid(),
                    new(WorldEntityKind.Npc,mira),new(WorldEntityKind.Character,hero.Id),
                    3,2,1,0,1,-2,"trusted ally","saved the bridge")),
                new(WorldChangeKind.SetRelationship,Relationship:new(Guid.NewGuid(),
                    new(WorldEntityKind.Character,hero.Id),new(WorldEntityKind.Npc,mira),
                    1,3,0,0,0,0,"respected guide","knows the road")),
                new(WorldChangeKind.EstablishFact,Fact:new(fact,"artifact-holder",truth,
                    "witnessed by the wizard",WorldVisibility.Secret)),
                new(WorldChangeKind.GrantKnowledge,Knowledge:new(Guid.NewGuid(),fact,
                    new(WorldEntityKind.Npc,mira),KnowledgeStatus.Known,100,null,0,"saw the relic")),
                new(WorldChangeKind.GrantKnowledge,Knowledge:new(Guid.NewGuid(),fact,
                    new(WorldEntityKind.Npc,spy),KnowledgeStatus.Believed,45,
                    new("Vaelaris","possesses","a false map"),0,"rumor")),
                new(WorldChangeKind.CreateQuest,Quest:new(quest,"Find the relic","Investigate the relic",
                    QuestStatus.Discovered,[new(first,"Meet Mira",false),
                        new(second,"Find the vault",false,[first])],
                    RelatedNpcIds:[mira],RelatedFactionIds:[faction],RelatedLocationIds:[town],
                    IsPublic:true))
            ]));
            Assert.Equal(1,original.State.Revision);
            Assert.Equal(hero.Id,original.State.Npcs.Single(x=>x.Id==mira).MechanicalCharacterId);
            Assert.Single(original.State.Memberships);
            Assert.Equal(2,original.State.Relationships.Length);
            Assert.NotEqual(original.State.Relationships[0].From,
                original.State.Relationships[1].From);
            Assert.Single(original.State.Facts);
            Assert.Equal("the amber relic",original.State.Facts.Single().Value.Object);
            Assert.Equal("a false map",(await world.GetKnowledgeAsync(campaignId,
                WorldEntityKind.Npc,spy)).Single().BeliefValue!.Object);
            Assert.Equal("the amber relic",(await world.GetDmAsync(campaignId)).State.Facts.Single().Value.Object);
            var publicView=await world.GetPublicAsync(campaignId);
            Assert.Single(publicView.Npcs); Assert.Empty(publicView.Facts);
            Assert.Single(publicView.Quests);
            Assert.DoesNotContain(await world.GetPublicTimelineAsync(campaignId),
                x=>x.Type is "FactEstablished" or "KnowledgeAcquired");
            Assert.DoesNotContain("hides a cipher",System.Text.Json.JsonSerializer.Serialize(publicView));
            Assert.DoesNotContain("false map",System.Text.Json.JsonSerializer.Serialize(publicView));

            var campaignNow=await services.GetRequiredService<CampaignService>().GetAsync(campaignId);
            await services.GetRequiredService<CampaignService>().AdvanceTimeAsync(campaignId,
                new(120,campaignNow.Revision));
            var active=original.State.Quests.Single() with { Status=QuestStatus.Active };
            var done=active with { Status=QuestStatus.Completed,
                Objectives=[new(first,"Meet Mira",true),new(second,"Find the vault",true,[first])] };
            var changed=await world.ApplyAsync(campaignId,new(1,"After the duel",
            [
                new(WorldChangeKind.UpdateNpc,Npc:original.State.Npcs.Single(x=>x.Id==mira)
                    with { Status=NpcStatus.Dead }),
                new(WorldChangeKind.UpdateFaction,Faction:original.State.Factions.Single()
                    with { LeaderNpcId=null }),
                new(WorldChangeKind.UpdateQuest,Quest:active),
                new(WorldChangeKind.UpdateQuest,Quest:done)
            ]));
            Assert.Equal(120,changed.GameSeconds);
            Assert.Equal(2,changed.State.Revision);
            Assert.Equal(QuestStatus.Completed,changed.State.Quests.Single().Status);
            var events=await world.GetRecentEventsAsync(campaignId);
            Assert.Contains(events,x=>x.Type=="NPCStatusChanged");
            Assert.Contains(events,x=>x.Type=="QuestCompleted" &&
                x.Data.GetProperty("campaignGameSeconds").GetInt64()==120);
            Assert.Equal(12,(await services.GetRequiredService<CharacterService>()
                .GetAsync(hero.Id)).Health.Current);
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var world=scope.ServiceProvider.GetRequiredService<WorldService>();
            Assert.Equal(2,(await world.GetDmAsync(campaignId)).State.Revision);
            Assert.Empty((await world.GetPublicAsync(campaignId)).Facts);
            Assert.Equal(NpcStatus.Dead,(await world.GetDmAsync(campaignId)).State.Npcs
                .Single(x=>x.Id==mira).Status);
        }
    }

    [Fact]
    public async Task InvalidBatchConcurrencyAndSqliteFailureLeaveWorldAndEventsUnchanged()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.WorldSafetyTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Safety"));
        var world=services.GetRequiredService<WorldService>();
        var root=Guid.NewGuid(); var region=Guid.NewGuid();
        await world.ApplyAsync(campaign.Id,new(0,"Map",[
            C(new(root,"Root","A world",LocationKind.World)),
            C(new(region,"Region","A region",LocationKind.Region,root))]));
        var before=await world.GetDmAsync(campaign.Id);
        var eventCount=(await services.GetRequiredService<CampaignService>().EventsAsync(campaign.Id)).Count;
        await Assert.ThrowsAsync<RuleViolation>(()=>world.ApplyAsync(campaign.Id,new(1,"Bad batch",[
            new(WorldChangeKind.CreateNpc,Npc:Npc(Guid.NewGuid(),"Valid")),
            new(WorldChangeKind.UpdateLocation,Location:before.State.Locations.Single(x=>x.Id==root)
                with { ParentId=region })])));
        Assert.Empty((await world.GetDmAsync(campaign.Id)).State.Npcs);
        Assert.Equal(eventCount,(await services.GetRequiredService<CampaignService>()
            .EventsAsync(campaign.Id)).Count);
        await Assert.ThrowsAsync<StateConflictException>(()=>world.ApplyAsync(campaign.Id,
            new(0,"Stale",[new(WorldChangeKind.CreateNpc,Npc:Npc(Guid.NewGuid(),"Stale"))])));

        var store=services.GetRequiredService<IWorldStore>();
        var campaignState=await services.GetRequiredService<ICampaignStore>()
            .GetCampaignAsync(campaign.Id,default);
        var duplicate=(await services.GetRequiredService<CampaignService>()
            .EventsAsync(campaign.Id)).Single(x=>x.Type=="CampaignCreated");
        await Assert.ThrowsAsync<StateConflictException>(()=>store.SaveAsync(before.State,
            before.State with { Revision=2 },campaignState!,[duplicate],default));
        Assert.Equal(1,(await world.GetDmAsync(campaign.Id)).State.Revision);
        Assert.Equal(campaignState!.Revision,(await services.GetRequiredService<ICampaignStore>()
            .GetCampaignAsync(campaign.Id,default))!.Revision);
        Assert.Equal(eventCount,(await services.GetRequiredService<CampaignService>()
            .EventsAsync(campaign.Id)).Count);
    }

    [Fact]
    public async Task SecretDisclosureAndQuestDependenciesRequireExplicitValidTransitions()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.WorldDisclosureTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Disclosure"));
        var world=services.GetRequiredService<WorldService>();
        var fact=new WorldFact(Guid.NewGuid(),"hidden-key",new("Mira","holds","a key"),
            "private source",WorldVisibility.Secret);
        var a=Guid.NewGuid(); var b=Guid.NewGuid();
        await world.ApplyAsync(campaign.Id,new(0,"Known truth",[
            new(WorldChangeKind.EstablishFact,Fact:fact)]));
        await Assert.ThrowsAsync<RuleViolation>(()=>world.ApplyAsync(campaign.Id,new(1,"Shortcut",[
            new(WorldChangeKind.UpdateFact,Fact:fact with { Visibility=WorldVisibility.Public })])));
        await Assert.ThrowsAsync<RuleViolation>(()=>world.ApplyAsync(campaign.Id,new(1,"Invalid quest",[
            new(WorldChangeKind.CreateQuest,Quest:new(Guid.NewGuid(),"Cycle","Bad dependencies",
                QuestStatus.Discovered,[new(a,"A",false,[b]),new(b,"B",false,[a])]))])));
        Assert.Empty((await world.GetPublicAsync(campaign.Id)).Facts);
        Assert.Equal(1,(await world.GetDmAsync(campaign.Id)).State.Revision);
        await world.ApplyAsync(campaign.Id,new(1,"Public revelation",[
            new(WorldChangeKind.DiscloseSecret,Fact:fact with { Visibility=WorldVisibility.Public })]));
        var safe=await world.GetPublicAsync(campaign.Id);
        Assert.Single(safe.Facts);
        Assert.Equal("a key",safe.Facts.Single().Value.Object);
        Assert.DoesNotContain("private source",System.Text.Json.JsonSerializer.Serialize(safe));
        Assert.Contains(await world.GetRecentEventsAsync(campaign.Id),x=>x.Type=="SecretDisclosed");

        var prerequisite=new WorldQuest(Guid.NewGuid(),"First quest","Prerequisite",
            QuestStatus.Unknown,[]);
        var dependent=new WorldQuest(Guid.NewGuid(),"Second quest","Requires first",
            QuestStatus.Discovered,[],[prerequisite.Id]);
        await world.ApplyAsync(campaign.Id,new(2,"Create linked quests",[
            new(WorldChangeKind.CreateQuest,Quest:prerequisite),
            new(WorldChangeKind.CreateQuest,Quest:dependent)]));
        await Assert.ThrowsAsync<RuleViolation>(()=>world.ApplyAsync(campaign.Id,new(3,
            "Premature activation",[new(WorldChangeKind.UpdateQuest,Quest:dependent with {
                Status=QuestStatus.Active })])));
        await world.ApplyAsync(campaign.Id,new(3,"Finish prerequisite",[
            new(WorldChangeKind.UpdateQuest,Quest:prerequisite with { Status=QuestStatus.Discovered }),
            new(WorldChangeKind.UpdateQuest,Quest:prerequisite with { Status=QuestStatus.Active }),
            new(WorldChangeKind.UpdateQuest,Quest:prerequisite with { Status=QuestStatus.Completed })]));
        await world.ApplyAsync(campaign.Id,new(4,"Activate dependent",[
            new(WorldChangeKind.UpdateQuest,Quest:dependent with { Status=QuestStatus.Active })]));
        Assert.Single((await world.GetDmAsync(campaign.Id)).State.Quests,
            x=>x.Id==dependent.Id && x.Status==QuestStatus.Active);
    }

    [Fact]
    public async Task ConcurrentWorldCommandsAcceptOneRevisionAndOneEvent()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.WorldRaceTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        Guid campaignId;
        await using(var setup=provider.CreateAsyncScope())
            campaignId=(await setup.ServiceProvider.GetRequiredService<CampaignService>()
                .CreateAsync(new("Race"))).Id;
        async Task<bool> Attempt(int index)
        {
            await using var scope=provider.CreateAsyncScope();
            try
            {
                await scope.ServiceProvider.GetRequiredService<WorldService>()
                    .ApplyAsync(campaignId,new(0,$"Writer {index}",[
                        new(WorldChangeKind.CreateNpc,Npc:Npc(Guid.NewGuid(),$"Writer {index}"))]));
                return true;
            }
            catch(StateConflictException) { return false; }
        }
        var outcomes=await Task.WhenAll(Attempt(1),Attempt(2));
        Assert.Single(outcomes,x=>x);
        await using var verify=provider.CreateAsyncScope();
        var world=verify.ServiceProvider.GetRequiredService<WorldService>();
        Assert.Equal(1,(await world.GetDmAsync(campaignId)).State.Revision);
        Assert.Single((await world.GetDmAsync(campaignId)).State.Npcs);
        Assert.Single(await world.GetRecentEventsAsync(campaignId));
    }
}
