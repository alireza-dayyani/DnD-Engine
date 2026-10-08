using DndEngine.Domain.Progression;

namespace DndEngine.Domain.Inventory;

public sealed record EncounterItemDefinition(string Id, string Name, ItemKind Kind,
    bool Stackable, bool Consumable, string? EffectId, int PriceCopper);
public sealed record EncounterItemPack(string Version, EncounterItemDefinition[] Items);
