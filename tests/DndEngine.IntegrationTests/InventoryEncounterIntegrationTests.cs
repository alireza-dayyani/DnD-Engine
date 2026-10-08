using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Inventory;
using DndEngine.Infrastructure;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace DndEngine.IntegrationTests;

public sealed class InventoryEncounterIntegrationTests
{
    private static ServiceProvider Provider(string path,params int[] rolls) => new ServiceCollection()
        .AddLogging().AddDndEngine(path).AddSingleton<IDiceRoller>(new FixedDiceRoller(rolls))
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true });

    private static async Task<Guid> Fighter(IServiceProvider services,Guid campaignId,string name)
    {
        var sheet=await services.GetRequiredService<ProgressionService>().CreateAsync(new(campaignId,
            name,"dwarf",null,"Medium","criminal","fighter",
            new() { [Ability.Strength]=15,[Ability.Dexterity]=14,[Ability.Constitution]=14,
                [Ability.Intelligence]=10,[Ability.Wisdom]=12,[Ability.Charisma]=8 },
            new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },
            ["athletics","perception"],StartingItemIds:["chain-mail","greatsword"],
            MasteredWeaponIds:["greatsword"],FightingStyleFeat:"defense"));
        return sheet.Id;
    }

    [Fact]
    public async Task InventoryTransferDropPickupEquipmentAndConsumptionPersistWithoutDuplication()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.InventoryTests",Guid.NewGuid().ToString("N"));
        Guid campaignId,firstId,secondId;
        await using(var provider=Provider(path,2,3))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            Assert.False(services.GetRequiredService<CampaignDbContext>().Database.HasPendingModelChanges());
            Assert.False(services.GetRequiredService<RulesDbContext>().Database.HasPendingModelChanges());
            campaignId=(await services.GetRequiredService<CampaignService>().CreateAsync(new("Inventory"))).Id;
            firstId=await Fighter(services,campaignId,"Mira");
            secondId=await Fighter(services,campaignId,"Nora");
            var inventory=services.GetRequiredService<InventoryService>();
            var first=await inventory.GetAsync(firstId);
            first=await inventory.AddAsync(firstId,new("potion-of-healing",3,first.Revision));
            var potion=first.Items.Single(x=>x.DefinitionId=="potion-of-healing");
            Assert.Equal(3,potion.Quantity);
            var second=await inventory.GetAsync(secondId);
            await Assert.ThrowsAsync<RuleViolation>(()=>inventory.TransferAsync(firstId,
                new(secondId,potion.Id,4,first.Revision,second.Revision)));
            Assert.Equal(3,(await inventory.GetAsync(firstId)).Items.Single(x=>x.Id==potion.Id).Quantity);
            first=await inventory.TransferAsync(firstId,new(secondId,potion.Id,2,
                first.Revision,second.Revision));
            second=await inventory.GetAsync(secondId);
            Assert.Equal(1,first.Items.Single(x=>x.Id==potion.Id).Quantity);
            Assert.Equal(2,second.Items.Single(x=>x.DefinitionId=="potion-of-healing").Quantity);
            await Assert.ThrowsAsync<StateConflictException>(()=>inventory.TransferAsync(firstId,
                new(secondId,potion.Id,1,first.Revision-1,second.Revision)));
            var dropped=await inventory.DropAsync(secondId,new(
                second.Items.Single(x=>x.DefinitionId=="potion-of-healing").Id,1,second.Revision));
            Assert.Single(await inventory.ListDroppedAsync(campaignId));
            var beforeOwner=Assert.IsType<Character>(await services.GetRequiredService<ICampaignStore>().GetCharacterAsync(firstId,default));
            var beforeInventory=Assert.IsType<InventoryState>(await services.GetRequiredService<IInventoryStore>().GetAsync(firstId,default));
            var beforeProgression=Assert.IsType<DndEngine.Domain.Progression.ProgressionState>(await services.GetRequiredService<IProgressionStore>().GetAsync(firstId,default));
            var beforeProfile=Assert.IsType<CombatProfile>(await services.GetRequiredService<ICombatStore>().GetProfileAsync(firstId,default));
            var beforeEvents=(await services.GetRequiredService<CampaignService>().EventsAsync(campaignId)).Count;
            await Assert.ThrowsAsync<StateConflictException>(()=>services.GetRequiredService<IInventoryStore>()
                .SaveAsync([new InventoryOwnerUpdate(beforeOwner,beforeInventory with {
                    CopperPieces=beforeInventory.CopperPieces+1 },
                    beforeProgression with {
                        CurrencyCopper=beforeInventory.CopperPieces+1 },
                    beforeProfile)],
                    [new CampaignEvent(0,Guid.NewGuid(),campaignId,firstId,"Probe",
                        DateTimeOffset.UtcNow,Ruleset.Current,2,beforeOwner.Revision+1,
                        JsonSerializer.SerializeToElement(new { failed=true }))],default,
                    [new DroppedItem(dropped.Id,campaignId,secondId,dropped.DefinitionId,1)]));
            Assert.Equal(beforeOwner.Revision,(await services.GetRequiredService<ICampaignStore>()
                .GetCharacterAsync(firstId,default))!.Revision);
            Assert.Equal(beforeInventory.CopperPieces,(await inventory.GetAsync(firstId)).CopperPieces);
            Assert.Equal(beforeEvents,(await services.GetRequiredService<CampaignService>()
                .EventsAsync(campaignId)).Count);
            first=await inventory.GetAsync(firstId);
            first=await inventory.PickupAsync(firstId,new(dropped.Id,first.Revision));
            Assert.Empty(await inventory.ListDroppedAsync(campaignId));
            Assert.Equal(2,first.Items.Single(x=>x.DefinitionId=="potion-of-healing").Quantity);
            first=await inventory.CurrencyAsync(firstId,new(550,first.Revision));
            await Assert.ThrowsAsync<RuleViolation>(()=>inventory.CurrencyAsync(firstId,new(-551,first.Revision)));
            var armor=first.Items.Single(x=>x.DefinitionId=="chain-mail");
            if (armor.Equipped) first=await inventory.EquipAsync(firstId,new(armor.Id,false,first.Revision));
            first=await inventory.EquipAsync(firstId,new(armor.Id,true,first.Revision));
            Assert.True(first.Items.Single(x=>x.Id==armor.Id).Equipped);
            await services.GetRequiredService<MechanicsService>().DamageAsync(firstId,new(8));
            first=await inventory.GetAsync(firstId);
            var used=await inventory.UseAsync(firstId,new(potion.Id,firstId,first.Revision));
            Assert.Equal(7,used.HitPointsRegained);
            Assert.Single(used.Inventory.Items,x=>x.DefinitionId=="potion-of-healing");
            Assert.Equal(1,used.Inventory.Items.Single(x=>x.DefinitionId=="potion-of-healing").Quantity);
            Assert.Equal(used.Inventory.Items.Length,
                (await services.GetRequiredService<ProgressionService>().SheetAsync(firstId)).Inventory.Length);
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var inventory=scope.ServiceProvider.GetRequiredService<InventoryService>();
            Assert.Equal(550,(await inventory.GetAsync(firstId)).CopperPieces);
            Assert.Equal(1,(await inventory.GetAsync(firstId)).Items.Single(x=>x.DefinitionId=="potion-of-healing").Quantity);
            Assert.Equal(1,(await inventory.GetAsync(secondId)).Items.Single(x=>x.DefinitionId=="potion-of-healing").Quantity);
        }
    }

    [Fact]
    public async Task CombatPotionCompletionXpAndLootAreDurable()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.RewardTests",Guid.NewGuid().ToString("N"));
        Guid campaignId,heroId,monsterId,encounterId;
        await using(var provider=Provider(path,18,10,2,3,15,6))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            campaignId=(await services.GetRequiredService<CampaignService>().CreateAsync(new("Encounter"))).Id;
            heroId=await Fighter(services,campaignId,"Mira");
            var inventory=services.GetRequiredService<InventoryService>();
            var heroInventory=await inventory.GetAsync(heroId);
            heroInventory=await inventory.AddAsync(heroId,new("mace",1,heroInventory.Revision));
            heroInventory=await inventory.AddAsync(heroId,new("potion-of-healing",1,heroInventory.Revision));
            var potion=heroInventory.Items.Single(x=>x.DefinitionId=="potion-of-healing");
            var mace=heroInventory.Items.Single(x=>x.DefinitionId=="mace");
            await services.GetRequiredService<MechanicsService>().DamageAsync(heroId,new(8));
            monsterId=(await services.GetRequiredService<MonsterService>().CreateAsync(new(campaignId,"skeleton"))).Instance.Id;
            var combat=services.GetRequiredService<CombatService>();
            encounterId=(await combat.CreateAsync(campaignId,new("Skeleton duel"))).Id;
            var hero=(await combat.AddAsync(encounterId,new(heroId))).Result.Id;
            var monster=(await services.GetRequiredService<MonsterService>()
                .AddToEncounterAsync(encounterId,new(monsterId))).Result.Id;
            await combat.InitiativeAsync(encounterId); await combat.StartAsync(encounterId,new());
            var revision=(await combat.GetAsync(encounterId)).Encounter.Revision;
            var used=await combat.UseItemAsync(encounterId,new(hero,potion.Id,hero,0,revision));
            Assert.Equal(7,used.Result.HitPointsRegained);
            await Assert.ThrowsAsync<StateConflictException>(()=>combat.UseItemAsync(encounterId,
                new(hero,potion.Id,hero,0,revision)));
            var hit=await combat.AttackAsync(encounterId,new(hero,
                new(monster,mace.Id,AttackMode.Melee,new(5,true,true))));
            Assert.True(hit.Result.Health!.After.Dead);
            var complete=await combat.CompleteAsync(encounterId,new(EncounterOutcome.Victory,
                hit.Revision));
            Assert.Equal(EncounterStatus.Completed,complete.Result.Status);
            var rewards=services.GetRequiredService<EncounterRewardService>();
            var rewardView=await rewards.GetAsync(encounterId);
            Assert.Equal(50,rewardView.Rewards.AvailableExperience);
            Assert.Contains(monsterId,rewardView.AvailableLoot.Keys);
            var awarded=await rewards.AwardExperienceAsync(encounterId,new(
                [new ExperienceAward(heroId,50)],rewardView.Rewards.Revision));
            await Assert.ThrowsAsync<StateConflictException>(()=>rewards.AwardExperienceAsync(encounterId,
                new([new ExperienceAward(heroId,50)],rewardView.Rewards.Revision)));
            var monsterInventory=await inventory.GetAsync(monsterId);
            var item=monsterInventory.Items.Single(x=>x.DefinitionId=="shortsword");
            var currentHero=await inventory.GetAsync(heroId);
            await rewards.AwardLootAsync(encounterId,new(monsterId,heroId,item.Id,1,
                monsterInventory.Revision,currentHero.Revision));
            Assert.Contains((await inventory.GetAsync(heroId)).Items,x=>x.Id==item.Id);
            Assert.DoesNotContain((await inventory.GetAsync(monsterId)).Items,x=>x.Id==item.Id);
            Assert.Single(await services.GetRequiredService<CampaignService>().EventsAsync(campaignId),
                x=>x.Type=="EncounterCompleted");
            Assert.Equal(50,awarded.Awards.Sum(x=>x.Amount));
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            Assert.Equal(50,(await services.GetRequiredService<EncounterRewardService>()
                .GetAsync(encounterId)).Rewards.Awards.Sum(x=>x.Amount));
            Assert.DoesNotContain((await services.GetRequiredService<InventoryService>()
                .GetAsync(monsterId)).Items,x=>x.DefinitionId=="shortsword");
            Assert.Equal(1,(await services.GetRequiredService<CampaignService>()
                .EventsAsync(campaignId)).Count(x=>x.Type=="LootAwarded"));
        }
    }
}
