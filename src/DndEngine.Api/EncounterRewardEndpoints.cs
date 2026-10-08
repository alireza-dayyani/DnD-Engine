using DndEngine.Application;

namespace DndEngine.Api;

internal static class EncounterRewardEndpoints
{
    public static void MapEncounterRewards(this WebApplication app)
    {
        app.MapPost("/combat/{id:guid}/complete",(Guid id,CompleteEncounter request,
            CombatService service,CancellationToken ct) => service.CompleteAsync(id,request,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        app.MapGet("/combat/{id:guid}/rewards",(Guid id,EncounterRewardService service,
            CancellationToken ct) => service.GetAsync(id,ct));
        app.MapPost("/combat/{id:guid}/rewards/experience",(Guid id,
            AwardEncounterExperience request,EncounterRewardService service,CancellationToken ct) =>
            service.AwardExperienceAsync(id,request,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/combat/{id:guid}/rewards/loot",(Guid id,AwardEncounterLoot request,
            EncounterRewardService service,CancellationToken ct) =>
            service.AwardLootAsync(id,request,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
    }
}
