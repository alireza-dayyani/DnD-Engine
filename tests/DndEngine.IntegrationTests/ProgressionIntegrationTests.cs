using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Progression;
using DndEngine.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Nodes;

namespace DndEngine.IntegrationTests;

public sealed class ProgressionIntegrationTests
{
    private static ServiceProvider Provider(string path, params int[] rolls) => new ServiceCollection().AddLogging()
        .AddDndEngine(path).AddSingleton<IDiceRoller>(new FixedDiceRoller(rolls))
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true });
    private static CreateSrdCharacter Fighter(Guid campaign) => new(campaign,"Mira","dwarf",null,"Medium","criminal","fighter",
        new() { [Ability.Strength]=15,[Ability.Dexterity]=14,[Ability.Constitution]=14,[Ability.Intelligence]=10,[Ability.Wisdom]=12,[Ability.Charisma]=8 },
        new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["athletics","perception"],
        StartingItemIds:["chain-mail","greatsword"],MasteredWeaponIds:["greatsword"],FightingStyleFeat:"defense");

    [Fact]
    public async Task MulticlassSpellSlotsPersistAndRecoverOnTheirOwnRestIntervals()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.SpellSlotTests",Guid.NewGuid().ToString("N"));
        Guid id; long revision;
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Spell slot test"));
            var request=new CreateSrdCharacter(campaign.Id,"Arin","dwarf",null,"Medium","criminal","wizard",
                Enum.GetValues<Ability>().ToDictionary(a=>a,_=>13),
                new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","history"]);
            var service=services.GetRequiredService<ProgressionService>();
            var sheet=await service.CreateAsync(request);
            sheet=await service.LevelUpAsync(sheet.Id,new("warlock",sheet.Revision));
            id=sheet.Id;
            Assert.Equal(2,sheet.Spellcasting!.SharedSlots.Single().Current);
            Assert.Equal(1,sheet.Spellcasting.PactMagicSlots!.Current);
            sheet=await service.SpendSpellSlotAsync(id,new(SpellSlotPoolKind.Shared,1,sheet.Revision));
            sheet=await service.SpendSpellSlotAsync(id,new(SpellSlotPoolKind.PactMagic,1,sheet.Revision));
            Assert.Equal(1,sheet.Spellcasting!.SharedSlots.Single().Current);
            Assert.Equal(0,sheet.Spellcasting.PactMagicSlots!.Current);
            await Assert.ThrowsAsync<RuleViolation>(()=>service.SpendSpellSlotAsync(id,new(SpellSlotPoolKind.PactMagic,1,sheet.Revision)));
            await Assert.ThrowsAsync<StateConflictException>(()=>service.SpendSpellSlotAsync(id,new(SpellSlotPoolKind.Shared,1,sheet.Revision-1)));
            revision=sheet.Revision;
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var service=scope.ServiceProvider.GetRequiredService<ProgressionService>();
            var sheet=await service.SheetAsync(id);
            Assert.Equal(revision,sheet.Revision);
            Assert.Equal(1,sheet.Spellcasting!.SharedSlots.Single().Current);
            Assert.Equal(0,sheet.Spellcasting.PactMagicSlots!.Current);
            var shortRest=await service.ShortRestAsync(id,new([],sheet.Revision));
            Assert.Equal(1,shortRest.Sheet.Spellcasting!.SharedSlots.Single().Current);
            Assert.Equal(1,shortRest.Sheet.Spellcasting.PactMagicSlots!.Current);
            Assert.Contains("Pact Magic slots restored",shortRest.OtherChanges);
            var longRest=await service.LongRestAsync(id,new(shortRest.Sheet.Revision));
            Assert.Equal(2,longRest.Sheet.Spellcasting!.SharedSlots.Single().Current);
            Assert.Equal(1,longRest.Sheet.Spellcasting.PactMagicSlots!.Current);
            var events=await scope.ServiceProvider.GetRequiredService<CampaignService>().EventsAsync(longRest.Sheet.CampaignId);
            Assert.Equal(2,events.Count(x=>x.Type=="SpellSlotSpent"));
        }
    }

    [Fact]
    public async Task PreparedCureWoundsCanUsePactMagicAndPersistsHealingWithTheSlot()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.SpellCastTests",Guid.NewGuid().ToString("N"));
        Guid id; int healedMaximum;
        await using(var provider=Provider(path,3,4))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Spell cast test"));
            var request=new CreateSrdCharacter(campaign.Id,"Elen","dwarf",null,"Medium","criminal","cleric",
                Enum.GetValues<Ability>().ToDictionary(a=>a,_=>13),
                new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["history","insight"],
                PreparedSpellIds:["cure-wounds"]);
            var service=services.GetRequiredService<ProgressionService>();
            var sheet=await service.CreateAsync(request);
            Assert.Equal(SpellPackVersions.Current,sheet.SpellPackVersion);
            Assert.Contains(sheet.PreparedSpells!,x=>x.ClassId=="cleric" && x.SpellId=="cure-wounds");
            sheet=await service.LevelUpAsync(sheet.Id,new("warlock",sheet.Revision));
            id=sheet.Id; healedMaximum=sheet.MaximumHp;
            await services.GetRequiredService<MechanicsService>().DamageAsync(id,new(6));
            sheet=await service.SheetAsync(id);
            await Assert.ThrowsAsync<RuleViolation>(()=>service.CastPreparedSpellAsync(id,
                new("cleric","cure-wounds",SpellSlotPoolKind.PactMagic,1,sheet.Revision,false)));
            var result=await service.CastPreparedSpellAsync(id,
                new("cleric","cure-wounds",SpellSlotPoolKind.PactMagic,1,sheet.Revision,true));
            Assert.Equal([3,4],result.Rolls);
            Assert.Equal(6,result.HitPointsRegained);
            Assert.Equal(healedMaximum,result.Sheet.CurrentHp);
            Assert.Equal(0,result.Sheet.Spellcasting!.PactMagicSlots!.Current);
            Assert.Equal(2,result.Sheet.Spellcasting.SharedSlots.Single().Current);
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var service=services.GetRequiredService<ProgressionService>();
            var sheet=await service.SheetAsync(id);
            Assert.Equal(healedMaximum,sheet.CurrentHp);
            Assert.Equal(0,sheet.Spellcasting!.PactMagicSlots!.Current);
            Assert.Contains(sheet.PreparedSpells!,x=>x.SpellId=="cure-wounds");
            var events=await services.GetRequiredService<CampaignService>().EventsAsync(sheet.CampaignId);
            Assert.Contains(events,x=>x.Type=="SpellCast");
            Assert.Equal(2,(await service.SpellChoicesAsync()).Length);
        }
    }

    [Fact]
    public async Task ExistingUnversionedCharacterKeepsInitialSpellPackAfterRestart()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.SpellPackTests",Guid.NewGuid().ToString("N"));
        Guid id;
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Pinned spell test"));
            var request=new CreateSrdCharacter(campaign.Id,"Elen","dwarf",null,"Medium","criminal","cleric",
                Enum.GetValues<Ability>().ToDictionary(a=>a,_=>13),
                new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["history","insight"],
                PreparedSpellIds:["cure-wounds"]);
            id=(await services.GetRequiredService<ProgressionService>().CreateAsync(request)).Id;
            var db=services.GetRequiredService<CampaignDbContext>();
            var row=await db.Progressions.SingleAsync(x=>x.CharacterId==id);
            var state=JsonNode.Parse(row.StateJson)!.AsObject();
            Assert.True(state.Remove("spellPackVersion"));
            row.StateJson=state.ToJsonString();
            await db.SaveChangesAsync();
        }
        await using(var provider=Provider(path,3,4))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var service=services.GetRequiredService<ProgressionService>();
            var sheet=await service.SheetAsync(id);
            Assert.Equal(SpellPackVersions.Initial,sheet.SpellPackVersion);
            var catalog=services.GetRequiredService<ISpellCatalog>();
            Assert.Single(await catalog.GetAsync(Ruleset.Current,SpellPackVersions.Initial,default));
            Assert.Equal(2,(await catalog.GetAsync(Ruleset.Current,SpellPackVersions.Current,default)).Length);
            await services.GetRequiredService<MechanicsService>().DamageAsync(id,new(6));
            sheet=await service.SheetAsync(id);
            var cast=await service.CastPreparedSpellAsync(id,
                new("cleric","cure-wounds",SpellSlotPoolKind.Shared,1,sheet.Revision,true));
            Assert.Equal(6,cast.HitPointsRegained);
            Assert.Equal(SpellPackVersions.Initial,cast.Sheet.SpellPackVersion);
        }
    }

    [Fact]
    public async Task ExistingRulesDatabaseMigratesWithoutChangingItsPinnedInitialPack()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.SpellPackMigrationTests",Guid.NewGuid().ToString("N"));
        string initialHash;
        await using(var provider=Provider(path))
        {
            await using var scope=provider.CreateAsyncScope();
            var rules=scope.ServiceProvider.GetRequiredService<RulesDbContext>();
            await rules.Database.MigrateAsync("20261005204700_SpellContent");
            await RulesCatalog.ImportAsync(rules);
            var json=JsonSerializer.Serialize(new[] { new {
                Id="cure-wounds",Name="Cure Wounds",Level=1,
                ClassIds=new[]{"bard","cleric","druid","paladin","ranger"},
                CastingTime="Action",Range="Touch",Components="V,S",
                Effect=SpellEffectKind.SelfHealing,Source="SRD 5.2.1 p. 121"
            } },new JsonSerializerOptions(JsonSerializerDefaults.Web) { Converters = { new JsonStringEnumConverter() } });
            initialHash=Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(json)));
            rules.SpellContent.Add(new() { RulesetId=Ruleset.Current.Id,Version=Ruleset.Current.Version,
                ContentHash=initialHash,DataJson=json });
            await rules.SaveChangesAsync();
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var rules=scope.ServiceProvider.GetRequiredService<RulesDbContext>();
            Assert.Equal(initialHash,(await rules.SpellContent.SingleAsync()).ContentHash);
            Assert.Single(await rules.SpellPacks.ToArrayAsync());
            Assert.False(rules.Database.HasPendingModelChanges());
        }
    }

    [Fact]
    public async Task ClericReplacesPreparedSpellOnLongRestAndChoicePersists()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.SpellRestTests",Guid.NewGuid().ToString("N"));
        Guid id;
        await using(var provider=Provider(path,2,3))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Spell rest test"));
            var request=new CreateSrdCharacter(campaign.Id,"Elen","dwarf",null,"Medium","criminal","cleric",
                Enum.GetValues<Ability>().ToDictionary(a=>a,_=>13),
                new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["history","insight"],
                PreparedSpellIds:["cure-wounds"]);
            var service=services.GetRequiredService<ProgressionService>();
            var sheet=await service.CreateAsync(request);
            id=sheet.Id;
            var rest=await service.LongRestAsync(id,new(sheet.Revision,SpellReplacements:[
                new("cleric","cure-wounds","healing-word")]));
            sheet=rest.Sheet;
            Assert.Contains("Prepared spells replaced",rest.OtherChanges);
            Assert.Equal("healing-word",Assert.Single(sheet.PreparedSpells!).SpellId);
            await Assert.ThrowsAsync<RuleViolation>(()=>service.CastPreparedSpellAsync(id,
                new("cleric","cure-wounds",SpellSlotPoolKind.Shared,1,sheet.Revision,true)));
            await services.GetRequiredService<MechanicsService>().DamageAsync(id,new(6));
            sheet=await service.SheetAsync(id);
            var cast=await service.CastPreparedSpellAsync(id,
                new("cleric","healing-word",SpellSlotPoolKind.Shared,1,sheet.Revision,true));
            Assert.Equal([2,3],cast.Rolls);
            Assert.Equal(6,cast.HitPointsRegained);
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var sheet=await scope.ServiceProvider.GetRequiredService<ProgressionService>().SheetAsync(id);
            Assert.Equal("healing-word",Assert.Single(sheet.PreparedSpells!).SpellId);
            Assert.Equal(1,sheet.Spellcasting!.SharedSlots.Single().Current);
        }
    }

    [Fact]
    public async Task BardReplacesSpellOnBardLevelNotLongRest()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.BardSpellTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope();
        var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Bard spell test"));
        var service=services.GetRequiredService<ProgressionService>();
        var bard=(await service.ChoicesAsync()).Classes.Single(x=>x.Id=="bard");
        var request=new CreateSrdCharacter(campaign.Id,"Elen","dwarf",null,"Medium","criminal","bard",
            Enum.GetValues<Ability>().ToDictionary(a=>a,_=>13),
            new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","insight","perception"],
            ClassTools:bard.ToolChoiceOptions!.Take(3).ToArray(),PreparedSpellIds:["cure-wounds"]);
        var sheet=await service.CreateAsync(request);
        await Assert.ThrowsAsync<RuleViolation>(()=>service.LongRestAsync(sheet.Id,
            new(sheet.Revision,SpellReplacements:[new("bard","cure-wounds","healing-word")])));
        sheet=await service.LevelUpAsync(sheet.Id,new("bard",sheet.Revision,
            SpellReplacement:new("bard","cure-wounds","healing-word")));
        Assert.Equal("healing-word",Assert.Single(sheet.PreparedSpells!).SpellId);
        sheet=await service.LevelUpAsync(sheet.Id,new("cleric",sheet.Revision,
            AdditionalPreparedSpellIds:["cure-wounds"]));
        Assert.Contains(sheet.PreparedSpells!,x=>x.ClassId=="bard" && x.SpellId=="healing-word");
        Assert.Contains(sheet.PreparedSpells!,x=>x.ClassId=="cleric" && x.SpellId=="cure-wounds");
        Assert.Equal(4,sheet.Spellcasting!.Classes.Single(x=>x.ClassId=="cleric").PreparedMaximum);
    }

    [Fact]
    public async Task ClassLevelUpAddsAnEligiblePreparedSpellAndPersistsItsCapacity()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.SpellAdditionTests",Guid.NewGuid().ToString("N"));
        Guid id;
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Spell addition test"));
            var request=new CreateSrdCharacter(campaign.Id,"Elen","dwarf",null,"Medium","criminal","cleric",
                Enum.GetValues<Ability>().ToDictionary(a=>a,_=>13),
                new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["history","insight"],
                PreparedSpellIds:["cure-wounds"]);
            var service=services.GetRequiredService<ProgressionService>();
            var sheet=await service.CreateAsync(request);
            id=sheet.Id;
            await Assert.ThrowsAsync<RuleViolation>(()=>service.LevelUpAsync(id,new("cleric",sheet.Revision,
                AdditionalPreparedSpellIds:["cure-wounds"])));
            sheet=await service.LevelUpAsync(id,new("cleric",sheet.Revision,
                AdditionalPreparedSpellIds:["healing-word"]));
            Assert.Equal(2,sheet.PreparedSpells!.Length);
            var casting=sheet.Spellcasting!.Classes.Single(x=>x.ClassId=="cleric");
            Assert.Equal(2,casting.PreparedCount);
            Assert.Equal(5,casting.PreparedMaximum);
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var sheet=await scope.ServiceProvider.GetRequiredService<ProgressionService>().SheetAsync(id);
            Assert.Contains(sheet.PreparedSpells!,x=>x.SpellId=="healing-word" && x.ClassId=="cleric");
            Assert.Equal(5,sheet.Spellcasting!.Classes.Single(x=>x.ClassId=="cleric").PreparedMaximum);
        }
    }

    [Fact]
    public async Task CreationProgressionRestAndRestartRetainDerivedState()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.ProgressionTests",Guid.NewGuid().ToString("N"));
        Guid id; Guid campaignId;
        await using(var provider=Provider(path,7))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Progression test"));
            campaignId=campaign.Id;
            var service=services.GetRequiredService<ProgressionService>();
            var sheet=await service.CreateAsync(Fighter(campaign.Id)); id=sheet.Id;
            Assert.Equal(1,sheet.Level); Assert.Equal(13,sheet.MaximumHp);
            Assert.Equal(16,sheet.Abilities[Ability.Dexterity]);
            Assert.Equal(13,sheet.ArmorClass.Total); // Chain Mail is not equipped yet.
            Assert.Contains(sheet.Proficiencies,x=>x.Kind==DndEngine.Domain.Progression.ProficiencyKind.Save && x.Id=="Strength");
            Assert.Contains("greatsword",sheet.WeaponMasteries);
            var armor=sheet.Inventory.Single(x=>x.DefinitionId=="chain-mail");
            sheet=await service.EquipAsync(id,new(armor.Id,sheet.Revision),true);
            Assert.Equal(17,sheet.ArmorClass.Total);
            sheet=await service.LevelUpAsync(id,new("fighter",sheet.Revision));
            Assert.Equal(2,sheet.Level); Assert.Contains(sheet.Resources,x=>x.Id=="action-surge");
            Assert.Equal(2,sheet.HitDice.Single().Total);
            var rest=await service.ShortRestAsync(id,new([10],sheet.Revision));
            Assert.Single(rest.HitDieRolls); Assert.Equal(1,rest.Sheet.HitDice.Single().Available);
            var longRest=await service.LongRestAsync(id,new(rest.Sheet.Revision));
            Assert.Equal(2,longRest.Sheet.HitDice.Single().Available);
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope();
            var services=scope.ServiceProvider;
            var sheet=await services.GetRequiredService<ProgressionService>().SheetAsync(id);
            Assert.Equal(2,sheet.Level); Assert.Equal(17,sheet.ArmorClass.Total);
            Assert.Equal(2,sheet.HitDice.Single().Available);
            Assert.Equal(5,(await services.GetRequiredService<RulesDbContext>().Database.GetAppliedMigrationsAsync()).Count());
            Assert.Equal(3,(await services.GetRequiredService<CampaignDbContext>().Database.GetAppliedMigrationsAsync()).Count());
            Assert.False(services.GetRequiredService<RulesDbContext>().Database.HasPendingModelChanges());
            var events=await services.GetRequiredService<CampaignService>().EventsAsync(campaignId);
            Assert.Contains(events,x=>x.Type=="LevelGained");
        }
    }

    [Fact]
    public async Task FailedLevelUpAndStaleRevisionLeaveNoPartialState()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.ProgressionTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope();
        var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Atomicity test"));
        var service=services.GetRequiredService<ProgressionService>();
        var sheet=await service.CreateAsync(Fighter(campaign.Id));
        var before=(await services.GetRequiredService<CampaignService>().EventsAsync(campaign.Id)).Count;
        var invalid=Fighter(campaign.Id) with { BaseAbilities=new(Fighter(campaign.Id).BaseAbilities) { [Ability.Dexterity]=20 } };
        await Assert.ThrowsAsync<RuleViolation>(()=>service.CreateAsync(invalid));
        var combatService=services.GetRequiredService<CombatService>();
        await Assert.ThrowsAsync<RuleViolation>(()=>combatService.ImportAsync(sheet.Id,sheet.CombatCapabilities));
        await Assert.ThrowsAsync<RuleViolation>(()=>combatService.GrantWeaponAsync(sheet.Id,new("greatsword")));
        await Assert.ThrowsAsync<RuleViolation>(()=>service.LevelUpAsync(sheet.Id,new("wizard",sheet.Revision)));
        await Assert.ThrowsAsync<StateConflictException>(()=>service.LevelUpAsync(sheet.Id,new("fighter",42)));
        Assert.Equal(1,(await service.SheetAsync(sheet.Id)).Level);
        Assert.Equal(before,(await services.GetRequiredService<CampaignService>().EventsAsync(campaign.Id)).Count);
    }

    [Fact]
    public async Task AllSrdSpeciesAndBackgroundsCreateWithValidatedChoices()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.ProgressionTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope();
        var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Species matrix"));
        var service=services.GetRequiredService<ProgressionService>();
        var rules=await service.ChoicesAsync();
        Assert.Equal(9,rules.Species.Length); Assert.Equal(4,rules.Backgrounds.Length); Assert.Equal(12,rules.Classes.Length);
        foreach (var species in rules.Species)
        foreach (var background in rules.Backgrounds)
        {
            var bonuses=new Dictionary<Ability,int> { [background.AbilityChoices[0]]=2,[background.AbilityChoices[1]]=1 };
            var skills=new[] {"athletics","perception"};
            if (background.Skills.Contains("athletics")) skills=["insight","perception"];
            if (background.Skills.Contains("insight")) skills=["athletics","perception"];
            var request=new CreateSrdCharacter(campaign.Id,$"{species.Id}-{background.Id}",species.Id,
                species.Variants.FirstOrDefault()?.Id,species.Sizes[0],background.Id,"fighter",
                Enum.GetValues<Ability>().ToDictionary(a=>a,_=>13),bonuses,skills,
                species.Id=="human" ? background.OriginFeat=="savage-attacker" ? "alert" : "savage-attacker" : null,
                SpeciesSkill: species.Id is "elf" or "human" ? "survival" : null,FightingStyleFeat:"defense",
                BackgroundToolId:background.Id=="soldier" ? "dice-set" : null);
            var sheet=await service.CreateAsync(request);
            Assert.Equal(species.Speed+(species.Id=="elf" && species.Variants[0].Id=="wood" ? 5 : 0),sheet.Speed);
            Assert.Equal(1,sheet.Level); Assert.Equal(background.OriginFeat,sheet.Feats[0]);
            Assert.Equal(13+bonuses.GetValueOrDefault(background.AbilityChoices[0]),sheet.Abilities[background.AbilityChoices[0]]);
        }
    }

    [Fact]
    public async Task EverySrdClassCreatesWithItsOwnHitDieSkillsAndTraining()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.ProgressionTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope();
        var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Class matrix"));
        var service=services.GetRequiredService<ProgressionService>();
        var rules=await service.ChoicesAsync();
        foreach (var definition in rules.Classes)
        {
            var choices=definition.SkillChoices.Contains("*") ?
                new[] {"athletics","arcana","history","perception"} : definition.SkillChoices;
            var selected=choices.Except(["sleight-of-hand","stealth"]).Take(definition.SkillChoiceCount).ToArray();
            var request=new CreateSrdCharacter(campaign.Id,definition.Id,"dwarf",null,"Medium","criminal",definition.Id,
                Enum.GetValues<Ability>().ToDictionary(a=>a,_=>13),
                new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },selected,
                ClassTools:(definition.ToolChoiceOptions ?? []).Take(definition.ToolChoiceCount).ToArray(),
                FightingStyleFeat:definition.Features.Any(x=>x.Id=="fighting-style" && x.Level==1) ? "defense" : null);
            var sheet=await service.CreateAsync(request);
            Assert.Equal(definition.HitDie+3,sheet.MaximumHp);
            Assert.Equal(definition.HitDie,sheet.HitDice.Single().Sides);
            Assert.Equal(2,sheet.ProficiencyBonus);
            Assert.Contains(sheet.Proficiencies,x=>x.Kind==DndEngine.Domain.Progression.ProficiencyKind.Save &&
                x.Id==definition.Saves[0].ToString());
            Assert.Equal(definition.SkillChoiceCount,selected.Length);
        }
    }

    [Fact]
    public async Task ArmorMasteryResourceAndHitDiceRulesAreDerivedAndDeterministic()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.ProgressionTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path,4);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope();
        var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Derived rules"));
        var service=services.GetRequiredService<ProgressionService>();
        var sheet=await service.CreateAsync(Fighter(campaign.Id));
        Assert.Contains("greatsword",sheet.WeaponMasteries);
        Assert.Contains("longbow",sheet.CombatCapabilities.WeaponProficiencies);
        Assert.Equal(13,sheet.ArmorClass.Total);
        Assert.Contains(sheet.Skills,x=>x.Id=="athletics" && x.Modifier.Total==4);
        var armor=sheet.Inventory.Single(x=>x.DefinitionId=="chain-mail");
        sheet=await service.EquipAsync(sheet.Id,new(armor.Id,sheet.Revision),true);
        Assert.Equal(17,sheet.ArmorClass.Total);
        Assert.Contains(sheet.ArmorClass.Parts,x=>x.Source=="chain-mail" && x.Value==16);
        sheet=await service.LevelUpAsync(sheet.Id,new("fighter",sheet.Revision));
        sheet=await service.SpendResourceAsync(sheet.Id,new("action-surge",1,sheet.Revision));
        Assert.Equal(0,sheet.Resources.Single(x=>x.Id=="action-surge").Current);
        var rest=await service.ShortRestAsync(sheet.Id,new([10],sheet.Revision));
        Assert.Equal(4,rest.HitDieRolls[0]);
        Assert.Equal(1,rest.Sheet.Resources.Single(x=>x.Id=="action-surge").Current);
        Assert.Equal(1,rest.Sheet.HitDice.Single().Available);
        await Assert.ThrowsAsync<StateConflictException>(()=>service.SpendResourceAsync(sheet.Id,new("action-surge",1,sheet.Revision)));
    }

    [Fact]
    public async Task EquippedArmorAppliesStealthDisadvantageToExistingCheckEndpoint()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.ProgressionTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path,18,4);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope();
        var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Armor check"));
        var service=services.GetRequiredService<ProgressionService>();
        var sheet=await service.CreateAsync(Fighter(campaign.Id));
        var armor=sheet.Inventory.Single(x=>x.DefinitionId=="chain-mail");
        sheet=await service.EquipAsync(sheet.Id,new(armor.Id,sheet.Revision),true);
        var check=await services.GetRequiredService<MechanicsService>().SkillCheckAsync(sheet.Id,new("stealth",10));
        Assert.Equal(AdvantageState.Disadvantage,check.AdvantageState);
        Assert.Equal(4,check.SelectedRoll);
    }

    [Fact]
    public async Task UntrainedArmorUsesItsAcButImposesD20DisadvantageAndBlocksCasting()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.ProgressionTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path,18,4);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope();
        var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Untrained armor"));
        var service=services.GetRequiredService<ProgressionService>();
        var request=Fighter(campaign.Id) with { ClassId="sorcerer",ClassSkills=["arcana","deception"],
            StartingItemIds=[],MasteredWeaponIds=[],FightingStyleFeat=null };
        var sheet=await service.CreateAsync(request);
        sheet=await service.AcquireItemAsync(sheet.Id,new("shield",sheet.Revision));
        sheet=await service.EquipAsync(sheet.Id,new(sheet.Inventory.Single(x=>x.DefinitionId=="shield").Id,sheet.Revision),true);
        Assert.Equal(13,sheet.ArmorClass.Total);
        Assert.False(sheet.UntrainedArmorPenalty);
        Assert.False(sheet.SpellcastingBlockedByArmor);
        sheet=await service.AcquireItemAsync(sheet.Id,new("chain-mail",sheet.Revision));
        var armor=sheet.Inventory.Single(x=>x.DefinitionId=="chain-mail");
        sheet=await service.EquipAsync(sheet.Id,new(armor.Id,sheet.Revision),true);
        Assert.Equal(16,sheet.ArmorClass.Total);
        Assert.True(sheet.UntrainedArmorPenalty);
        Assert.True(sheet.SpellcastingBlockedByArmor);
        var check=await services.GetRequiredService<MechanicsService>().AbilityCheckAsync(sheet.Id,new(Ability.Strength,10));
        Assert.Equal(AdvantageState.Disadvantage,check.AdvantageState);
        Assert.Equal(4,check.SelectedRoll);
        Assert.Equal(16,sheet.ArmorClass.Total);
    }

    [Fact]
    public async Task LongRestCanChangeMasteryChoicesButRejectsIneligibleSelectionsAtomically()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.ProgressionTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope();
        var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Mastery change"));
        var service=services.GetRequiredService<ProgressionService>();
        var sheet=await service.CreateAsync(Fighter(campaign.Id));
        await Assert.ThrowsAsync<RuleViolation>(()=>service.LongRestAsync(sheet.Id,new(sheet.Revision,["greatsword","longbow","dagger","spear"])));
        Assert.Equal(sheet.Revision,(await service.SheetAsync(sheet.Id)).Revision);
        var rest=await service.LongRestAsync(sheet.Id,new(sheet.Revision,["longbow"]));
        Assert.Equal(["longbow"],rest.Sheet.WeaponMasteries);
    }

    [Fact]
    public async Task FighterProgressesToTwentyWithChoicesAndDerivedAttackCount()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.ProgressionTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path);
        await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope();
        var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Level twenty"));
        var service=services.GetRequiredService<ProgressionService>();
        var sheet=await service.CreateAsync(Fighter(campaign.Id));
        for (var next=2;next<=20;next++)
        {
            var featLevel=next is 4 or 6 or 8 or 12 or 14 or 16;
            var increases=featLevel ? new Dictionary<Ability,int> { [next is 4 or 6 ? Ability.Intelligence :
                next is 8 or 12 ? Ability.Wisdom : Ability.Charisma]=2 } :
                next==19 ? new Dictionary<Ability,int> { [Ability.Dexterity]=1 } : null;
            sheet=await service.LevelUpAsync(sheet.Id,new("fighter",sheet.Revision,
                FeatId:next==19 ? "boon-of-combat-prowess" : featLevel ? "ability-score-improvement" : null,
                AbilityIncreases:increases,SubclassId:next==3 ? "champion" : null));
        }
        Assert.Equal(20,sheet.Level); Assert.Equal(6,sheet.ProficiencyBonus);
        Assert.Equal(4,sheet.CombatCapabilities.AttacksPerAction);
        Assert.Equal("champion",sheet.SubclassIds!["fighter"]);
        Assert.Equal(17,sheet.Abilities[Ability.Dexterity]);
        Assert.Equal(20,sheet.HitDice.Sum(x=>x.Total));
    }
}
