using DndEngine.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DndEngine.Infrastructure.Migrations.Campaign;

[DbContext(typeof(CampaignDbContext))]
[Migration("20261007100000_CampaignGameTime")]
public sealed class CampaignGameTime : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<long>(name: "GameSeconds", table: "Campaigns",
            type: "INTEGER", nullable: false, defaultValue: 0L);
        migrationBuilder.AddColumn<long>(name: "Revision", table: "Campaigns",
            type: "INTEGER", nullable: false, defaultValue: 0L);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropColumn(name: "GameSeconds", table: "Campaigns");
        migrationBuilder.DropColumn(name: "Revision", table: "Campaigns");
    }
}
