using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndEngine.IntegrationTests;

public sealed class MonsterIntegrationTests
{
    private static ServiceProvider Provider(string path, params int[] rolls) => new ServiceCollection()
        .AddLogging().AddDndEngine(path).AddSingleton<IDiceRoller>(new FixedDiceRoller(rolls))
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true });

    [Fact]
    public async Task PinnedDefinitionsSpawnIndependentInstancesAndJoinExistingCombatAcrossRestart()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.MonsterTests",Guid.NewGuid().ToString("N"));
        Guid campaignId,firstId,secondId,encounterId;
        await using(var provider=Provider(path,16,12,8,15,6))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            Assert.False(services.GetRequiredService<CampaignDbContext>().Database.HasPendingModelChanges());
            Assert.False(services.GetRequiredService<RulesDbContext>().Database.HasPendingModelChanges());
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Monster camp"));
            campaignId=campaign.Id;
            var monsters=services.GetRequiredService<MonsterService>();
            var definitions=await monsters.DefinitionsAsync(campaignId);
            Assert.Equal("1",definitions.Version); Assert.Equal(3,definitions.Monsters.Length);
            Assert.Equal(50,definitions.Monsters.Single(x=>x.Id=="skeleton").ExperiencePoints);
            Assert.True(definitions.Monsters.Single(x=>x.Id=="priest-acolyte")
                .Spells.Single(x=>x.SpellId=="healing-word").Supported);
            await Assert.ThrowsAsync<RuleViolation>(()=>monsters.CreateAsync(new(campaignId,
                "goblin-minion",Ammunition:new() { ["dagger"]=3 })));
            var first=await monsters.CreateAsync(new(campaignId,"skeleton",
                Ammunition:new() { ["shortbow"]=2 })); firstId=first.Instance.Id;
            var second=await monsters.CreateAsync(new(campaignId,"skeleton")); secondId=second.Instance.Id;
            Assert.NotEqual(firstId,secondId);
            Assert.Equal(2,first.CombatProfile.Weapons.Single(x=>x.DefinitionId=="shortbow").AmmunitionRemaining);
            Assert.Equal(0,second.CombatProfile.Weapons.Single(x=>x.DefinitionId=="shortbow").AmmunitionRemaining);
            Assert.Contains(DamageType.Bludgeoning,first.CombatProfile.Capabilities.Vulnerabilities);
            var hero=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaignId,
                "Hero",1,Enum.GetValues<Ability>().ToDictionary(x=>x,_=>14),[],[],20));
            var combat=services.GetRequiredService<CombatService>();
            await Assert.ThrowsAsync<RuleViolation>(()=>combat.ImportAsync(firstId,
                new(30,["mace"],[],[],[],[])));
            await Assert.ThrowsAsync<RuleViolation>(()=>combat.GrantWeaponAsync(firstId,new("mace")));
            await combat.ImportAsync(hero.Id,new(30,["mace"],[],[],[],[]));
            var mace=await combat.GrantWeaponAsync(hero.Id,new("mace"));
            Assert.Contains((await services.GetRequiredService<InventoryService>().GetAsync(hero.Id)).Items,
                x=>x.Id==mace.Id && x.DefinitionId=="mace");
            var encounter=await combat.CreateAsync(campaignId,new("Skeleton patrol")); encounterId=encounter.Id;
            var heroCombatant=(await combat.AddAsync(encounterId,new(hero.Id))).Result.Id;
            await Assert.ThrowsAsync<RuleViolation>(()=>combat.AddAsync(encounterId,new(firstId)));
            var firstCombatant=(await monsters.AddToEncounterAsync(encounterId,new(firstId))).Result.Id;
            await monsters.AddToEncounterAsync(encounterId,new(secondId));
            await combat.InitiativeAsync(encounterId);
            await combat.StartAsync(encounterId,new());
            await Assert.ThrowsAsync<StateConflictException>(()=>services.GetRequiredService<MechanicsService>()
                .DamageAsync(firstId,new(1)));
            await Assert.ThrowsAsync<StateConflictException>(()=>services.GetRequiredService<MechanicsService>()
                .HealAsync(hero.Id,new(1)));
            Assert.Equal(3,(await combat.GetAsync(encounterId)).Encounter.Combatants.Length);
            await Assert.ThrowsAsync<RuleViolation>(()=>combat.ApplyConditionAsync(encounterId,
                new(firstCombatant,ConditionKind.Poisoned,"Poison trap")));
            var hit=await combat.AttackAsync(encounterId,new(heroCombatant,
                new(firstCombatant,mace.Id,AttackMode.Melee,new(5,true,true))));
            Assert.True(hit.Result.Health!.After.Dead);
            Assert.Equal(16,hit.Result.Damage!.AppliedDamage);
            Assert.Equal(2,(await services.GetRequiredService<CampaignService>()
                .EventsAsync(campaignId)).Count(x=>x.Type=="MonsterSpawned"));
            Assert.Single(await services.GetRequiredService<CampaignService>()
                .EventsAsync(campaignId),x=>x.Type=="MonsterDefeated");
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var monsters=services.GetRequiredService<MonsterService>();
            Assert.True((await monsters.GetAsync(firstId)).Character.Health.Dead);
            Assert.Equal(13,(await monsters.GetAsync(secondId)).Character.Health.Current);
            Assert.Equal(0,(await monsters.GetAsync(secondId)).CombatProfile.Weapons
                .Single(x=>x.DefinitionId=="shortbow").AmmunitionRemaining);
            Assert.Equal(3,(await services.GetRequiredService<CombatService>()
                .GetAsync(encounterId)).Encounter.Combatants.Length);
            Assert.Equal(campaignId,(await monsters.GetAsync(firstId)).Instance.CampaignId);
        }
    }

    [Fact]
    public async Task PriestAcolyteHealingWordUsesExistingSpellResolverAndDailyResource()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.MonsterMagicTests",Guid.NewGuid().ToString("N"));
        Guid monsterId,encounterId,campaignId;
        await using(var provider=Provider(path,18,10,2,3))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Acolyte"));
            campaignId=campaign.Id;
            var monsters=services.GetRequiredService<MonsterService>();
            var priest=await monsters.CreateAsync(new(campaignId,"priest-acolyte")); monsterId=priest.Instance.Id;
            await services.GetRequiredService<MechanicsService>().DamageAsync(monsterId,new(6));
            var hero=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaignId,
                "Hero",1,Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],20));
            var combat=services.GetRequiredService<CombatService>();
            await combat.ImportAsync(hero.Id,new(30,[],[],[],[],[]));
            var encounter=await combat.CreateAsync(campaignId,new("Acolyte duel")); encounterId=encounter.Id;
            var priestCombatant=(await monsters.AddToEncounterAsync(encounterId,new(monsterId))).Result.Id;
            var heroCombatant=(await combat.AddAsync(encounterId,new(hero.Id))).Result.Id;
            await combat.InitiativeAsync(encounterId); await combat.StartAsync(encounterId,new());
            var revision=(await combat.GetAsync(encounterId)).Encounter.Revision;
            var cast=await combat.CastMonsterSpellAsync(encounterId,new(priestCombatant,
                "healing-word",[new(priestCombatant,0,true)],true,true,true,revision));
            Assert.Equal(6,cast.Result.Targets[0].Health!.HitPointsRegained);
            Assert.Equal(11,(await monsters.GetAsync(monsterId)).Character.Health.Current);
            Assert.Equal(0,(await monsters.GetAsync(monsterId)).Instance.LimitedUsesRemaining!["divine-aid"]);
            await combat.EndTurnAsync(encounterId,new(priestCombatant));
            await combat.EndTurnAsync(encounterId,new(heroCombatant));
            var nextRevision=(await combat.GetAsync(encounterId)).Encounter.Revision;
            await Assert.ThrowsAsync<RuleViolation>(()=>combat.CastMonsterSpellAsync(encounterId,
                new(priestCombatant,"healing-word",[new(priestCombatant,0,true)],
                    true,true,true,nextRevision)));
            Assert.Single(await services.GetRequiredService<CampaignService>().EventsAsync(campaignId),
                x=>x.Type=="CombatSpellCast");
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var priest=await services.GetRequiredService<MonsterService>().GetAsync(monsterId);
            Assert.Equal(11,priest.Character.Health.Current);
            Assert.Equal(0,priest.Instance.LimitedUsesRemaining!["divine-aid"]);
            Assert.Equal(EncounterStatus.Active,(await services.GetRequiredService<CombatService>()
                .GetAsync(encounterId)).Encounter.Status);
        }
    }

    [Fact]
    public async Task GoblinAttacksAndEncounterMayEndByRetreatWithEnemyAlive()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.GoblinTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path,18,10,15,3);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Goblin attack"));
        var monster=await services.GetRequiredService<MonsterService>().CreateAsync(new(campaign.Id,"goblin-minion"));
        Assert.Equal(3,(await services.GetRequiredService<InventoryService>().GetAsync(monster.Instance.Id))
            .Items.Count(x=>x.DefinitionId=="dagger"));
        var hero=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaign.Id,
            "Hero",1,Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],20));
        var combat=services.GetRequiredService<CombatService>();
        await combat.ImportAsync(hero.Id,new(30,[],[],[],[],[]));
        var encounter=await combat.CreateAsync(campaign.Id,new("Escape the goblin"));
        var goblinCombatant=(await services.GetRequiredService<MonsterService>()
            .AddToEncounterAsync(encounter.Id,new(monster.Instance.Id))).Result.Id;
        var heroCombatant=(await combat.AddAsync(encounter.Id,new(hero.Id))).Result.Id;
        await combat.InitiativeAsync(encounter.Id); await combat.StartAsync(encounter.Id,new());
        var dagger=monster.CombatProfile.Weapons.First(x=>x.DefinitionId=="dagger");
        var attack=await combat.AttackAsync(encounter.Id,new(goblinCombatant,
            new(heroCombatant,dagger.Id,AttackMode.Melee,new(5,true,true))));
        Assert.True(attack.Result.Hit);
        Assert.Equal(5,attack.Result.Damage!.AppliedDamage);
        var complete=await combat.CompleteAsync(encounter.Id,new(EncounterOutcome.Retreat,
            attack.Revision));
        Assert.Equal(EncounterStatus.Completed,complete.Result.Status);
        Assert.Equal(0,(await services.GetRequiredService<EncounterRewardService>()
            .GetAsync(encounter.Id)).Rewards.AvailableExperience);
        Assert.False((await services.GetRequiredService<MonsterService>()
            .GetAsync(monster.Instance.Id)).Character.Health.Dead);
    }

    [Fact]
    public async Task UnpinnedDefeatedMonsterDoesNotBlockCompletionOrInventExperience()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.CustomMonsterTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path,18,8,15,6);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Custom monster"));
        var characters=services.GetRequiredService<CharacterService>();
        var hero=await characters.CreateAsync(new(campaign.Id,"Hero",1,
            Enum.GetValues<Ability>().ToDictionary(x=>x,_=>14),[],[],20));
        var custom=await characters.CreateAsync(new(campaign.Id,"Custom",1,
            Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],5));
        var combat=services.GetRequiredService<CombatService>();
        await combat.ImportAsync(hero.Id,new(30,["mace"],[],[],[],[]));
        await combat.ImportAsync(custom.Id,new(30,[],[],[],[],[]));
        var mace=await combat.GrantWeaponAsync(hero.Id,new("mace"));
        var encounter=await combat.CreateAsync(campaign.Id,new("Custom battle"));
        var actor=(await combat.AddAsync(encounter.Id,new(hero.Id))).Result.Id;
        var target=(await combat.AddAsync(encounter.Id,new(custom.Id,
            CombatantKind.Monster,ZeroHpPolicy.Die))).Result.Id;
        await combat.InitiativeAsync(encounter.Id); await combat.StartAsync(encounter.Id,new());
        var hit=await combat.AttackAsync(encounter.Id,new(actor,
            new(target,mace.Id,AttackMode.Melee,new(5,true,true))));
        Assert.True(hit.Result.Health!.After.Dead);
        await combat.CompleteAsync(encounter.Id,new(EncounterOutcome.Victory,hit.Revision));
        var rewards=(await services.GetRequiredService<EncounterRewardService>()
            .GetAsync(encounter.Id)).Rewards;
        Assert.Equal(0,rewards.AvailableExperience);
        Assert.Empty(rewards.DefeatedMonsterIds);
    }
}
