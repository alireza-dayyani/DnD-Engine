using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DndEngine.Domain;
using DndEngine.Domain.Progression;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace DndEngine.IntegrationTests;

public class ApiTests
{
    private sealed class Factory(params int[] rolls) : WebApplicationFactory<Program>
    {
        private readonly string path = Path.Combine(Path.GetTempPath(),"DndEngine.ApiTests",Guid.NewGuid().ToString("N"));
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("DataDirectory",path);
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>
                { ["DataDirectory"]=path }));
            builder.ConfigureServices(services=>services.AddSingleton<IDiceRoller>(
                new FixedDiceRoller(rolls.Length==0 ? [11,7,16,10] : rolls)));
        }
    }
    [Fact]
    public async Task HttpContractsResolveMechanicalSlice()
    {
        await using var app=new Factory(); using var client=app.CreateClient();
        var campaign=await Post(client,"/campaigns",new { name="API campaign" });
        var campaignId=campaign.GetProperty("id").GetGuid();
        var c=await Post(client,"/characters",new { campaignId,name="Vaelaris",level=5,
            abilities=new{Strength=10,Dexterity=14,Constitution=12,Intelligence=10,Wisdom=16,Charisma=18},
            skillProficiencies=new[]{"deception"},savingThrowProficiencies=new[]{"Wisdom"},maximumHp=20 });
        var id=c.GetProperty("id").GetGuid();
        Assert.Equal(12,c.GetProperty("armorClass").GetInt32());
        var normal=await Post(client,$"/characters/{id}/checks/skill",new{skillId="deception",dc=15});
        Assert.Equal(18,normal.GetProperty("total").GetInt32()); Assert.Equal("Normal",normal.GetProperty("advantageState").GetString());
        var advantage=await Post(client,$"/characters/{id}/checks/skill",new{skillId="deception",dc=15,advantage=true});
        Assert.Equal(16,advantage.GetProperty("selectedRoll").GetInt32());
        await Post(client,$"/characters/{id}/saving-throws",new{ability="Wisdom",dc=15});
        await Post(client,$"/characters/{id}/damage",new{amount=7}); await Post(client,$"/characters/{id}/heal",new{amount=3});
        var saved=await client.GetFromJsonAsync<JsonElement>($"/characters/{id}");
        Assert.Equal(16,saved.GetProperty("health").GetProperty("current").GetInt32());
        var events=await client.GetFromJsonAsync<JsonElement>($"/campaigns/{campaignId}/events"); Assert.Equal(7,events.GetArrayLength());
    }
    [Fact]
    public async Task HttpRejectsUnknownIdsMalformedContractsAndInvalidRules()
    {
        await using var app=new Factory(); using var client=app.CreateClient();
        Assert.Equal(HttpStatusCode.NotFound,(await client.GetAsync($"/characters/{Guid.NewGuid()}")).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/campaigns",new{name="",srdVersion="5.2.1"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync("/campaigns",new{name="future",srdVersion="5.2.2"})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"/characters/{Guid.NewGuid()}/saving-throws",new{ability="Bogus",dc=10})).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"/characters/{Guid.NewGuid()}/damage",new{})).StatusCode);
    }

    [Fact]
    public async Task HttpSpellSlotSpendingUpdatesTheSheetAndRejectsStaleWrites()
    {
        await using var app=new Factory(); using var client=app.CreateClient();
        var campaign=await Post(client,"/campaigns",new { name="Magic API" });
        var campaignId=campaign.GetProperty("id").GetGuid();
        var sheet=await Post(client,"/srd-characters",new {
            campaignId,name="Arin",speciesId="dwarf",speciesVariantId=(string?)null,size="Medium",backgroundId="criminal",classId="wizard",
            baseAbilities=new { Strength=13,Dexterity=13,Constitution=13,Intelligence=13,Wisdom=13,Charisma=13 },
            backgroundBonuses=new { Dexterity=2,Constitution=1 },classSkills=new[]{"arcana","history"}
        });
        var id=sheet.GetProperty("id").GetGuid();
        var revision=sheet.GetProperty("revision").GetInt64();
        sheet=await Post(client,$"/characters/{id}/spell-slots/spend",new { pool="Shared",spellLevel=1,expectedRevision=revision });
        Assert.Equal(1,sheet.GetProperty("spellcasting").GetProperty("sharedSlots")[0].GetProperty("current").GetInt32());
        Assert.Equal(HttpStatusCode.Conflict,(await client.PostAsJsonAsync($"/characters/{id}/spell-slots/spend",
            new { pool="Shared",spellLevel=1,expectedRevision=revision })).StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest,(await client.PostAsJsonAsync($"/characters/{id}/spell-slots/spend",
            new { pool="PactMagic",spellLevel=1,expectedRevision=revision+1 })).StatusCode);
        var summary=await client.GetFromJsonAsync<JsonElement>($"/characters/{id}/spellcasting");
        Assert.Equal(1,summary.GetProperty("sharedSlots")[0].GetProperty("current").GetInt32());
    }

    [Fact]
    public async Task HttpPreparedCureWoundsHealsAndConsumesOneSlot()
    {
        await using var app=new Factory(3,4); using var client=app.CreateClient();
        var spells=await client.GetFromJsonAsync<JsonElement>("/spells");
        Assert.Equal(10,spells.GetArrayLength());
        Assert.Equal("cure-wounds",spells[0].GetProperty("id").GetString());
        Assert.Equal("healing-word",spells[1].GetProperty("id").GetString());
        var third=await client.GetFromJsonAsync<JsonElement>("/spells?packVersion=3");
        Assert.Equal(7,third.GetArrayLength());
        Assert.False(third.EnumerateArray().Single(x=>x.GetProperty("id").GetString()=="circle-of-death")
            .TryGetProperty("materialItemId",out _));
        Assert.Equal("black-pearl-powder-500gp",spells.EnumerateArray()
            .Single(x=>x.GetProperty("id").GetString()=="circle-of-death")
            .GetProperty("materialItemId").GetString());
        var initialSpells=await client.GetFromJsonAsync<JsonElement>("/spells?packVersion=1");
        Assert.Single(initialSpells.EnumerateArray());
        Assert.Equal(8,initialSpells[0].GetProperty("dieSides").GetInt32());
        var campaign=await Post(client,"/campaigns",new { name="Spell API" });
        var campaignId=campaign.GetProperty("id").GetGuid();
        var sheet=await Post(client,"/srd-characters",new {
            campaignId,name="Elen",speciesId="dwarf",speciesVariantId=(string?)null,size="Medium",backgroundId="criminal",classId="cleric",
            baseAbilities=new { Strength=13,Dexterity=13,Constitution=13,Intelligence=13,Wisdom=13,Charisma=13 },
            backgroundBonuses=new { Dexterity=2,Constitution=1 },classSkills=new[]{"history","insight"},
            preparedSpellIds=new[]{"cure-wounds"}
        });
        var id=sheet.GetProperty("id").GetGuid();
        await Post(client,$"/characters/{id}/damage",new { amount=6 });
        sheet=await client.GetFromJsonAsync<JsonElement>($"/characters/{id}/sheet");
        var result=await Post(client,$"/characters/{id}/spells/cast-self",new {
            classId="cleric",spellId="cure-wounds",pool="Shared",spellLevel=1,
            expectedRevision=sheet.GetProperty("revision").GetInt64(),componentsAvailable=true
        });
        Assert.Equal(6,result.GetProperty("hitPointsRegained").GetInt32());
        Assert.Equal(1,result.GetProperty("sheet").GetProperty("spellcasting")
            .GetProperty("sharedSlots")[0].GetProperty("current").GetInt32());
        Assert.Equal(result.GetProperty("sheet").GetProperty("maximumHp").GetInt32(),
            result.GetProperty("sheet").GetProperty("currentHp").GetInt32());
    }

    [Fact]
    public async Task HttpHealingWordUsesItsOwnDiceAndClassList()
    {
        await using var app=new Factory(2,3); using var client=app.CreateClient();
        var campaign=await Post(client,"/campaigns",new { name="Healing Word API" });
        var sheet=await Post(client,"/srd-characters",new {
            campaignId=campaign.GetProperty("id").GetGuid(),name="Elen",speciesId="dwarf",
            speciesVariantId=(string?)null,size="Medium",backgroundId="criminal",classId="cleric",
            baseAbilities=new { Strength=13,Dexterity=13,Constitution=13,Intelligence=13,Wisdom=13,Charisma=13 },
            backgroundBonuses=new { Dexterity=2,Constitution=1 },classSkills=new[]{"history","insight"},
            preparedSpellIds=new[]{"cure-wounds"}
        });
        Assert.Equal(SpellPackVersions.Current,sheet.GetProperty("spellPackVersion").GetString());
        var id=sheet.GetProperty("id").GetGuid();
        sheet=(await Post(client,$"/characters/{id}/rests/long",new {
            expectedRevision=sheet.GetProperty("revision").GetInt64(),
            spellReplacements=new[]{new { classId="cleric",fromSpellId="cure-wounds",toSpellId="healing-word" }}
        })).GetProperty("sheet");
        Assert.Equal("healing-word",sheet.GetProperty("preparedSpells")[0].GetProperty("spellId").GetString());
        sheet=await Post(client,$"/characters/{id}/level-up",new {
            classId="cleric",expectedRevision=sheet.GetProperty("revision").GetInt64(),
            additionalPreparedSpellIds=new[]{"cure-wounds"}
        });
        Assert.Equal(2,sheet.GetProperty("spellcasting").GetProperty("classes")[0]
            .GetProperty("preparedCount").GetInt32());
        await Post(client,$"/characters/{id}/damage",new { amount=8 });
        sheet=await client.GetFromJsonAsync<JsonElement>($"/characters/{id}/sheet");
        var result=await Post(client,$"/characters/{id}/spells/cast-self",new {
            classId="cleric",spellId="healing-word",pool="Shared",spellLevel=1,
            expectedRevision=sheet.GetProperty("revision").GetInt64(),componentsAvailable=true
        });
        Assert.Equal(6,result.GetProperty("hitPointsRegained").GetInt32());
        Assert.Equal(2,result.GetProperty("rolls").GetArrayLength());
        Assert.Equal(2,result.GetProperty("sheet").GetProperty("spellcasting")
            .GetProperty("sharedSlots")[0].GetProperty("current").GetInt32());
    }
    [Fact]
    public async Task HttpCombatMagicCastsKnownCantripAndRejectsStaleEncounterRevision()
    {
        await using var app=new Factory(18,2,1,5); using var client=app.CreateClient();
        var campaign=await Post(client,"/campaigns",new { name="Combat Magic API" });
        var campaignId=campaign.GetProperty("id").GetGuid();
        var cleric=await Post(client,"/srd-characters",new {
            campaignId,name="Elen",speciesId="dwarf",speciesVariantId=(string?)null,size="Medium",
            backgroundId="criminal",classId="cleric",
            baseAbilities=new { Strength=13,Dexterity=13,Constitution=13,Intelligence=13,Wisdom=13,Charisma=13 },
            backgroundBonuses=new { Dexterity=2,Constitution=1 },classSkills=new[]{"history","insight"},
            knownCantripIds=new[]{"sacred-flame"}
        });
        var enemy=await Post(client,"/characters",new { campaignId,name="Enemy",level=1,
            abilities=new { Strength=10,Dexterity=10,Constitution=10,Intelligence=10,Wisdom=10,Charisma=10 },
            skillProficiencies=Array.Empty<string>(),savingThrowProficiencies=Array.Empty<string>(),maximumHp=20,armorClass=10 });
        var enemyId=enemy.GetProperty("id").GetGuid();
        using(var imported=await client.PutAsJsonAsync($"/characters/{enemyId}/combat-profile",new {
            speed=30,weaponProficiencies=Array.Empty<string>(),resistances=Array.Empty<string>(),
            immunities=Array.Empty<string>(),vulnerabilities=Array.Empty<string>(),conditionImmunities=Array.Empty<string>() }))
            imported.EnsureSuccessStatusCode();
        var encounter=await Post(client,$"/campaigns/{campaignId}/combat",new { name="Duel" });
        var encounterId=encounter.GetProperty("id").GetGuid();
        var actor=(await Post(client,$"/combat/{encounterId}/combatants",new {
            characterId=cleric.GetProperty("id").GetGuid() })).GetProperty("result").GetProperty("id").GetGuid();
        var target=(await Post(client,$"/combat/{encounterId}/combatants",new {
            characterId=enemyId,kind="Monster",zeroHpPolicy="Die" })).GetProperty("result").GetProperty("id").GetGuid();
        await Post(client,$"/combat/{encounterId}/initiative",new { });
        await Post(client,$"/combat/{encounterId}/start",new { });
        var view=await client.GetFromJsonAsync<JsonElement>($"/combat/{encounterId}");
        var revision=view.GetProperty("encounter").GetProperty("revision").GetInt64();
        var cast=await Post(client,$"/combat/{encounterId}/spells/cast",new {
            combatantId=actor,classId="cleric",spellId="sacred-flame",pool=(string?)null,spellLevel=0,
            targets=new[]{new { combatantId=target,distanceFeet=30,casterCanSeeTarget=true,
                targetCanSeeCaster=true,cover="Half" }},verbalAvailable=true,somaticAvailable=true,
            materialAvailable=false,expectedRevision=revision
        });
        Assert.Equal(5,cast.GetProperty("result").GetProperty("targets")[0]
            .GetProperty("damage").GetProperty("appliedDamage").GetInt32());
        using var stale=await client.PostAsJsonAsync($"/combat/{encounterId}/spells/cast",new {
            combatantId=actor,classId="cleric",spellId="sacred-flame",pool=(string?)null,spellLevel=0,
            targets=new[]{new { combatantId=target,distanceFeet=30,casterCanSeeTarget=true,
                targetCanSeeCaster=true,cover="None" }},verbalAvailable=true,somaticAvailable=true,
            materialAvailable=false,expectedRevision=revision
        });
        Assert.Equal(HttpStatusCode.Conflict,stale.StatusCode);
    }
    private static async Task<JsonElement> Post(HttpClient client,string url,object body)
    {
        using var response=await client.PostAsJsonAsync(url,body);
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
