using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using DndEngine.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
namespace DndEngine.IntegrationTests;

public class ApiTests
{
    private sealed class Factory : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.ConfigureAppConfiguration((_,config)=>config.AddInMemoryCollection(new Dictionary<string,string?>
                { ["DataDirectory"]=Path.Combine(Path.GetTempPath(),"DndEngine.ApiTests",Guid.NewGuid().ToString("N")) }));
            builder.ConfigureServices(services=>services.AddSingleton<IDiceRoller>(new FixedDiceRoller(11,7,16,10)));
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
    private static async Task<JsonElement> Post(HttpClient client,string url,object body)
    {
        using var response=await client.PostAsJsonAsync(url,body);
        Assert.True(response.IsSuccessStatusCode,await response.Content.ReadAsStringAsync());
        return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
}
