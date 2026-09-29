using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
namespace DndEngine.IntegrationTests;

public class PersistenceTests
{
    private static string DirectoryPath() => Path.Combine(Path.GetTempPath(),"DndEngine.Tests",Guid.NewGuid().ToString("N"));
    private static ServiceProvider Provider(string directory,params int[] dice) => new ServiceCollection().AddLogging()
        .AddDndEngine(directory).AddSingleton<IDiceRoller>(new FixedDiceRoller(dice)).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true });
    private static async Task<CharacterView> CreateAsync(IServiceProvider scope)
    {
        var campaign=await scope.GetRequiredService<CampaignService>().CreateAsync(new("Persistent campaign"));
        return await scope.GetRequiredService<CharacterService>().CreateAsync(new(campaign.Id,"Vaelaris",5,
            Enum.GetValues<Ability>().ToDictionary(x=>x,_=>18),["deception"],[Ability.Wisdom],20));
    }
    [Fact]
    public async Task CampaignCharacterDamageHealingAndEventsSurviveNewProvider()
    {
        var path=DirectoryPath(); Guid id; Guid campaignId;
        await using(var provider=Provider(path,11,7,16,10))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var c=await CreateAsync(services); id=c.Id; campaignId=c.CampaignId;
            var mechanics=services.GetRequiredService<MechanicsService>();
            var normal=await mechanics.SkillCheckAsync(id,new("deception",15)); Assert.Equal(18,normal.Total);
            var advantage=await mechanics.SkillCheckAsync(id,new("deception",15,Advantage:true)); Assert.Equal(16,advantage.SelectedRoll);
            Assert.True((await mechanics.SavingThrowAsync(id,new(Ability.Wisdom,17))).Success);
            await mechanics.DamageAsync(id,new(7)); await mechanics.HealAsync(id,new(3));
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var c=await services.GetRequiredService<CharacterService>().GetAsync(id);
            Assert.Equal(16,c.Health.Current); Assert.Equal(5,c.Revision); Assert.Equal(18,c.Abilities[Ability.Charisma]);
            Assert.Contains("deception",c.SkillProficiencies); Assert.Contains(Ability.Wisdom,c.SavingThrowProficiencies);
            Assert.Equal(Ruleset.Current,(await services.GetRequiredService<CampaignService>().GetAsync(campaignId)).Ruleset);
            var events=await services.GetRequiredService<CampaignService>().EventsAsync(campaignId);
            Assert.Equal(7,events.Count); Assert.Equal("CharacterHealed",events[^1].Type);
            Assert.All(events,e=>Assert.Equal(Ruleset.Current,e.Ruleset));
            Assert.Equal(18,events[2].Data.GetProperty("total").GetInt32());
            Assert.Equal(2,(await services.GetRequiredService<CampaignService>().EventsAsync(campaignId,events[4].Sequence)).Count);
            Assert.Equal(18,await services.GetRequiredService<RulesDbContext>().Skills.CountAsync());
            Assert.Single(await services.GetRequiredService<CampaignDbContext>().Database.GetAppliedMigrationsAsync());
            Assert.False(services.GetRequiredService<CampaignDbContext>().Database.HasPendingModelChanges());
            Assert.False(services.GetRequiredService<RulesDbContext>().Database.HasPendingModelChanges());
        }
    }
    [Fact]
    public async Task TemporaryHpDeathAndUnconsciousSavingThrowsPersist()
    {
        await using var provider=Provider(DirectoryPath(),20); await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var c=await CreateAsync(services); var mechanics=services.GetRequiredService<MechanicsService>();
        await mechanics.TemporaryHpAsync(c.Id,new(5,true));
        Assert.Equal(5,(await services.GetRequiredService<CharacterService>().GetAsync(c.Id)).Health.Temporary);
        await mechanics.DamageAsync(c.Id,new(25));
        var save=await mechanics.SavingThrowAsync(c.Id,new(Ability.Dexterity,1)); Assert.Empty(save.Rolls); Assert.False(save.Success);
        await mechanics.DeathSaveAsync(c.Id);
        Assert.Equal(1,(await services.GetRequiredService<CharacterService>().GetAsync(c.Id)).Health.Current);
    }
    [Fact]
    public async Task InvalidOperationsDoNotWriteEventsOrState()
    {
        await using var provider=Provider(DirectoryPath()); await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var c=await CreateAsync(services); var mechanics=services.GetRequiredService<MechanicsService>();
        await Assert.ThrowsAsync<RuleViolation>(()=>mechanics.DamageAsync(c.Id,new(-1)));
        await Assert.ThrowsAsync<RuleViolation>(()=>mechanics.SkillCheckAsync(c.Id,new("fake",10)));
        Assert.Equal(20,(await services.GetRequiredService<CharacterService>().GetAsync(c.Id)).Health.Current);
        Assert.Equal(2,(await services.GetRequiredService<CampaignService>().EventsAsync(c.CampaignId)).Count);
    }
    [Fact]
    public async Task StaleWritesCannotLoseHealthOrCreateAuditEvents()
    {
        await using var provider=Provider(DirectoryPath()); await provider.InitializeDndEngineAsync();
        await using var first=provider.CreateAsyncScope(); await using var second=provider.CreateAsyncScope();
        var c=await CreateAsync(first.ServiceProvider);
        var staleStore=second.ServiceProvider.GetRequiredService<ICampaignStore>();
        var stale=(await staleStore.GetCharacterAsync(c.Id,default))!;
        await first.ServiceProvider.GetRequiredService<MechanicsService>().DamageAsync(c.Id,new(7));
        stale.Health.Heal(1);
        var entry=new CampaignEvent(0,Guid.NewGuid(),c.CampaignId,c.Id,"CharacterHealed",DateTimeOffset.UtcNow,Ruleset.Current,1,1,JsonSerializer.SerializeToElement(new{test=true}));
        await Assert.ThrowsAsync<StateConflictException>(()=>staleStore.SaveCharacterAsync(stale,entry,default));
        Assert.Equal(13,(await staleStore.GetCharacterAsync(c.Id,default))!.Health.State.Current);
        Assert.Equal(3,(await staleStore.GetEventsAsync(c.CampaignId,0,100,default)).Count);
    }
    [Fact]
    public async Task AuditInsertFailureRollsBackHealthUpdate()
    {
        await using var provider=Provider(DirectoryPath()); await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var c=await CreateAsync(services); var store=services.GetRequiredService<ICampaignStore>();
        var character=(await store.GetCharacterAsync(c.Id,default))!; character.Health.Damage(7);
        var existing=(await store.GetEventsAsync(c.CampaignId,0,100,default))[0];
        var duplicate=existing with { CharacterId=c.Id,CharacterRevision=1 };
        await Assert.ThrowsAsync<DbUpdateException>(()=>store.SaveCharacterAsync(character,duplicate,default));
        Assert.Equal(20,(await store.GetCharacterAsync(c.Id,default))!.Health.State.Current);
        Assert.Equal(2,(await store.GetEventsAsync(c.CampaignId,0,100,default)).Count);
    }
    [Fact]
    public async Task VersionedContentIsIdempotentAndTamperingFails()
    {
        await using var provider=Provider(DirectoryPath()); await provider.InitializeDndEngineAsync();
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var db=scope.ServiceProvider.GetRequiredService<RulesDbContext>();
        Assert.Equal(18,await db.Skills.CountAsync());
        var skill=await db.Skills.FirstAsync(); skill.Ability="Strength"; skill.Name="Changed"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(()=>RulesCatalog.ImportAsync(db));
    }
}
