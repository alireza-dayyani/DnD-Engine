using DndEngine.Application;

namespace DndEngine.Api;

public static class ProgressionEndpoints
{
    public static void MapProgression(this WebApplication app)
    {
        app.MapGet("/character-choices", (ProgressionService service,CancellationToken ct) => service.ChoicesAsync(ct));
        app.MapGet("/spells", (string? packVersion,ProgressionService service,CancellationToken ct) =>
            service.SpellChoicesAsync(packVersion,ct));
        app.MapPost("/srd-characters", async (CreateSrdCharacter request,ProgressionService service,CancellationToken ct) =>
        {
            var sheet = await service.CreateAsync(request,ct);
            return Results.Created($"/characters/{sheet.Id}/sheet",sheet);
        });
        app.MapGet("/characters/{id:guid}/sheet", (Guid id,ProgressionService service,CancellationToken ct) => service.SheetAsync(id,ct));
        app.MapGet("/characters/{id:guid}/spellcasting", (Guid id,ProgressionService service,CancellationToken ct) => service.SpellcastingAsync(id,ct));
        app.MapPost("/characters/{id:guid}/level-up", (Guid id,LevelUpCharacter request,ProgressionService service,CancellationToken ct) => service.LevelUpAsync(id,request,ct));
        app.MapPost("/characters/{id:guid}/inventory", (Guid id,ItemChange request,ProgressionService service,CancellationToken ct) => service.AcquireItemAsync(id,request,ct));
        app.MapPost("/characters/{id:guid}/inventory/equip", (Guid id,EquipItem request,ProgressionService service,CancellationToken ct) => service.EquipAsync(id,request,true,ct));
        app.MapPost("/characters/{id:guid}/inventory/unequip", (Guid id,EquipItem request,ProgressionService service,CancellationToken ct) => service.EquipAsync(id,request,false,ct));
        app.MapPost("/characters/{id:guid}/inventory/remove", (Guid id,EquipItem request,ProgressionService service,CancellationToken ct) => service.RemoveItemAsync(id,request,ct));
        app.MapPost("/characters/{id:guid}/resources/spend", (Guid id,SpendResource request,ProgressionService service,CancellationToken ct) => service.SpendResourceAsync(id,request,ct));
        app.MapPost("/characters/{id:guid}/spell-slots/spend", (Guid id,SpendSpellSlot request,ProgressionService service,CancellationToken ct) => service.SpendSpellSlotAsync(id,request,ct));
        app.MapPost("/characters/{id:guid}/spells/cast-self", (Guid id,CastPreparedSpell request,ProgressionService service,CancellationToken ct) => service.CastPreparedSpellAsync(id,request,ct));
        app.MapPost("/characters/{id:guid}/spell-pack/adopt", (Guid id,AdoptSpellPack request,ProgressionService service,CancellationToken ct) => service.AdoptSpellPackAsync(id,request,ct));
        app.MapPost("/characters/{id:guid}/rests/short", (Guid id,ShortRestRequest request,ProgressionService service,CancellationToken ct) => service.ShortRestAsync(id,request,ct));
        app.MapPost("/characters/{id:guid}/rests/long", (Guid id,LongRestRequest request,ProgressionService service,CancellationToken ct) => service.LongRestAsync(id,request,ct));
    }
}
