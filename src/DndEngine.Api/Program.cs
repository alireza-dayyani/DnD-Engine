using System.Text.Json.Serialization;
using DndEngine.Application;
using DndEngine.Domain;
using DndEngine.Infrastructure;

var builder = WebApplication.CreateBuilder(args);
builder.Services.ConfigureHttpJsonOptions(o => {
    o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false));
    o.SerializerOptions.RespectRequiredConstructorParameters = true;
});
builder.Services.AddProblemDetails();
builder.Services.AddDndEngine(builder.Configuration["DataDirectory"] ?? Path.Combine(builder.Environment.ContentRootPath, "../../data"));
var app = builder.Build();
app.Use(async (context, next) =>
{
    try { await next(context); }
    catch (Exception ex) when (ex is RuleViolation or NotFoundException or StateConflictException)
    {
        var status = ex is NotFoundException ? 404 : ex is StateConflictException ? 409 : 400;
        await Results.Problem(statusCode: status, title: ex.Message).ExecuteAsync(context);
    }
});
app.MapGet("/health", () => Results.Ok(new { status = "ready", ruleset = Ruleset.Current }));
app.MapPost("/campaigns", async (CreateCampaign request, CampaignService service, CancellationToken ct) => {
    var result = await service.CreateAsync(request, ct); return Results.Created($"/campaigns/{result.Id}", result);
});
app.MapGet("/campaigns/{id:guid}", (Guid id, CampaignService service, CancellationToken ct) => service.GetAsync(id, ct));
app.MapGet("/campaigns/{id:guid}/events", (Guid id, long? after, int? limit, CampaignService service, CancellationToken ct) => service.EventsAsync(id, after ?? 0, limit ?? 100, ct));
app.MapPost("/characters", async (CreateCharacter request, CharacterService service, CancellationToken ct) => {
    var result = await service.CreateAsync(request, ct); return Results.Created($"/characters/{result.Id}", result);
});
app.MapGet("/characters/{id:guid}", (Guid id, CharacterService service, CancellationToken ct) => service.GetAsync(id, ct));
app.MapPost("/characters/{id:guid}/checks/ability", (Guid id, CheckRequest request, MechanicsService service, CancellationToken ct) => service.AbilityCheckAsync(id, request, ct));
app.MapPost("/characters/{id:guid}/checks/skill", (Guid id, SkillCheckRequest request, MechanicsService service, CancellationToken ct) => service.SkillCheckAsync(id, request, ct));
app.MapPost("/characters/{id:guid}/saving-throws", (Guid id, CheckRequest request, MechanicsService service, CancellationToken ct) => service.SavingThrowAsync(id, request, ct));
app.MapPost("/characters/{id:guid}/damage", (Guid id, DamageRequest request, MechanicsService service, CancellationToken ct) => service.DamageAsync(id, request, ct));
app.MapPost("/characters/{id:guid}/heal", (Guid id, HealingRequest request, MechanicsService service, CancellationToken ct) => service.HealAsync(id, request, ct));
app.MapPost("/characters/{id:guid}/temporary-hp", (Guid id, TemporaryHpRequest request, MechanicsService service, CancellationToken ct) => service.TemporaryHpAsync(id, request, ct));
app.MapPost("/characters/{id:guid}/death-saving-throws", (Guid id, MechanicsService service, CancellationToken ct) => service.DeathSaveAsync(id, ct));
await app.Services.InitializeDndEngineAsync();
await app.RunAsync();
public partial class Program;
