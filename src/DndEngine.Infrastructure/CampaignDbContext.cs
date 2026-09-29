using Microsoft.EntityFrameworkCore;
namespace DndEngine.Infrastructure;

public sealed class CampaignRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string RulesetId { get; set; } = "";
    public string SrdVersion { get; set; } = "";
}
public sealed class CharacterRow
{
    public Guid Id { get; set; }
    public Guid CampaignId { get; set; }
    public string Name { get; set; } = "";
    public int Level { get; set; }
    public int ArmorClass { get; set; }
    public string AbilitiesJson { get; set; } = "";
    public string SkillsJson { get; set; } = "";
    public string SavesJson { get; set; } = "";
    public string HealthJson { get; set; } = "";
    public long Revision { get; set; }
}
public sealed class EventRow
{
    public long Sequence { get; set; }
    public Guid EventId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid? CharacterId { get; set; }
    public string Type { get; set; } = "";
    public DateTimeOffset OccurredAtUtc { get; set; }
    public string RulesetId { get; set; } = "";
    public string SrdVersion { get; set; } = "";
    public int SchemaVersion { get; set; }
    public long? CharacterRevision { get; set; }
    public string DataJson { get; set; } = "";
}
public sealed class CampaignDbContext(DbContextOptions<CampaignDbContext> options) : DbContext(options)
{
    public DbSet<CampaignRow> Campaigns => Set<CampaignRow>();
    public DbSet<CharacterRow> Characters => Set<CharacterRow>();
    public DbSet<EventRow> Events => Set<EventRow>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CampaignRow>().HasKey(x => x.Id);
        model.Entity<CharacterRow>().HasKey(x => x.Id);
        model.Entity<CharacterRow>().Property(x => x.Revision).IsConcurrencyToken();
        model.Entity<CharacterRow>().HasOne<CampaignRow>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CharacterRow>().ToTable(t => {
            t.HasCheckConstraint("CK_Character_Level", "Level BETWEEN 1 AND 20");
            t.HasCheckConstraint("CK_Character_Revision", "Revision >= 0");
        });
        model.Entity<EventRow>().HasKey(x => x.Sequence);
        model.Entity<EventRow>().HasIndex(x => x.EventId).IsUnique();
        model.Entity<EventRow>().HasIndex(x => new { x.CampaignId, x.Sequence });
        model.Entity<EventRow>().HasIndex(x => new { x.CharacterId, x.CharacterRevision }).IsUnique();
        model.Entity<EventRow>().HasOne<CampaignRow>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<EventRow>().HasOne<CharacterRow>().WithMany().HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Restrict);
    }
}
