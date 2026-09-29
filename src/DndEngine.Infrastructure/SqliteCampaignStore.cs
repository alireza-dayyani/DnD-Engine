using System.Text.Json;
using DndEngine.Application;
using DndEngine.Domain;
using Microsoft.EntityFrameworkCore;
namespace DndEngine.Infrastructure;

public sealed class SqliteCampaignStore(CampaignDbContext db) : ICampaignStore
{
    public async Task<Campaign?> GetCampaignAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Campaigns.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : new(row.Id, row.Name, new(row.RulesetId, row.SrdVersion));
    }
    public async Task<Character?> GetCharacterAsync(Guid id, CancellationToken ct)
    {
        var row = await db.Characters.AsNoTracking().SingleOrDefaultAsync(x => x.Id == id, ct);
        return row is null ? null : new(row.Id, row.CampaignId, row.Name, row.Level,
            JsonSerializer.Deserialize<Dictionary<Ability, int>>(row.AbilitiesJson)!,
            JsonSerializer.Deserialize<string[]>(row.SkillsJson)!, JsonSerializer.Deserialize<Ability[]>(row.SavesJson)!,
            row.ArmorClass, new(JsonSerializer.Deserialize<HealthState>(row.HealthJson)!), row.Revision);
    }
    public async Task CreateCampaignAsync(Campaign campaign, CampaignEvent entry, CancellationToken ct)
    {
        db.Campaigns.Add(new() { Id = campaign.Id, Name = campaign.Name, RulesetId = campaign.Ruleset.Id, SrdVersion = campaign.Ruleset.Version });
        db.Events.Add(ToRow(entry));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
    public async Task CreateCharacterAsync(Character character, CampaignEvent entry, CancellationToken ct)
    {
        db.Characters.Add(ToRow(character)); db.Events.Add(ToRow(entry));
        await db.SaveChangesAsync(ct);
        db.ChangeTracker.Clear();
    }
    public async Task SaveCharacterAsync(Character character, CampaignEvent entry, CancellationToken ct)
    {
        var row = ToRow(character);
        db.Attach(row);
        row.Revision = checked(character.Revision + 1);
        db.Entry(row).State = EntityState.Modified;
        db.Entry(row).Property(x => x.Revision).OriginalValue = character.Revision;
        db.Events.Add(ToRow(entry));
        try { await db.SaveChangesAsync(ct); }
        catch (DbUpdateConcurrencyException) { throw new StateConflictException("Character changed concurrently. Reload before trying again."); }
        finally { db.ChangeTracker.Clear(); }
    }
    public async Task<IReadOnlyList<CampaignEvent>> GetEventsAsync(Guid campaignId, long after, int limit, CancellationToken ct)
    {
        var rows = await db.Events.AsNoTracking().Where(x => x.CampaignId == campaignId && x.Sequence > after)
            .OrderBy(x => x.Sequence).Take(limit).ToArrayAsync(ct);
        return rows.Select(x => new CampaignEvent(x.Sequence, x.EventId, x.CampaignId, x.CharacterId, x.Type,
            x.OccurredAtUtc, new(x.RulesetId, x.SrdVersion), x.SchemaVersion, x.CharacterRevision,
            JsonSerializer.Deserialize<JsonElement>(x.DataJson))).ToArray();
    }
    internal static CharacterRow ToRow(Character c) => new() { Id = c.Id, CampaignId = c.CampaignId, Name = c.Name,
        Level = c.Level.Value, ArmorClass = c.ArmorClass, Revision = c.Revision,
        AbilitiesJson = JsonSerializer.Serialize(c.Abilities.ToDictionary(x => x.Key, x => x.Value.Value)),
        SkillsJson = JsonSerializer.Serialize(c.SkillProficiencies), SavesJson = JsonSerializer.Serialize(c.SavingThrowProficiencies),
        HealthJson = JsonSerializer.Serialize(c.Health.State) };
    internal static EventRow ToRow(CampaignEvent e) => new() { EventId = e.EventId, CampaignId = e.CampaignId,
        CharacterId = e.CharacterId, Type = e.Type, OccurredAtUtc = e.OccurredAtUtc, RulesetId = e.Ruleset.Id,
        SrdVersion = e.Ruleset.Version, SchemaVersion = e.SchemaVersion, CharacterRevision = e.CharacterRevision,
        DataJson = e.Data.GetRawText() };
}
