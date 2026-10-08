using DndEngine.Application;
using DndEngine.Api;
using DndEngine.Domain.Combat;

internal static class CombatEndpoints
{
    public static void MapCombat(this WebApplication app)
    {
        app.MapGet("/campaigns/{id:guid}/combat-content", (Guid id, CombatService s, CancellationToken ct) => s.ContentAsync(id, ct));
        app.MapPut("/characters/{id:guid}/combat-profile", (Guid id, CombatCapabilities r, CombatService s, CancellationToken ct) => s.ImportAsync(id, r, ct));
        app.MapGet("/characters/{id:guid}/combat-profile", (Guid id, CombatService s, CancellationToken ct) => s.ProfileAsync(id, ct));
        app.MapPost("/characters/{id:guid}/weapons", (Guid id, GrantWeapon r, CombatService s, CancellationToken ct) => s.GrantWeaponAsync(id, r, ct));
        app.MapDelete("/characters/{id:guid}/conditions/{conditionId:guid}", (Guid id, Guid conditionId, CombatService s, CancellationToken ct) => s.RemoveOutsideCombatAsync(id, conditionId, ct));
        app.MapPost("/campaigns/{id:guid}/combat", async (Guid id, CreateCombat r, CombatService s, CancellationToken ct) => {
            var result = await s.CreateAsync(id, r, ct); return Results.Created($"/combat/{result.Id}", result);
        });
        app.MapGet("/combat/{id:guid}", (Guid id, CombatService s, CancellationToken ct) => s.GetAsync(id, ct));
        app.MapPost("/combat/{id:guid}/combatants", (Guid id, AddCombatant r, CombatService s, CancellationToken ct) => s.AddAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/initiative", (Guid id, InitiativeContext r, CombatService s, CancellationToken ct) => s.InitiativeAsync(id, ct, r));
        app.MapPost("/combat/{id:guid}/start", (Guid id, StartCombat r, CombatService s, CancellationToken ct) => s.StartAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/move", (Guid id, MoveCombatant r, CombatService s, CancellationToken ct) => s.MoveAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/stand", (Guid id, CombatActor r, CombatService s, CancellationToken ct) => s.StandAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/action", (Guid id, TakeCombatAction r, CombatService s, CancellationToken ct) => s.ActionAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/attack", (Guid id, AttackCombatant r, CombatService s, CancellationToken ct) => s.AttackAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/items/use",(Guid id,UseCombatItem r,CombatService s,CancellationToken ct) =>
            s.UseItemAsync(id,r,ct)).WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/combat/{id:guid}/spells/cast", (Guid id, CastCombatSpell r, CombatService s, CancellationToken ct) => s.CastSpellAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/monsters/spells/cast", (Guid id, CastMonsterSpell r,
            CombatService s, CancellationToken ct) => s.CastMonsterSpellAsync(id,r,ct))
            .WithMetadata(new IdempotencyRequiredAttribute());
        app.MapPost("/combat/{id:guid}/spell-slots/convert-to-points", (Guid id, CombatConvertSpellSlot r, CombatService s, CancellationToken ct) => s.ConvertSpellSlotAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/spell-slots/create", (Guid id, CombatCreateSorcerySlot r, CombatService s, CancellationToken ct) => s.CreateSorcerySlotAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/saving-throws", (Guid id, CombatSavingThrow r, CombatService s, CancellationToken ct) => s.SavingThrowAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/conditions", (Guid id, ApplyCombatCondition r, CombatService s, CancellationToken ct) => s.ApplyConditionAsync(id, r, ct));
        app.MapDelete("/combat/{id:guid}/combatants/{combatantId:guid}/conditions/{conditionId:guid}",
            (Guid id, Guid combatantId, Guid conditionId, CombatService s, CancellationToken ct) => s.RemoveConditionAsync(id, combatantId, conditionId, ct));
        app.MapPost("/combat/{id:guid}/end-turn", (Guid id, CombatActor r, CombatService s, CancellationToken ct) => s.EndTurnAsync(id, r, ct));
        app.MapPost("/combat/{id:guid}/end", (Guid id, CombatService s, CancellationToken ct) => s.EndAsync(id, ct));
    }
}
