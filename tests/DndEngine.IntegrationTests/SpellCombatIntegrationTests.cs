using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Progression;
using DndEngine.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DndEngine.IntegrationTests;

public sealed class SpellCombatIntegrationTests
{
    private static ServiceProvider Provider(string path,params int[] rolls) => new ServiceCollection()
        .AddLogging().AddDndEngine(path).AddSingleton<IDiceRoller>(new FixedDiceRoller(rolls))
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true });

    [Fact]
    public async Task ViciousMockeryPenaltyPersistsAndIsConsumedByNextAttack()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.MockeryTests",Guid.NewGuid().ToString("N"));
        Guid encounterId,bardCombatant,enemyCombatant,weaponId;
        await using(var provider=Provider(path,18,2,1,3))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Mockery"));
            var bard=await services.GetRequiredService<ProgressionService>().CreateAsync(new(campaign.Id,
                "Lyris","dwarf",null,"Medium","criminal","bard",
                Enum.GetValues<Ability>().ToDictionary(x=>x,_=>13),
                new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },
                ["arcana","history","insight"],ClassTools:["lute","flute","drum"],
                KnownCantripIds:["vicious-mockery"]));
            var enemy=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaign.Id,"Enemy",1,
                Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],20,10));
            var combat=services.GetRequiredService<CombatService>();
            await combat.ImportAsync(enemy.Id,new(30,["longsword"],[],[],[],[]));
            weaponId=(await combat.GrantWeaponAsync(enemy.Id,new("longsword"))).Id;
            var encounter=await combat.CreateAsync(campaign.Id,new("Duel")); encounterId=encounter.Id;
            bardCombatant=(await combat.AddAsync(encounterId,new(bard.Id))).Result.Id;
            enemyCombatant=(await combat.AddAsync(encounterId,new(enemy.Id,CombatantKind.Monster,ZeroHpPolicy.Die))).Result.Id;
            await combat.InitiativeAsync(encounterId); await combat.StartAsync(encounterId,new());
            var revision=(await combat.GetAsync(encounterId)).Encounter.Revision;
            var cast=await combat.CastSpellAsync(encounterId,new(bardCombatant,"bard","vicious-mockery",null,0,
                [new(enemyCombatant,30,false,CasterCanHearTarget:true)],true,false,false,revision));
            Assert.True(cast.Result.Targets[0].HitOrFailedSave);
            Assert.Single((await combat.GetAsync(encounterId)).Encounter.AttackPenalties!);
            await combat.EndTurnAsync(encounterId,new(bardCombatant));
        }
        await using(var provider=Provider(path,18,2,4))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var combat=scope.ServiceProvider.GetRequiredService<CombatService>();
            Assert.Single((await combat.GetAsync(encounterId)).Encounter.AttackPenalties!);
            var attack=(await combat.AttackAsync(encounterId,new(enemyCombatant,
                new(bardCombatant,weaponId,AttackMode.Melee,new(5,true,true))))).Result;
            Assert.Equal(AdvantageState.Disadvantage,attack.AttackRoll.AdvantageState);
            Assert.Empty((await combat.GetAsync(encounterId)).Encounter.AttackPenalties!);
        }
    }

    [Fact]
    public async Task EldritchBlastUsesSeparateBeamsAndAllowsOneTargetTwice()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.BeamTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path,18,2,18,4,17,5);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Beams"));
        var progression=services.GetRequiredService<ProgressionService>();
        var warlock=await progression.CreateAsync(new(campaign.Id,"Thane","dwarf",null,"Medium",
            "criminal","warlock",Enum.GetValues<Ability>().ToDictionary(x=>x,_=>13),
            new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","history"],
            KnownCantripIds:["eldritch-blast","poison-spray"]));
        for(var level=2;level<=5;level++)
            warlock=await progression.LevelUpAsync(warlock.Id,level switch
            {
                3 => new("warlock",warlock.Revision,SubclassId:"fiend-patron"),
                4 => new("warlock",warlock.Revision,FeatId:"ability-score-improvement",
                    AbilityIncreases:new() { [Ability.Charisma]=2 }),
                _ => new("warlock",warlock.Revision)
            });
        var enemy=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaign.Id,"Enemy",1,
            Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],30,10));
        var combat=services.GetRequiredService<CombatService>();
        await combat.ImportAsync(enemy.Id,new(30,[],[],[],[],[]));
        var encounter=await combat.CreateAsync(campaign.Id,new("Beams"));
        var actor=(await combat.AddAsync(encounter.Id,new(warlock.Id))).Result.Id;
        var target=(await combat.AddAsync(encounter.Id,new(enemy.Id,CombatantKind.Monster,ZeroHpPolicy.Die))).Result.Id;
        await combat.InitiativeAsync(encounter.Id); await combat.StartAsync(encounter.Id,new());
        var revision=(await combat.GetAsync(encounter.Id)).Encounter.Revision;
        await Assert.ThrowsAsync<RuleViolation>(()=>combat.CastSpellAsync(encounter.Id,
            new(actor,"warlock","eldritch-blast",null,0,[new(target,30,true)],true,true,false,revision)));
        var cast=await combat.CastSpellAsync(encounter.Id,new(actor,"warlock","eldritch-blast",null,0,
            [new(target,30,true),new(target,30,true)],true,true,false,revision));
        Assert.Equal(2,cast.Result.Targets.Length);
        Assert.Equal([4,5],cast.Result.Targets.Select(x=>x.Damage!.AppliedDamage).ToArray());
        Assert.Equal(21,(await combat.GetAsync(encounter.Id)).Characters.Single(x=>x.Id==enemy.Id).Health.Current);
    }

    [Fact]
    public async Task DistantAndHeightenedSpellApplyRangeAndSaveRules()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.MoreMetamagicTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path,18,2,18,2,2,2,2,18,2);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Metamagic range"));
        var progression=services.GetRequiredService<ProgressionService>();
        var sorcerer=await progression.CreateAsync(new(campaign.Id,"Nira","dwarf",null,"Medium",
            "criminal","sorcerer",Enum.GetValues<Ability>().ToDictionary(x=>x,_=>13),
            new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","persuasion"],
            PreparedSpellIds:["burning-hands"],KnownCantripIds:["fire-bolt"]));
        sorcerer=await progression.LevelUpAsync(sorcerer.Id,new("sorcerer",sorcerer.Revision,
            AdditionalMetamagicOptions:[MetamagicOption.DistantSpell,MetamagicOption.HeightenedSpell]));
        var enemy=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaign.Id,"Enemy",1,
            Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],30,10));
        var combat=services.GetRequiredService<CombatService>();
        await combat.ImportAsync(enemy.Id,new(30,[],[],[],[],[]));
        var encounter=await combat.CreateAsync(campaign.Id,new("Duel"));
        var actor=(await combat.AddAsync(encounter.Id,new(sorcerer.Id))).Result.Id;
        var target=(await combat.AddAsync(encounter.Id,new(enemy.Id,CombatantKind.Monster,ZeroHpPolicy.Die))).Result.Id;
        await combat.InitiativeAsync(encounter.Id); await combat.StartAsync(encounter.Id,new());
        var revision=(await combat.GetAsync(encounter.Id)).Encounter.Revision;
        await Assert.ThrowsAsync<RuleViolation>(()=>combat.CastSpellAsync(encounter.Id,
            new(actor,"sorcerer","fire-bolt",null,0,[new(target,200,true)],true,true,false,revision)));
        var distant=await combat.CastSpellAsync(encounter.Id,new(actor,"sorcerer","fire-bolt",null,0,
            [new(target,200,true)],true,true,false,revision,MetamagicOption.DistantSpell));
        Assert.Equal(2,distant.Result.Targets[0].Damage!.AppliedDamage);
        var converted=await combat.ConvertSpellSlotAsync(encounter.Id,
            new(actor,SpellSlotPoolKind.Shared,1,distant.Revision));
        Assert.Equal(2,converted.Result.SorceryPointsAfter);
        await combat.EndTurnAsync(encounter.Id,new(actor));
        await combat.EndTurnAsync(encounter.Id,new(target));
        revision=(await combat.GetAsync(encounter.Id)).Encounter.Revision;
        var heightened=await combat.CastSpellAsync(encounter.Id,new(actor,"sorcerer","burning-hands",
            SpellSlotPoolKind.Shared,1,[new(target,10,true)],true,true,false,revision,
            MetamagicOption.HeightenedSpell,MetamagicTargetId:target));
        Assert.Equal(AdvantageState.Disadvantage,heightened.Result.Targets[0].SavingThrow!.AdvantageState);
        Assert.Equal(6,heightened.Result.Targets[0].Damage!.AppliedDamage);
        Assert.Equal(0,heightened.Result.SorceryPointsAfter);
    }

    [Fact]
    public async Task CantripAndAreaSpellUseTurnBudgetAndCommitHpSlotsAndAuditAcrossRestart()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.SpellCombatTests",Guid.NewGuid().ToString("N"));
        Guid encounterId,casterId,target1Id,target2Id,campaignId;
        await using(var provider=Provider(path,18,2,1,15,6,4,5,6,1,2))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Spell combat"));
            campaignId=campaign.Id;
            var progression=services.GetRequiredService<ProgressionService>();
            var caster=await progression.CreateAsync(new(campaign.Id,"Ilyra","dwarf",null,"Medium",
                "criminal","wizard",Enum.GetValues<Ability>().ToDictionary(x=>x,_=>13),
                new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","history"],
                PreparedSpellIds:["burning-hands"],KnownCantripIds:["fire-bolt"],
                WizardSpellbookIds:["burning-hands"]));
            casterId=caster.Id;
            var characters=services.GetRequiredService<CharacterService>();
            CreateCharacter monster(string name) => new(campaign.Id,name,1,
                Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],20,10);
            var target1=await characters.CreateAsync(monster("Target One"));
            var target2=await characters.CreateAsync(monster("Target Two"));
            target1Id=target1.Id; target2Id=target2.Id;
            var combat=services.GetRequiredService<CombatService>();
            var capabilities=new CombatCapabilities(30,[],[],[],[],[]);
            await combat.ImportAsync(target1.Id,capabilities);
            await combat.ImportAsync(target2.Id,capabilities);
            var encounter=await combat.CreateAsync(campaign.Id,new("Three combatants"));
            encounterId=encounter.Id;
            var actor=(await combat.AddAsync(encounterId,new(caster.Id))).Result.Id;
            var first=(await combat.AddAsync(encounterId,new(target1.Id,CombatantKind.Monster,ZeroHpPolicy.Die))).Result.Id;
            var second=(await combat.AddAsync(encounterId,new(target2.Id,CombatantKind.Monster,ZeroHpPolicy.Die))).Result.Id;
            await combat.InitiativeAsync(encounterId);
            await combat.StartAsync(encounterId,new());
            var revision=(await combat.GetAsync(encounterId)).Encounter.Revision;
            var bolt=await combat.CastSpellAsync(encounterId,new(actor,"wizard","fire-bolt",null,0,
                [new(first,30,true)],true,true,false,revision));
            Assert.Equal(6,bolt.Result.Targets[0].Damage!.AppliedDamage);
            Assert.True(bolt.Result.Resources.ActionUsed);
            Assert.Equal(2,(await progression.SpellcastingAsync(casterId))!.SharedSlots[0].Current);
            await Assert.ThrowsAsync<RuleViolation>(()=>combat.CastSpellAsync(encounterId,
                new(actor,"wizard","burning-hands",SpellSlotPoolKind.Shared,1,
                    [new(first,10,true),new(second,10,true)],true,true,false,bolt.Revision)));
            await combat.EndTurnAsync(encounterId,new(actor));
            await combat.EndTurnAsync(encounterId,new(first));
            await combat.EndTurnAsync(encounterId,new(second));
            revision=(await combat.GetAsync(encounterId)).Encounter.Revision;
            var area=await combat.CastSpellAsync(encounterId,new(actor,"wizard","burning-hands",
                SpellSlotPoolKind.Shared,1,[new(first,10,true),new(second,10,true)],
                true,true,false,revision));
            Assert.All(area.Result.Targets,x=>Assert.Equal(15,x.Damage!.AppliedDamage));
            Assert.True(area.Result.Targets[0].Health!.After.Dead);
            Assert.Equal(5,area.Result.Targets[1].Health!.After.Current);
            Assert.Equal(1,(await progression.SpellcastingAsync(casterId))!.SharedSlots[0].Current);
            await Assert.ThrowsAsync<StateConflictException>(()=>combat.CastSpellAsync(encounterId,
                new(actor,"wizard","fire-bolt",null,0,[new(second,30,true)],true,true,false,revision)));
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var combat=await services.GetRequiredService<CombatService>().GetAsync(encounterId);
            Assert.Equal(0,combat.Characters.Single(x=>x.Id==target1Id).Health.Current);
            Assert.Equal(5,combat.Characters.Single(x=>x.Id==target2Id).Health.Current);
            Assert.Equal(1,(await services.GetRequiredService<ProgressionService>()
                .SpellcastingAsync(casterId))!.SharedSlots[0].Current);
            var events=await services.GetRequiredService<CampaignService>().EventsAsync(campaignId);
            Assert.Equal(2,events.Count(x=>x.Type=="CombatSpellCast"));
        }
    }

    [Fact]
    public async Task BlurPersistsAndEndsAfterFailedConcentrationSave()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.BlurTests",Guid.NewGuid().ToString("N"));
        Guid encounterId,campaignId,actor,target,weaponId;
        await using(var provider=Provider(path,18,2,18,2,18,18,4,1))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Concentration"));
            campaignId=campaign.Id;
            var progression=services.GetRequiredService<ProgressionService>();
            var wizard=await progression.CreateAsync(new(campaign.Id,"Ilyra","dwarf",null,"Medium",
                "criminal","wizard",Enum.GetValues<Ability>().ToDictionary(x=>x,_=>13),
                new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","history"],
                PreparedSpellIds:["burning-hands"],WizardSpellbookIds:["burning-hands"]));
            wizard=await progression.LevelUpAsync(wizard.Id,new("wizard",wizard.Revision));
            wizard=await progression.LevelUpAsync(wizard.Id,new("wizard",wizard.Revision,
                SubclassId:"school-of-evocation",AdditionalPreparedSpellIds:["blur"],
                AdditionalWizardSpellbookIds:["blur"]));
            Assert.Contains("blur",wizard.WizardSpellbookIds!);
            var enemy=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaign.Id,"Enemy",1,
                Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],30,10));
            var combat=services.GetRequiredService<CombatService>();
            await combat.ImportAsync(enemy.Id,new(30,["longsword"],[],[],[],[]));
            weaponId=(await combat.GrantWeaponAsync(enemy.Id,new("longsword"))).Id;
            var encounter=await combat.CreateAsync(campaign.Id,new("Blur fight")); encounterId=encounter.Id;
            actor=(await combat.AddAsync(encounterId,new(wizard.Id))).Result.Id;
            target=(await combat.AddAsync(encounterId,new(enemy.Id,CombatantKind.Monster,ZeroHpPolicy.Die))).Result.Id;
            await combat.InitiativeAsync(encounterId); await combat.StartAsync(encounterId,new());
            var revision=(await combat.GetAsync(encounterId)).Encounter.Revision;
            var cast=await combat.CastSpellAsync(encounterId,new(actor,"wizard","blur",SpellSlotPoolKind.Shared,2,
                [new(actor,0,true)],true,false,false,revision));
            Assert.NotNull(cast.Result.ActiveEffect);
            Assert.True((await combat.GetAsync(encounterId)).Encounter.ActiveSpells!.Length == 1);
            await combat.EndTurnAsync(encounterId,new(actor));
            var missed=(await combat.AttackAsync(encounterId,new(target,
                new(actor,weaponId,AttackMode.Melee,new(5,true,true))))).Result;
            Assert.Equal(AdvantageState.Disadvantage,missed.AttackRoll.AdvantageState);
            Assert.False(missed.Hit);
        }
        await using(var provider=Provider(path,18,18,4,1))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var combat=services.GetRequiredService<CombatService>();
            Assert.Single((await combat.GetAsync(encounterId)).Encounter.ActiveSpells!);
            await combat.EndTurnAsync(encounterId,new(target));
            await combat.EndTurnAsync(encounterId,new(actor));
            var hit=(await combat.AttackAsync(encounterId,new(target,
                new(actor,weaponId,AttackMode.Melee,new(5,true,true))))).Result;
            Assert.True(hit.Hit);
            Assert.Equal(4,hit.Damage!.AppliedDamage);
            Assert.Empty((await combat.GetAsync(encounterId)).Encounter.ActiveSpells!);
            var events=await services.GetRequiredService<CampaignService>().EventsAsync(campaignId);
            Assert.Contains(events,x=>x.Type=="ConcentrationChecked");
            Assert.Contains(events,x=>x.Type=="ConcentrationEnded");
        }
    }

    [Fact]
    public async Task QuickenedCantripUsesBonusActionAndSorceryPointsAtomically()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.MetamagicTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path,18,2,15,4,15,4);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Metamagic"));
        var progression=services.GetRequiredService<ProgressionService>();
        var sorcerer=await progression.CreateAsync(new(campaign.Id,"Nira","dwarf",null,"Medium",
            "criminal","sorcerer",Enum.GetValues<Ability>().ToDictionary(x=>x,_=>13),
            new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","persuasion"],
            PreparedSpellIds:["burning-hands"],KnownCantripIds:["fire-bolt"]));
        sorcerer=await progression.LevelUpAsync(sorcerer.Id,new("sorcerer",sorcerer.Revision,
            AdditionalMetamagicOptions:[MetamagicOption.QuickenedSpell,MetamagicOption.SubtleSpell]));
        Assert.Equal(2,sorcerer.SorceryPointsCurrent);
        var enemy=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaign.Id,"Enemy",1,
            Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],30,10));
        var combat=services.GetRequiredService<CombatService>();
        await combat.ImportAsync(enemy.Id,new(30,[],[],[],[],[]));
        var encounter=await combat.CreateAsync(campaign.Id,new("Duel"));
        var actor=(await combat.AddAsync(encounter.Id,new(sorcerer.Id))).Result.Id;
        var target=(await combat.AddAsync(encounter.Id,new(enemy.Id,CombatantKind.Monster,ZeroHpPolicy.Die))).Result.Id;
        await combat.InitiativeAsync(encounter.Id); await combat.StartAsync(encounter.Id,new());
        var revision=(await combat.GetAsync(encounter.Id)).Encounter.Revision;
        var quick=await combat.CastSpellAsync(encounter.Id,new(actor,"sorcerer","fire-bolt",null,0,
            [new(target,30,true)],true,true,false,revision,MetamagicOption.QuickenedSpell));
        Assert.True(quick.Result.Resources.BonusActionUsed);
        Assert.False(quick.Result.Resources.ActionUsed);
        Assert.Equal(0,quick.Result.SorceryPointsAfter);
        await Assert.ThrowsAsync<RuleViolation>(()=>combat.CastSpellAsync(encounter.Id,
            new(actor,"sorcerer","burning-hands",SpellSlotPoolKind.Shared,1,
                [new(target,10,true)],true,true,false,quick.Revision)));
        var normal=await combat.CastSpellAsync(encounter.Id,new(actor,"sorcerer","fire-bolt",null,0,
            [new(target,30,true)],true,true,false,quick.Revision));
        Assert.True(normal.Result.Resources.ActionUsed);
        Assert.Equal(22,(await combat.GetAsync(encounter.Id)).Characters.Single(x=>x.Id==enemy.Id).Health.Current);
        Assert.Equal(0,(await progression.SheetAsync(sorcerer.Id)).SorceryPointsCurrent);
        var converted=await combat.ConvertSpellSlotAsync(encounter.Id,
            new(actor,SpellSlotPoolKind.Shared,1,normal.Revision));
        Assert.Equal(1,converted.Result.SorceryPointsAfter);
        converted=await combat.ConvertSpellSlotAsync(encounter.Id,
            new(actor,SpellSlotPoolKind.Shared,1,converted.Revision));
        Assert.Equal(2,(await progression.SheetAsync(sorcerer.Id)).SorceryPointsCurrent);
        await Assert.ThrowsAsync<RuleViolation>(()=>combat.CreateSorcerySlotAsync(encounter.Id,
            new(actor,1,converted.Revision)));
        await combat.EndTurnAsync(encounter.Id,new(actor));
        await combat.EndTurnAsync(encounter.Id,new(target));
        var revision2=(await combat.GetAsync(encounter.Id)).Encounter.Revision;
        var created=await combat.CreateSorcerySlotAsync(encounter.Id,new(actor,1,revision2));
        Assert.True(created.Result.Resources.BonusActionUsed);
        Assert.Equal(0,created.Result.SorceryPointsAfter);
        Assert.Equal(2,(await progression.SpellcastingAsync(sorcerer.Id))!.SharedSlots[0].Current);
    }

    [Fact]
    public async Task FiendGrantedSpellUsesPactSlotWithoutCountingAsChosenPreparation()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.FiendSpellTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path,18,2,3,4,5,6,1);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Fiend spell"));
        var progression=services.GetRequiredService<ProgressionService>();
        var warlock=await progression.CreateAsync(new(campaign.Id,"Thane","dwarf",null,"Medium",
            "criminal","warlock",Enum.GetValues<Ability>().ToDictionary(x=>x,_=>13),
            new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","history"]));
        warlock=await progression.LevelUpAsync(warlock.Id,new("warlock",warlock.Revision));
        warlock=await progression.LevelUpAsync(warlock.Id,new("warlock",warlock.Revision,
            SubclassId:"fiend-patron"));
        Assert.Empty(warlock.PreparedSpells!);
        Assert.Contains(warlock.AlwaysPreparedSpells!,x=>x.ClassId=="warlock" && x.SpellId=="burning-hands");
        var enemy=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaign.Id,"Enemy",1,
            Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],30,10));
        var combat=services.GetRequiredService<CombatService>();
        await combat.ImportAsync(enemy.Id,new(30,[],[],[],[],[]));
        var encounter=await combat.CreateAsync(campaign.Id,new("Duel"));
        var actor=(await combat.AddAsync(encounter.Id,new(warlock.Id))).Result.Id;
        var target=(await combat.AddAsync(encounter.Id,new(enemy.Id,CombatantKind.Monster,ZeroHpPolicy.Die))).Result.Id;
        await combat.InitiativeAsync(encounter.Id); await combat.StartAsync(encounter.Id,new());
        var revision=(await combat.GetAsync(encounter.Id)).Encounter.Revision;
        var cast=await combat.CastSpellAsync(encounter.Id,new(actor,"warlock","burning-hands",
            SpellSlotPoolKind.PactMagic,2,[new(target,10,true)],true,true,false,revision));
        Assert.Equal(4,cast.Result.Targets[0].DamageOrHealingRolls.Length);
        Assert.Equal(1,(await progression.SpellcastingAsync(warlock.Id))!.PactMagicSlots!.Current);
    }

    [Fact]
    public async Task MysticArcanumCastsOnceWithoutSpendingPactMagic()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.ArcanumTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path,18,2,1,2,3,4,5,6,7,8,1);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Arcanum"));
        var progression=services.GetRequiredService<ProgressionService>();
        var warlock=await progression.CreateAsync(new(campaign.Id,"Thane","dwarf",null,"Medium",
            "criminal","warlock",Enum.GetValues<Ability>().ToDictionary(x=>x,_=>13),
            new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","history"]));
        for (var level=2;level<=11;level++)
        {
            LevelUpCharacter request=level switch
            {
                3 => new("warlock",warlock.Revision,SubclassId:"fiend-patron"),
                4 or 8 => new("warlock",warlock.Revision,FeatId:"ability-score-improvement",
                    AbilityIncreases:new() { [Ability.Charisma]=2 }),
                11 => new("warlock",warlock.Revision,MysticArcanumSpellId:"circle-of-death"),
                _ => new("warlock",warlock.Revision)
            };
            warlock=await progression.LevelUpAsync(warlock.Id,request);
        }
        Assert.Equal("circle-of-death",warlock.MysticArcanumChoices![6]);
        warlock=await progression.AcquireItemAsync(warlock.Id,new("black-pearl-powder-500gp",warlock.Revision));
        var enemy=await services.GetRequiredService<CharacterService>().CreateAsync(new(campaign.Id,"Enemy",1,
            Enum.GetValues<Ability>().ToDictionary(x=>x,_=>10),[],[],100,10));
        var combat=services.GetRequiredService<CombatService>();
        await combat.ImportAsync(enemy.Id,new(30,[],[],[],[],[]));
        var encounter=await combat.CreateAsync(campaign.Id,new("Duel"));
        var actor=(await combat.AddAsync(encounter.Id,new(warlock.Id))).Result.Id;
        var target=(await combat.AddAsync(encounter.Id,new(enemy.Id,CombatantKind.Monster,ZeroHpPolicy.Die))).Result.Id;
        await combat.InitiativeAsync(encounter.Id); await combat.StartAsync(encounter.Id,new());
        var revision=(await combat.GetAsync(encounter.Id)).Encounter.Revision;
        var cast=await combat.CastSpellAsync(encounter.Id,new(actor,"warlock","circle-of-death",null,6,
            [new(target,60,true,DistanceFromAreaCenterFeet:10)],true,true,true,revision,
            AreaCenterDistanceFeet:50));
        Assert.True(cast.Result.MysticArcanumSpent);
        Assert.Equal(36,cast.Result.Targets[0].Damage!.AppliedDamage);
        Assert.Equal(3,(await progression.SpellcastingAsync(warlock.Id))!.PactMagicSlots!.Current);
        Assert.Contains(6,(await progression.SheetAsync(warlock.Id)).MysticArcanumSpentLevels!);
        await combat.EndTurnAsync(encounter.Id,new(actor));
        await combat.EndTurnAsync(encounter.Id,new(target));
        revision=(await combat.GetAsync(encounter.Id)).Encounter.Revision;
        await Assert.ThrowsAsync<RuleViolation>(()=>combat.CastSpellAsync(encounter.Id,
            new(actor,"warlock","circle-of-death",null,6,
                [new(target,60,true,DistanceFromAreaCenterFeet:10)],true,true,true,revision,
                AreaCenterDistanceFeet:50)));
    }
}
