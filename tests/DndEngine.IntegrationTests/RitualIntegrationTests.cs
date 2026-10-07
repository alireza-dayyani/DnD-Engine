using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Domain.Progression;
using DndEngine.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace DndEngine.IntegrationTests;

public sealed class RitualIntegrationTests
{
    private static ServiceProvider Provider(string path) => new ServiceCollection()
        .AddLogging().AddDndEngine(path).AddSingleton<IDiceRoller>(new FixedDiceRoller())
        .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes=true });

    [Fact]
    public async Task WizardBookRitualAdvancesTimeAndComprehensionExpiresAcrossRestart()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.RitualTests",Guid.NewGuid().ToString("N"));
        Guid campaignId,characterId;
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var campaigns=services.GetRequiredService<CampaignService>();
            var campaign=await campaigns.CreateAsync(new("Ritual")); campaignId=campaign.Id;
            var progression=services.GetRequiredService<ProgressionService>();
            var wizard=await progression.CreateAsync(new(campaign.Id,"Ilyra","dwarf",null,"Medium",
                "criminal","wizard",Enum.GetValues<Ability>().ToDictionary(x=>x,_=>13),
                new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","history"],
                WizardSpellbookIds:["comprehend-languages"]));
            characterId=wizard.Id;
            Assert.DoesNotContain(wizard.PreparedSpells!,x=>x.SpellId=="comprehend-languages");
            var request=new CastLanguageSpell("wizard",true,null,1,true,true,true,wizard.Revision,campaign.Revision,
                SpellbookAvailable:true,Uninterrupted:true);
            await Assert.ThrowsAsync<RuleViolation>(()=>progression.CastLanguageSpellAsync(characterId,
                request with { SpellbookAvailable=false }));
            await Assert.ThrowsAsync<RuleViolation>(()=>progression.CastLanguageSpellAsync(characterId,
                request with { Uninterrupted=false }));
            await Assert.ThrowsAsync<RuleViolation>(()=>progression.CastLanguageSpellAsync(characterId,
                request with { MaterialAvailable=false }));
            await Assert.ThrowsAsync<RuleViolation>(()=>progression.CastLanguageSpellAsync(characterId,
                request with { Ritual=false,Pool=SpellSlotPoolKind.Shared }));
            var result=await progression.CastLanguageSpellAsync(characterId,request);
            Assert.Equal(606,result.CompletedAtGameSecond);
            Assert.Equal(4206,result.ExpiresAtGameSecond);
            Assert.Null(result.SlotBefore);
            Assert.Equal(2,(await progression.SpellcastingAsync(characterId))!.SharedSlots[0].Current);
            Assert.True((await progression.CheckLanguageComprehensionAsync(characterId,
                new(LanguageMedium.Heard,true))).UnderstandsLiteralMeaning);
            Assert.False((await progression.CheckLanguageComprehensionAsync(characterId,
                new(LanguageMedium.Written,true))).UnderstandsLiteralMeaning);
            Assert.True((await progression.CheckLanguageComprehensionAsync(characterId,
                new(LanguageMedium.Written,true,true))).UnderstandsLiteralMeaning);
            await Assert.ThrowsAsync<StateConflictException>(()=>progression.CastLanguageSpellAsync(characterId,request));
            var advanced=await campaigns.AdvanceTimeAsync(campaignId,new(3600,result.CampaignRevision));
            Assert.Equal(4206,advanced.GameSeconds);
            Assert.False((await progression.CheckLanguageComprehensionAsync(characterId,
                new(LanguageMedium.Signed,true))).SpellActive);
            var combat=services.GetRequiredService<CombatService>();
            var encounter=await combat.CreateAsync(campaignId,new("Interrupt"));
            await combat.AddAsync(encounter.Id,new(characterId));
            await Assert.ThrowsAsync<StateConflictException>(()=>progression.CastLanguageSpellAsync(characterId,
                request with { ExpectedCharacterRevision=result.Sheet.Revision,
                    ExpectedCampaignRevision=advanced.Revision }));
        }
        await using(var provider=Provider(path))
        {
            await provider.InitializeDndEngineAsync();
            await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
            var campaign=await services.GetRequiredService<CampaignService>().GetAsync(campaignId);
            Assert.Equal(4206,campaign.GameSeconds);
            Assert.False((await services.GetRequiredService<ProgressionService>()
                .CheckLanguageComprehensionAsync(characterId,new(LanguageMedium.Heard,true))).SpellActive);
            Assert.Single(await services.GetRequiredService<CampaignService>().EventsAsync(campaignId),
                x=>x.Type=="LanguageSpellCast");
        }
    }

    [Fact]
    public async Task PreparedNormalCastSpendsSlotAndOnlyActionTime()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.LanguageCastTests",Guid.NewGuid().ToString("N"));
        await using var provider=Provider(path); await provider.InitializeDndEngineAsync();
        await using var scope=provider.CreateAsyncScope(); var services=scope.ServiceProvider;
        var campaign=await services.GetRequiredService<CampaignService>().CreateAsync(new("Language cast"));
        var progression=services.GetRequiredService<ProgressionService>();
        var sorcerer=await progression.CreateAsync(new(campaign.Id,"Nira","dwarf",null,"Medium",
            "criminal","sorcerer",Enum.GetValues<Ability>().ToDictionary(x=>x,_=>13),
            new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },["arcana","persuasion"],
            PreparedSpellIds:["comprehend-languages"]));
        var result=await progression.CastLanguageSpellAsync(sorcerer.Id,
            new("sorcerer",false,SpellSlotPoolKind.Shared,1,true,true,true,sorcerer.Revision,campaign.Revision));
        Assert.Equal(6,result.CompletedAtGameSecond);
        Assert.Equal(3606,result.ExpiresAtGameSecond);
        Assert.Equal(2,result.SlotBefore);
        Assert.Equal(1,(await progression.SpellcastingAsync(sorcerer.Id))!.SharedSlots[0].Current);
        var ritual=await progression.CastLanguageSpellAsync(sorcerer.Id,
            new("sorcerer",true,null,1,true,true,true,result.Sheet.Revision,
                result.CampaignRevision,Uninterrupted:true));
        Assert.Equal(612,ritual.CompletedAtGameSecond);
        Assert.Null(ritual.SlotBefore);
        Assert.Equal(1,(await progression.SpellcastingAsync(sorcerer.Id))!.SharedSlots[0].Current);
    }
}
