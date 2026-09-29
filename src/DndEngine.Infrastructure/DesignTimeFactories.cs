using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;
namespace DndEngine.Infrastructure;

public sealed class CampaignDesignFactory : IDesignTimeDbContextFactory<CampaignDbContext>
{
    public CampaignDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<CampaignDbContext>()
        .UseSqlite("Data Source=campaign.db").Options);
}
public sealed class RulesDesignFactory : IDesignTimeDbContextFactory<RulesDbContext>
{
    public RulesDbContext CreateDbContext(string[] args) => new(new DbContextOptionsBuilder<RulesDbContext>()
        .UseSqlite("Data Source=rules.db").Options);
}
