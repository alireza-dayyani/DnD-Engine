using System.Net;
using System.Net.Http.Json;
using System.Text;
using DndEngine.Application;
using DndEngine.Infrastructure;
using DndEngine.Domain;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DndEngine.IntegrationTests;

public sealed class IdempotencyIntegrationTests
{
    private sealed class Factory(string path) : WebApplicationFactory<Program>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Testing");
            builder.UseSetting("DataDirectory",path);
            builder.ConfigureAppConfiguration((_,c)=>c.AddInMemoryCollection(
                new Dictionary<string,string?> { ["DataDirectory"]=path }));
            builder.ConfigureLogging(logging=>logging.ClearProviders());
        }
    }

    private static async Task<(HttpStatusCode Status,string Body)> Spawn(HttpClient client,
        Guid key,string json)
    {
        using var request=new HttpRequestMessage(HttpMethod.Post,"/monsters") {
            Content=new StringContent(json,Encoding.UTF8,"application/json") };
        request.Headers.Add("X-Operation-Id",key.ToString());
        using var response=await client.SendAsync(request);
        return (response.StatusCode,await response.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SameOperationReplaysOriginalSpawnAcrossConcurrentRequestsAndRestart()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.OperationTests",Guid.NewGuid().ToString("N"));
        Guid campaignId,operationId=Guid.NewGuid(); string original;
        await using(var app=new Factory(path))
        {
            using var client=app.CreateClient();
            var campaignResponse=await client.PostAsJsonAsync("/campaigns",new CreateCampaign("Retry camp"));
            campaignResponse.EnsureSuccessStatusCode();
            campaignId=(await campaignResponse.Content.ReadFromJsonAsync<CampaignView>())!.Id;
            var json=$"{{\"campaignId\":\"{campaignId}\",\"definitionId\":\"skeleton\"}}";
            using(var missing=await client.PostAsync("/monsters",
                new StringContent(json,Encoding.UTF8,"application/json")))
                Assert.Equal(HttpStatusCode.BadRequest,missing.StatusCode);
            var results=await Task.WhenAll(Spawn(client,operationId,json),Spawn(client,operationId,json));
            Assert.True(results.All(x=>x.Status==HttpStatusCode.Created),
                string.Join(" | ",results.Select(x=>$"{x.Status}: {x.Body}")));
            Assert.Equal(results[0].Body,results[1].Body);
            original=results[0].Body;
            var mismatched=await Spawn(client,operationId,
                $"{{\"campaignId\":\"{campaignId}\",\"definitionId\":\"goblin-minion\"}}");
            Assert.Equal(HttpStatusCode.Conflict,mismatched.Status);
            await using var scope=app.Services.CreateAsyncScope();
            Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<CampaignDbContext>()
                .MonsterInstances.CountAsync());
            Assert.Single(await scope.ServiceProvider.GetRequiredService<CampaignService>()
                .EventsAsync(campaignId),x=>x.Type=="MonsterSpawned");
        }
        await using(var app=new Factory(path))
        {
            using var client=app.CreateClient();
            var json=$"{{\"campaignId\":\"{campaignId}\",\"definitionId\":\"skeleton\"}}";
            var replay=await Spawn(client,operationId,json);
            Assert.Equal(HttpStatusCode.Created,replay.Status);
            Assert.Equal(original,replay.Body);
            await using var scope=app.Services.CreateAsyncScope();
            Assert.Equal(1,await scope.ServiceProvider.GetRequiredService<CampaignDbContext>()
                .MonsterInstances.CountAsync());
        }
    }

    [Fact]
    public async Task RetriedPotionCommandDoesNotRerollOrConsumeTwiceAfterRestart()
    {
        var path=Path.Combine(Path.GetTempPath(),"DndEngine.PotionRetryTests",Guid.NewGuid().ToString("N"));
        Guid campaignId,heroId,itemId,operationId=Guid.NewGuid(); string original;
        await using(var app=new Factory(path))
        {
            using var client=app.CreateClient();
            await using(var scope=app.Services.CreateAsyncScope())
            {
                var services=scope.ServiceProvider;
                campaignId=(await services.GetRequiredService<CampaignService>().CreateAsync(new("Potion retry"))).Id;
                heroId=(await services.GetRequiredService<ProgressionService>().CreateAsync(new(campaignId,
                    "Mira","dwarf",null,"Medium","criminal","fighter",
                    new() { [Ability.Strength]=15,[Ability.Dexterity]=14,[Ability.Constitution]=14,
                        [Ability.Intelligence]=10,[Ability.Wisdom]=12,[Ability.Charisma]=8 },
                    new() { [Ability.Dexterity]=2,[Ability.Constitution]=1 },
                    ["athletics","perception"],StartingItemIds:["chain-mail","greatsword"],
                    MasteredWeaponIds:["greatsword"],FightingStyleFeat:"defense"))).Id;
                var inventory=services.GetRequiredService<InventoryService>();
                var current=await inventory.GetAsync(heroId);
                current=await inventory.AddAsync(heroId,new("potion-of-healing",2,current.Revision));
                itemId=current.Items.Single(x=>x.DefinitionId=="potion-of-healing").Id;
                await services.GetRequiredService<MechanicsService>().DamageAsync(heroId,new(8));
            }
            var revision=(await client.GetFromJsonAsync<InventoryView>($"/characters/{heroId}/inventory/state"))!.Revision;
            var json=$"{{\"itemId\":\"{itemId}\",\"targetId\":\"{heroId}\",\"expectedRevision\":{revision}}}";
            async Task<(HttpStatusCode,string)> Send(string body)
            {
                using var request=new HttpRequestMessage(HttpMethod.Post,$"/characters/{heroId}/inventory/items/use") {
                    Content=new StringContent(body,Encoding.UTF8,"application/json") };
                request.Headers.Add("X-Operation-Id",operationId.ToString());
                using var response=await client.SendAsync(request);
                return (response.StatusCode,await response.Content.ReadAsStringAsync());
            }
            var results=await Task.WhenAll(Send(json),Send(json));
            Assert.All(results,x=>Assert.Equal(HttpStatusCode.OK,x.Item1));
            Assert.Equal(results[0].Item2,results[1].Item2); original=results[0].Item2;
            Assert.Equal(HttpStatusCode.Conflict,(await Send(json.Replace($"\"expectedRevision\":{revision}",
                $"\"expectedRevision\":{revision+1}"))).Item1);
            await using var scope2=app.Services.CreateAsyncScope();
            var services2=scope2.ServiceProvider;
            Assert.Equal(1,(await services2.GetRequiredService<InventoryService>().GetAsync(heroId))
                .Items.Single(x=>x.Id==itemId).Quantity);
            Assert.Single(await services2.GetRequiredService<CampaignService>().EventsAsync(campaignId),
                x=>x.Type=="ItemConsumed");
        }
        await using(var app=new Factory(path))
        {
            using var client=app.CreateClient();
            var inventory=(await client.GetFromJsonAsync<InventoryView>($"/characters/{heroId}/inventory/state"))!;
            var json=$"{{\"itemId\":\"{itemId}\",\"targetId\":\"{heroId}\",\"expectedRevision\":{inventory.Revision-1}}}";
            using var request=new HttpRequestMessage(HttpMethod.Post,$"/characters/{heroId}/inventory/items/use") {
                Content=new StringContent(json,Encoding.UTF8,"application/json") };
            request.Headers.Add("X-Operation-Id",operationId.ToString());
            using var response=await client.SendAsync(request);
            Assert.Equal(HttpStatusCode.OK,response.StatusCode);
            Assert.Equal(original,await response.Content.ReadAsStringAsync());
            Assert.Equal(1,inventory.Items.Single(x=>x.Id==itemId).Quantity);
        }
    }
}
