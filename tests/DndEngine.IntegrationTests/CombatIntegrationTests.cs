using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Infrastructure;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace DndEngine.IntegrationTests;

public class CombatIntegrationTests
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } };
    private static string DirectoryPath() => Path.Combine(Path.GetTempPath(), "DndEngine.CombatTests", Guid.NewGuid().ToString("N"));
    private sealed class Factory(string path, params int[] rolls) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("DataDirectory",path);
            builder.ConfigureAppConfiguration((_, c) => c.AddInMemoryCollection(new Dictionary<string, string?> { ["DataDirectory"] = path }));
            builder.ConfigureServices(s => s.AddSingleton<IDiceRoller>(new FixedDiceRoller(rolls)));
        }
    }
    private static CreateCharacter Sheet(Guid campaign, string name, int hp = 20) => new(campaign, name, 1,
        Enum.GetValues<Ability>().ToDictionary(x => x, x => x == Ability.Strength ? 18 : 14), [], [], hp, 13);
    private static CombatCapabilities Capabilities(string weapon = "longsword") => new(30, [weapon], [], [], [], []);
    private static async Task<T> Post<T>(HttpClient client, string url, object body)
    {
        using var response = await client.PostAsJsonAsync(url, body, Json);
        Assert.True(response.IsSuccessStatusCode, await response.Content.ReadAsStringAsync());
        return (await response.Content.ReadFromJsonAsync<T>(Json))!;
    }
    private static async Task<T> Get<T>(HttpClient client, string url) => (await client.GetFromJsonAsync<T>(url, Json))!;

    [Fact]
    public async Task RealisticCombatSurvivesApiRestartAndContinuesWithIdenticalTimeline()
    {
        var path = DirectoryPath(); Guid campaignId, encounterId, heroId, goblinBId, actor, targetB, sword;
        string beforeRestart; CampaignEvent[] eventsBefore;
        await using (var app = new Factory(path, 15, 10, 20, 8, 7, 16, 4))
        {
            using var client = app.CreateClient();
            var campaign = await Post<CampaignView>(client, "/campaigns", new CreateCampaign("The broken bridge")); campaignId = campaign.Id;
            var hero = await Post<CharacterView>(client, "/characters", Sheet(campaignId, "Vaelaris", 30)); heroId = hero.Id;
            var a = await Post<CharacterView>(client, "/characters", Sheet(campaignId, "Goblin A", 12));
            var b = await Post<CharacterView>(client, "/characters", Sheet(campaignId, "Goblin B", 12)); goblinBId = b.Id;
            foreach (var character in new[] { hero, a, b })
            {
                using var put = await client.PutAsJsonAsync($"/characters/{character.Id}/combat-profile", Capabilities(), Json); put.EnsureSuccessStatusCode();
            }
            sword = (await Post<OwnedWeapon>(client, $"/characters/{hero.Id}/weapons", new GrantWeapon("longsword"))).Id;
            var goblinSword = (await Post<OwnedWeapon>(client, $"/characters/{b.Id}/weapons", new GrantWeapon("longsword"))).Id;
            var encounter = await Post<EncounterState>(client, $"/campaigns/{campaignId}/combat", new CreateCombat("Bridge ambush")); encounterId = encounter.Id;
            actor = (await Post<CombatCommandResult<CombatantState>>(client, $"/combat/{encounterId}/combatants", new AddCombatant(hero.Id))).Result.Id;
            var targetA = (await Post<CombatCommandResult<CombatantState>>(client, $"/combat/{encounterId}/combatants", new AddCombatant(a.Id, CombatantKind.Monster, ZeroHpPolicy.Die, InitiativeGroup: "goblins"))).Result.Id;
            targetB = (await Post<CombatCommandResult<CombatantState>>(client, $"/combat/{encounterId}/combatants", new AddCombatant(b.Id, CombatantKind.Monster, ZeroHpPolicy.Die, InitiativeGroup: "goblins"))).Result.Id;
            var initiative = await Post<CombatCommandResult<InitiativeResult>>(client, $"/combat/{encounterId}/initiative", new { });
            Assert.Equal("GameMaster", Assert.Single(initiative.Result.Ties).DecidedBy);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/combat/{encounterId}/start", new { })).StatusCode);
            await Post<CombatCommandResult<EncounterState>>(client, $"/combat/{encounterId}/start", new StartCombat([actor, targetA, targetB]));
            await Post<CombatCommandResult<MovementResult>>(client, $"/combat/{encounterId}/move", new MoveCombatant(actor, 10));
            var hit = (await Post<CombatCommandResult<WeaponAttackResult>>(client, $"/combat/{encounterId}/attack",
                new AttackCombatant(actor, new(targetA, sword, AttackMode.Melee, new(5, true, true))))).Result;
            Assert.True(hit.Critical); Assert.Equal(19, hit.Damage!.AppliedDamage); Assert.True(hit.Health!.After.Dead);
            await Post<CombatCommandResult<EncounterState>>(client, $"/combat/{encounterId}/end-turn", new CombatActor(actor));
            await Post<CombatCommandResult<EncounterState>>(client, $"/combat/{encounterId}/end-turn", new CombatActor(targetA));
            var reply = (await Post<CombatCommandResult<WeaponAttackResult>>(client, $"/combat/{encounterId}/attack",
                new AttackCombatant(targetB, new(actor, goblinSword, AttackMode.Melee, new(5, true, true))))).Result;
            Assert.Equal(8, reply.Damage!.AppliedDamage); Assert.Equal(22, reply.Health!.After.Current);
            await Post<CombatCommandResult<ActiveCondition>>(client, $"/combat/{encounterId}/conditions", new ApplyCombatCondition(actor, ConditionKind.Poisoned, "Goblin poison"));
            await Post<CombatCommandResult<EncounterState>>(client, $"/combat/{encounterId}/end-turn", new CombatActor(targetB));
            var view = await Get<CombatView>(client, $"/combat/{encounterId}");
            Assert.Equal(2, view.Encounter.Round); Assert.Equal(actor, view.CurrentCombatantId); Assert.Equal(4, view.Encounter.TurnNumber);
            Assert.Equal(0, view.Encounter.Combatants.Single(x => x.Id == actor).Resources.MovementUsed);
            beforeRestart = JsonSerializer.Serialize(view, Json);
            eventsBefore = await Get<CampaignEvent[]>(client, $"/campaigns/{campaignId}/events?limit=500");
            var kill = Assert.Single(eventsBefore, x => x.Type == "AttackMade" && x.Data.GetProperty("data").GetProperty("health").GetProperty("after").GetProperty("dead").GetBoolean());
            Assert.Equal(actor, kill.Data.GetProperty("data").GetProperty("attackerId").GetGuid());
            Assert.Equal("longsword", kill.Data.GetProperty("data").GetProperty("definitionId").GetString());
            var condition = Assert.Single(eventsBefore, x => x.Type == "ConditionApplied");
            Assert.Equal(actor, condition.Data.GetProperty("subjectCombatantId").GetGuid());
        }
        await using (var app = new Factory(path, 18, 12, 8))
        {
            using var client = app.CreateClient();
            Assert.Equal(beforeRestart, JsonSerializer.Serialize(await Get<CombatView>(client, $"/combat/{encounterId}"), Json));
            Assert.Equal(JsonSerializer.Serialize(eventsBefore, Json), JsonSerializer.Serialize(await Get<CampaignEvent[]>(client, $"/campaigns/{campaignId}/events?limit=500"), Json));
            var attack = (await Post<CombatCommandResult<WeaponAttackResult>>(client, $"/combat/{encounterId}/attack",
                new AttackCombatant(actor, new(targetB, sword, AttackMode.Melee, new(5, true, true))))).Result;
            Assert.Equal(AdvantageState.Disadvantage, attack.AttackRoll.AdvantageState); Assert.True(attack.Health!.After.Dead);
            Assert.Equal(0, (await Get<CharacterView>(client, $"/characters/{goblinBId}")).Health.Current);
            var completed = await Post<CombatCommandResult<EncounterState>>(client, $"/combat/{encounterId}/end", new { });
            Assert.Equal(EncounterStatus.Completed, completed.Result.Status);
            Assert.Equal(HttpStatusCode.BadRequest, (await client.PostAsJsonAsync($"/combat/{encounterId}/end-turn", new CombatActor(actor), Json)).StatusCode);
            var profile = await Get<CombatProfileState>(client, $"/characters/{heroId}/combat-profile");
            var remove = await client.DeleteAsync($"/characters/{heroId}/conditions/{profile.Conditions[0].Id}"); remove.EnsureSuccessStatusCode();
            Assert.Empty((await Get<CombatProfileState>(client, $"/characters/{heroId}/combat-profile")).Conditions);
        }
    }
    private static ServiceProvider Provider(string path, IDiceRoller dice) => new ServiceCollection().AddLogging().AddDndEngine(path)
        .AddSingleton(dice).BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true });
    private sealed record Setup(Guid Campaign, Guid Encounter, Guid Actor, Guid Target, Guid ActorCharacter, Guid TargetCharacter, Guid Weapon);
    private static async Task<Setup> Prepare(IServiceProvider services)
    {
        var campaign = await services.GetRequiredService<CampaignService>().CreateAsync(new("Conflict test"));
        var characters = services.GetRequiredService<CharacterService>(); var combat = services.GetRequiredService<CombatService>();
        var a = await characters.CreateAsync(Sheet(campaign.Id, "Archer", 30)); var b = await characters.CreateAsync(Sheet(campaign.Id, "Target", 30));
        await combat.ImportAsync(a.Id, Capabilities("shortbow")); await combat.ImportAsync(b.Id, Capabilities());
        var weapon = await combat.GrantWeaponAsync(a.Id, new("shortbow", 10));
        var e = await combat.CreateAsync(campaign.Id, new("Ambush"));
        var actor = await combat.AddAsync(e.Id, new(a.Id)); var target = await combat.AddAsync(e.Id, new(b.Id));
        await combat.InitiativeAsync(e.Id); await combat.StartAsync(e.Id, new());
        return new(campaign.Id, e.Id, actor.Result.Id, target.Result.Id, a.Id, b.Id, weapon.Id);
    }
    private static WeaponAttackOptions BowAttack(Setup f) => new(f.Target, f.Weapon, AttackMode.Ranged, new(30, true, true), Hands: 2);
    private sealed class InterruptingDice(params int[] rolls) : IDiceRoller
    {
        private readonly FixedDiceRoller inner = new(rolls);
        public Action? BeforeRoll { get; set; }
        public int Count { get; private set; }
        public DiceResult Roll(DiceExpression expression) { Count++; var callback = BeforeRoll; BeforeRoll = null; callback?.Invoke(); return inner.Roll(expression); }
    }
    [Fact]
    public async Task ConcurrentCharacterChangeRejectsEntireAttackWithoutRerolling()
    {
        var dice = new InterruptingDice(15, 5, 16, 4);
        await using var provider = Provider(DirectoryPath(), dice); await provider.InitializeDndEngineAsync();
        await using var scope = provider.CreateAsyncScope(); var services = scope.ServiceProvider; var f = await Prepare(services);
        await using var other = provider.CreateAsyncScope();
        dice.BeforeRoll = () => other.ServiceProvider.GetRequiredService<MechanicsService>().DamageAsync(f.TargetCharacter, new(1)).GetAwaiter().GetResult();
        await Assert.ThrowsAsync<StateConflictException>(() => services.GetRequiredService<CombatService>().AttackAsync(f.Encounter, new(f.Actor, BowAttack(f))));
        Assert.Equal(4, dice.Count); // two initiative rolls, exactly one attack roll and one damage roll
        var state = await services.GetRequiredService<CombatService>().GetAsync(f.Encounter);
        Assert.Equal(29, state.Characters.Single(x => x.Id == f.TargetCharacter).Health.Current);
        Assert.Equal(10, state.Profiles.Single(x => x.CharacterId == f.ActorCharacter).Weapons[0].AmmunitionRemaining);
        Assert.False(state.Encounter.Combatants.Single(x => x.Id == f.Actor).Resources.ActionUsed);
        Assert.DoesNotContain(await services.GetRequiredService<CampaignService>().EventsAsync(f.Campaign), e => e.Type == "AttackMade");
    }
    [Fact]
    public async Task FailedAuditInsertRollsBackEncounterHpAmmunitionAndConditions()
    {
        await using var provider = Provider(DirectoryPath(), new FixedDiceRoller(15, 5)); await provider.InitializeDndEngineAsync();
        await using var scope = provider.CreateAsyncScope(); var services = scope.ServiceProvider; var f = await Prepare(services);
        var store = services.GetRequiredService<ICombatStore>(); var campaigns = services.GetRequiredService<ICampaignStore>();
        var e = (await store.GetEncounterAsync(f.Encounter, default))!;
        var a = (await campaigns.GetCharacterAsync(f.ActorCharacter, default))!; var b = (await campaigns.GetCharacterAsync(f.TargetCharacter, default))!;
        var pa = (await store.GetProfileAsync(a.Id, default))!; var pb = (await store.GetProfileAsync(b.Id, default))!;
        var events = await campaigns.GetEventsAsync(f.Campaign, 0, 500, default);
        var weapon = (await services.GetRequiredService<ICombatCatalog>().GetAsync(Ruleset.Current, default)).Weapons.Single(x => x.Id == "shortbow");
        new WeaponAttackResolver(new FixedDiceRoller(16, 4)).Resolve(e, f.Actor, a, pa, b, pb, weapon, BowAttack(f));
        pb.AddCondition(new(Guid.NewGuid(), ConditionKind.Poisoned, "failed operation", null), b);
        await Assert.ThrowsAsync<StateConflictException>(() => store.SaveEncounterAsync(e, [a, b], [pa, pb], [events[0]], false, default));
        Assert.Equal(30, (await campaigns.GetCharacterAsync(b.Id, default))!.Health.State.Current);
        Assert.Empty((await store.GetProfileAsync(b.Id, default))!.State.Conditions);
        Assert.Equal(10, (await store.GetProfileAsync(a.Id, default))!.State.Weapons[0].AmmunitionRemaining);
        var persisted = (await store.GetEncounterAsync(f.Encounter, default))!;
        Assert.Equal(e.State.Revision, persisted.State.Revision); Assert.False(persisted.Combatant(f.Actor).Resources.ActionUsed);
        Assert.Equal(events.Count, (await campaigns.GetEventsAsync(f.Campaign, 0, 500, default)).Count);
    }
    [Fact]
    public async Task StaleEncounterRevisionRejectsWriteEvenWithoutChangedCharacter()
    {
        await using var provider = Provider(DirectoryPath(), new FixedDiceRoller(15, 5)); await provider.InitializeDndEngineAsync();
        await using var first = provider.CreateAsyncScope(); await using var second = provider.CreateAsyncScope();
        var f = await Prepare(first.ServiceProvider); var staleStore = second.ServiceProvider.GetRequiredService<ICombatStore>();
        var stale = (await staleStore.GetEncounterAsync(f.Encounter, default))!;
        await first.ServiceProvider.GetRequiredService<CombatService>().MoveAsync(f.Encounter, new(f.Actor, 5));
        stale.Complete();
        await Assert.ThrowsAsync<StateConflictException>(() => staleStore.SaveEncounterAsync(stale, [], [], [], false, default));
        Assert.Equal(EncounterStatus.Active, (await staleStore.GetEncounterAsync(f.Encounter, default))!.State.Status);
        Assert.True(await staleStore.IsEnrolledAsync(f.ActorCharacter, default));
    }
    [Fact]
    public async Task CombatContentIsPinnedValidatedAndTamperResistant()
    {
        await using var provider = Provider(DirectoryPath(), new FixedDiceRoller()); await provider.InitializeDndEngineAsync(); await provider.InitializeDndEngineAsync();
        await using var scope = provider.CreateAsyncScope(); var services = scope.ServiceProvider;
        var content = await services.GetRequiredService<ICombatCatalog>().GetAsync(Ruleset.Current, default);
        Assert.Equal(38, content.Weapons.Length); Assert.Equal(15, content.Conditions.Length); Assert.Equal(8, content.Masteries.Length);
        Assert.All(content.Masteries, m => Assert.False(m.Automated)); Assert.All(content.Weapons, w => w.Validate());
        Assert.Equal(1, content.Weapons.Single(w => w.Id == "blowgun").FixedDamage);
        var db = services.GetRequiredService<RulesDbContext>(); var row = await db.CombatContent.SingleAsync(); row.DataJson = "{}"; await db.SaveChangesAsync();
        await Assert.ThrowsAsync<InvalidOperationException>(() => CombatCatalog.ImportAsync(db, default));
    }
    [Fact]
    public async Task AutomaticDeathSavesConditionExpiryAndGrappleReleasePersist()
    {
        await using var provider = Provider(DirectoryPath(), new FixedDiceRoller(15, 5, 20)); await provider.InitializeDndEngineAsync();
        await using var scope = provider.CreateAsyncScope(); var services = scope.ServiceProvider; var f = await Prepare(services);
        var combat = services.GetRequiredService<CombatService>();
        await combat.ApplyConditionAsync(f.Encounter, new(f.Actor, ConditionKind.Grappled, "grapple", f.TargetCharacter));
        await combat.ApplyConditionAsync(f.Encounter, new(f.Target, ConditionKind.Stunned, "stun", Expiry: ExpiryBoundary.TurnStart, ExpiresOnTurn: 2));
        Assert.Empty((await combat.GetAsync(f.Encounter)).Profiles.Single(x => x.CharacterId == f.ActorCharacter).Conditions);
        var save = await combat.SavingThrowAsync(f.Encounter, new(f.Target, new(Ability.Strength, 1)));
        Assert.True(save.Result.AutomaticFailure); Assert.Null(save.Result.Roll);
        await services.GetRequiredService<MechanicsService>().DamageAsync(f.TargetCharacter, new(30));
        await combat.EndTurnAsync(f.Encounter, new(f.Actor));
        var view = await combat.GetAsync(f.Encounter);
        Assert.Empty(view.Profiles.Single(x => x.CharacterId == f.TargetCharacter).Conditions);
        Assert.Equal(1, view.Characters.Single(x => x.Id == f.TargetCharacter).Health.Current);
        var events = await services.GetRequiredService<CampaignService>().EventsAsync(f.Campaign);
        Assert.Contains(events, x => x.Type == "ConditionExpired"); Assert.Contains(events, x => x.Type == "DeathSavingThrowMade");
    }
    [Fact]
    public async Task EnrollmentProfileAndCrossCampaignGuardsLeaveNoWrites()
    {
        await using var provider = Provider(DirectoryPath(), new FixedDiceRoller(15, 5)); await provider.InitializeDndEngineAsync();
        await using var scope = provider.CreateAsyncScope(); var s = scope.ServiceProvider; var f = await Prepare(s); var combat = s.GetRequiredService<CombatService>();
        var second = await combat.CreateAsync(f.Campaign, new("Second fight"));
        await Assert.ThrowsAsync<RuleViolation>(() => combat.AddAsync(second.Id, new(f.ActorCharacter)));
        await Assert.ThrowsAsync<RuleViolation>(() => combat.ImportAsync(f.ActorCharacter, Capabilities()));
        await Assert.ThrowsAsync<RuleViolation>(() => combat.GrantWeaponAsync(f.ActorCharacter, new("longsword")));
        var foreign = await s.GetRequiredService<CampaignService>().CreateAsync(new("Other campaign"));
        var outsider = await s.GetRequiredService<CharacterService>().CreateAsync(Sheet(foreign.Id, "Outsider"));
        await combat.ImportAsync(outsider.Id, Capabilities());
        await Assert.ThrowsAsync<RuleViolation>(() => combat.AddAsync(second.Id, new(outsider.Id)));
        Assert.Empty((await combat.GetAsync(second.Id)).Encounter.Combatants);
        await combat.EndAsync(f.Encounter); await combat.AddAsync(second.Id, new(f.ActorCharacter));
        Assert.Single((await combat.GetAsync(second.Id)).Encounter.Combatants);
    }
    [Fact]
    public async Task AdditiveMigrationPreservesPhaseOneCharactersEventsAndSkillHash()
    {
        var path = DirectoryPath(); Guid characterId, campaignId; string oldEvents, hash;
        await using (var provider = Provider(path, new FixedDiceRoller()))
        {
            await using var scope = provider.CreateAsyncScope(); var s = scope.ServiceProvider;
            var rules = s.GetRequiredService<RulesDbContext>(); var campaign = s.GetRequiredService<CampaignDbContext>();
            await rules.GetService<IMigrator>().MigrateAsync(rules.Database.GetMigrations().First());
            await campaign.GetService<IMigrator>().MigrateAsync(campaign.Database.GetMigrations().First());
            await RulesCatalog.ImportAsync(rules); hash = (await rules.Rulesets.SingleAsync()).ContentHash;
            var c = await s.GetRequiredService<CampaignService>().CreateAsync(new("Existing Phase 1 campaign")); campaignId = c.Id;
            var character = await s.GetRequiredService<CharacterService>().CreateAsync(Sheet(c.Id, "Old character")); characterId = character.Id;
            await s.GetRequiredService<MechanicsService>().DamageAsync(characterId, new(7));
            oldEvents = JsonSerializer.Serialize(await s.GetRequiredService<CampaignService>().EventsAsync(campaignId), Json);
        }
        await using (var provider = Provider(path, new FixedDiceRoller()))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope = provider.CreateAsyncScope(); var s = scope.ServiceProvider;
            Assert.Equal(13, (await s.GetRequiredService<CharacterService>().GetAsync(characterId)).Health.Current);
            Assert.Equal(oldEvents, JsonSerializer.Serialize(await s.GetRequiredService<CampaignService>().EventsAsync(campaignId), Json));
            Assert.Equal(hash, (await s.GetRequiredService<RulesDbContext>().Rulesets.SingleAsync()).ContentHash);
            await s.GetRequiredService<CombatService>().ImportAsync(characterId, Capabilities());
            Assert.Equal(30, (await s.GetRequiredService<CombatService>().ProfileAsync(characterId)).Capabilities.Speed);
        }
    }
}
