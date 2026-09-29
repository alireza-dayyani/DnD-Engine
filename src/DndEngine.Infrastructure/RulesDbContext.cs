using Microsoft.EntityFrameworkCore;
namespace DndEngine.Infrastructure;

public sealed class RulesetRow
{
    public string Id { get; set; } = "";
    public string Version { get; set; } = "";
    public string SourceUrl { get; set; } = "";
    public string License { get; set; } = "";
    public string ContentHash { get; set; } = "";
}
public sealed class SkillRow
{
    public string RulesetId { get; set; } = "";
    public string Version { get; set; } = "";
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Ability { get; set; } = "";
    public string Source { get; set; } = "";
}
public sealed class RulesDbContext(DbContextOptions<RulesDbContext> options) : DbContext(options)
{
    public DbSet<RulesetRow> Rulesets => Set<RulesetRow>();
    public DbSet<SkillRow> Skills => Set<SkillRow>();
    protected override void OnModelCreating(ModelBuilder model)
    {
        model.Entity<RulesetRow>().HasKey(x => new { x.Id, x.Version });
        model.Entity<SkillRow>().HasKey(x => new { x.RulesetId, x.Version, x.Id });
        model.Entity<SkillRow>().HasOne<RulesetRow>().WithMany().HasForeignKey(x => new { x.RulesetId, x.Version }).OnDelete(DeleteBehavior.Restrict);
    }
}
