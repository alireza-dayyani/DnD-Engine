using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DndEngine.Infrastructure.Migrations.Rules;

[DbContext(typeof(RulesDbContext))]
[Migration("20261008100000_Phase5MonsterCatalog")]
public sealed class Phase5MonsterCatalog : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MonsterPacks",
            columns: table => new {
                RulesetId = table.Column<string>(type: "TEXT", nullable: false),
                RulesetVersion = table.Column<string>(type: "TEXT", nullable: false),
                PackVersion = table.Column<string>(type: "TEXT", nullable: false),
                ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                DataJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_MonsterPacks", x => new { x.RulesetId, x.RulesetVersion, x.PackVersion });
                table.ForeignKey("FK_MonsterPacks_Rulesets_RulesetId_RulesetVersion",
                    x => new { x.RulesetId, x.RulesetVersion }, "Rulesets",
                    new[] { "Id", "Version" }, onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "EncounterItemPacks",
            columns: table => new {
                RulesetId = table.Column<string>(type: "TEXT", nullable: false),
                RulesetVersion = table.Column<string>(type: "TEXT", nullable: false),
                PackVersion = table.Column<string>(type: "TEXT", nullable: false),
                ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                DataJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_EncounterItemPacks", x => new { x.RulesetId, x.RulesetVersion, x.PackVersion });
                table.ForeignKey("FK_EncounterItemPacks_Rulesets_RulesetId_RulesetVersion",
                    x => new { x.RulesetId, x.RulesetVersion }, "Rulesets",
                    new[] { "Id", "Version" }, onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("EncounterItemPacks");
        migrationBuilder.DropTable("MonsterPacks");
    }
}
