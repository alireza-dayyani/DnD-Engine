using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
namespace DndEngine.Application.Tests;

public class UseCaseTests
{
    private readonly MemoryStore store = new();
    private readonly TestCatalog catalog = new();
    private readonly TestClock clock = new();
    private CharacterService Characters => new(store,catalog,clock);
    private CampaignService Campaigns => new(store,catalog,clock);
    private async Task<CharacterView> CreateAsync()
    {
        var campaign=await Campaigns.CreateAsync(new("Test"));
        return await Characters.CreateAsync(Request(campaign.Id));
    }
    private static CreateCharacter Request(Guid campaignId) => new(campaignId,"Vaelaris",5,
        Enum.GetValues<Ability>().ToDictionary(x=>x,_=>18),["deception"],[Ability.Wisdom],20);

    [Fact]
    public async Task SheetImportAndCampaignAreVersionedAndAudited()
    {
        var c=await CreateAsync();
        Assert.Equal(3,c.ProficiencyBonus); Assert.Equal(14,c.ArmorClass);
        Assert.Equal(Ruleset.Current,(await Campaigns.GetAsync(c.CampaignId)).Ruleset);
        Assert.Equal(new[]{"CampaignCreated","CharacterCreated"},store.Events.Select(x=>x.Type));
        Assert.All(store.Events,e=>Assert.Equal(clock.GetUtcNow(),e.OccurredAtUtc));
    }
    [Fact]
    public async Task SkillCheckMatchesExampleAndPersistsExplanation()
    {
        var c=await CreateAsync();
        var result=await new MechanicsService(store,catalog,new FixedDiceRoller(11),clock).SkillCheckAsync(c.Id,new("deception",15));
        Assert.Equal(18,result.Total); Assert.True(result.Success);
        var entry=store.Events.Last();
        Assert.Equal("AbilityCheckMade",entry.Type); Assert.Equal(18,entry.Data.GetProperty("total").GetInt32());
        Assert.Equal("Charisma",entry.Data.GetProperty("ability").GetString());
        Assert.Equal(Ruleset.Current,entry.Ruleset); Assert.Equal(1,entry.CharacterRevision);
    }
    [Fact]
    public async Task AlternateAbilityStillUsesSkillProficiency()
    {
        var c=await CreateAsync();
        var result=await new MechanicsService(store,catalog,new FixedDiceRoller(11),clock)
            .SkillCheckAsync(c.Id,new("deception",18,AbilityOverride:Ability.Strength));
        Assert.Equal(Ability.Strength,result.Ability); Assert.Equal(3,result.ProficiencyModifier);
    }
    [Fact]
    public async Task UnknownSkillFailsWithoutDiceOrEvents()
    {
        var c=await CreateAsync();
        await Assert.ThrowsAsync<RuleViolation>(()=>new MechanicsService(store,catalog,new FixedDiceRoller(),clock)
            .SkillCheckAsync(c.Id,new("unknown",15)));
        Assert.Equal(2,store.Events.Count);
    }
    [Fact]
    public async Task UnknownCharacterAndCampaignFailExplicitly()
    {
        await Assert.ThrowsAsync<NotFoundException>(()=>Characters.GetAsync(Guid.NewGuid()));
        await Assert.ThrowsAsync<NotFoundException>(()=>Characters.CreateAsync(Request(Guid.NewGuid())));
        await Assert.ThrowsAsync<NotFoundException>(()=>Campaigns.EventsAsync(Guid.NewGuid()));
    }
    [Fact]
    public async Task UnsupportedVersionCannotSilentlyRunCurrentRules()
    {
        await Assert.ThrowsAsync<RuleViolation>(()=>Campaigns.CreateAsync(new("Future",SrdVersion:"5.2.2")));
        Assert.Empty(store.Events);
    }
    [Fact]
    public async Task CreationRejectsMissingAbilitiesUnknownSkillsAndExceptionalScores()
    {
        var campaign=await Campaigns.CreateAsync(new("Test"));
        await Assert.ThrowsAsync<RuleViolation>(()=>Characters.CreateAsync(Request(campaign.Id) with { Abilities=[] }));
        await Assert.ThrowsAsync<RuleViolation>(()=>Characters.CreateAsync(Request(campaign.Id) with { SkillProficiencies=["unknown"] }));
        var request=Request(campaign.Id); request.Abilities[Ability.Strength]=21;
        await Assert.ThrowsAsync<RuleViolation>(()=>Characters.CreateAsync(request));
        Assert.Single(store.Events);
    }
    [Fact]
    public async Task DuplicateProficienciesFail()
    {
        var campaign=await Campaigns.CreateAsync(new("Test"));
        await Assert.ThrowsAsync<RuleViolation>(()=>Characters.CreateAsync(Request(campaign.Id) with { SkillProficiencies=["deception","deception"] }));
    }
    [Fact]
    public async Task DamageAndHealingRecordBeforeAndAfter()
    {
        var c=await CreateAsync(); var service=new MechanicsService(store,catalog,new FixedDiceRoller(),clock);
        var damaged=await service.DamageAsync(c.Id,new(7)); var healed=await service.HealAsync(c.Id,new(3));
        Assert.Equal(13,damaged.Change.After.Current); Assert.Equal(16,healed.Change.After.Current);
        Assert.Equal("CharacterHealed",store.Events.Last().Type);
    }
    [Fact]
    public async Task EventCursorValidationRejectsBadRequests()
    {
        var c=await CreateAsync();
        await Assert.ThrowsAsync<RuleViolation>(()=>Campaigns.EventsAsync(c.CampaignId,-1));
        await Assert.ThrowsAsync<RuleViolation>(()=>Campaigns.EventsAsync(c.CampaignId,0,501));
    }
    private sealed class TestClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow()=>new(2026,9,29,0,0,0,TimeSpan.Zero);
    }
    private sealed class TestCatalog : IRulesCatalog
    {
        public Task RequireRulesetAsync(Ruleset ruleset,CancellationToken ct) { ruleset.RequireSupported(); return Task.CompletedTask; }
        public Task<SkillDefinition> GetSkillAsync(Ruleset ruleset,string id,CancellationToken ct)=>id=="deception"
            ? Task.FromResult(new SkillDefinition(id,"Deception",Ability.Charisma,"SRD-5.2.1 p.9"))
            : throw new RuleViolation("Unknown skill.");
    }
    private sealed class MemoryStore : ICampaignStore
    {
        private readonly Dictionary<Guid,Campaign> campaigns=[];
        private readonly Dictionary<Guid,Character> characters=[];
        public List<CampaignEvent> Events { get; }=[];
        public Task<Campaign?> GetCampaignAsync(Guid id,CancellationToken ct)=>Task.FromResult(campaigns.GetValueOrDefault(id));
        public Task<Character?> GetCharacterAsync(Guid id,CancellationToken ct)=>Task.FromResult(characters.GetValueOrDefault(id));
        public Task CreateCampaignAsync(Campaign campaign,CampaignEvent entry,CancellationToken ct)
        { campaigns.Add(campaign.Id,campaign); Events.Add(entry); return Task.CompletedTask; }
        public Task CreateCharacterAsync(Character character,CampaignEvent entry,CancellationToken ct)
        { characters.Add(character.Id,character); Events.Add(entry); return Task.CompletedTask; }
        public Task SaveCharacterAsync(Character c,CampaignEvent entry,CancellationToken ct)
        {
            characters[c.Id]=new(c.Id,c.CampaignId,c.Name,c.Level.Value,c.Abilities.ToDictionary(x=>x.Key,x=>x.Value.Value),
                c.SkillProficiencies,c.SavingThrowProficiencies,c.ArmorClass,new(c.Health.State),c.Revision+1);
            Events.Add(entry); return Task.CompletedTask;
        }
        public Task<IReadOnlyList<CampaignEvent>> GetEventsAsync(Guid id,long after,int limit,CancellationToken ct)=>
            Task.FromResult<IReadOnlyList<CampaignEvent>>(Events.Where(x=>x.CampaignId==id).Take(limit).ToArray());
    }
}
