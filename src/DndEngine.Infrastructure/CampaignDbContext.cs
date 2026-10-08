using Microsoft.EntityFrameworkCore;
namespace DndEngine.Infrastructure;

public sealed class CampaignRow
{
    public Guid Id { get; set; }
    public string Name { get; set; } = "";
    public string RulesetId { get; set; } = "";
    public string SrdVersion { get; set; } = "";
    public long GameSeconds { get; set; }
    public long Revision { get; set; }
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
    public DbSet<CombatProfileRow> CombatProfiles => Set<CombatProfileRow>();
    public DbSet<ProgressionRow> Progressions => Set<ProgressionRow>();
    public DbSet<EncounterRow> Encounters => Set<EncounterRow>();
    public DbSet<CombatMembershipRow> CombatMemberships => Set<CombatMembershipRow>();
    public DbSet<MonsterInstanceRow> MonsterInstances => Set<MonsterInstanceRow>();
    public DbSet<IdempotencyOperationRow> IdempotencyOperations => Set<IdempotencyOperationRow>();
    public DbSet<InventoryStateRow> InventoryStates => Set<InventoryStateRow>();
    public DbSet<EncounterRewardRow> EncounterRewards => Set<EncounterRewardRow>();
    public DbSet<DroppedItemRow> DroppedItems => Set<DroppedItemRow>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<CampaignRow>().HasKey(x => x.Id);
        model.Entity<CampaignRow>().Property(x => x.Revision).IsConcurrencyToken();
        model.Entity<CombatProfileRow>().HasKey(x => x.CharacterId);
        model.Entity<ProgressionRow>().HasKey(x => x.CharacterId);
        model.Entity<ProgressionRow>().HasOne<CharacterRow>().WithMany().HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CombatProfileRow>().HasOne<CharacterRow>().WithMany().HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<EncounterRow>().HasKey(x => x.Id);
        model.Entity<EncounterRow>().Property(x => x.Revision).IsConcurrencyToken();
        model.Entity<EncounterRow>().HasOne<CampaignRow>().WithMany().HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CombatMembershipRow>().HasKey(x => x.CharacterId);
        model.Entity<CombatMembershipRow>().HasOne<CharacterRow>().WithMany().HasForeignKey(x => x.CharacterId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<CombatMembershipRow>().HasOne<EncounterRow>().WithMany().HasForeignKey(x => x.EncounterId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MonsterInstanceRow>().HasKey(x => x.Id);
        model.Entity<MonsterInstanceRow>().Property(x => x.Revision).IsConcurrencyToken();
        model.Entity<MonsterInstanceRow>().HasOne<CharacterRow>().WithMany()
            .HasForeignKey(x => x.Id).OnDelete(DeleteBehavior.Restrict);
        model.Entity<MonsterInstanceRow>().HasOne<CampaignRow>().WithMany()
            .HasForeignKey(x => x.CampaignId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<IdempotencyOperationRow>().HasKey(x => x.OperationId);
        model.Entity<InventoryStateRow>().HasKey(x => x.OwnerId);
        model.Entity<InventoryStateRow>().HasOne<CharacterRow>().WithMany()
            .HasForeignKey(x=>x.OwnerId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<EncounterRewardRow>().HasKey(x=>x.EncounterId);
        model.Entity<EncounterRewardRow>().Property(x=>x.Revision).IsConcurrencyToken();
        model.Entity<EncounterRewardRow>().HasOne<EncounterRow>().WithMany()
            .HasForeignKey(x=>x.EncounterId).OnDelete(DeleteBehavior.Restrict);
        model.Entity<DroppedItemRow>().HasKey(x=>x.Id);
        model.Entity<DroppedItemRow>().HasIndex(x=>x.CampaignId);
        model.Entity<DroppedItemRow>().HasOne<CampaignRow>().WithMany()
            .HasForeignKey(x=>x.CampaignId).OnDelete(DeleteBehavior.Restrict);
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
