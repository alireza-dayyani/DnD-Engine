using DndEngine.Application;

namespace DndEngine.Api;

internal static class MonsterEndpoints
{
    public static void MapMonsters(this WebApplication app)
    {
        app.MapGet("/campaigns/{id:guid}/monster-definitions", (Guid id,
            string? packVersion, MonsterService service, CancellationToken ct) =>
            service.DefinitionsAsync(id,packVersion ?? "1",ct));
        app.MapGet("/monsters/{id:guid}", (Guid id, MonsterService service,
            CancellationToken ct) => service.GetAsync(id,ct));
        app.MapPost("/monsters", async (CreateMonsterInstance request,
            MonsterService service, CancellationToken ct) => {
            var result=await service.CreateAsync(request,ct);
            return Results.Created($"/monsters/{result.Instance.Id}",result);
        }).WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/combat/{id:guid}/monsters", (Guid id,
            AddMonsterToEncounter request, MonsterService service, CancellationToken ct) =>
            service.AddToEncounterAsync(id,request,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
    }
}
