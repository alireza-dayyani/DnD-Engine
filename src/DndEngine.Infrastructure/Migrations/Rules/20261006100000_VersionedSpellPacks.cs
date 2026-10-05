using DndEngine.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DndEngine.Infrastructure.Migrations.Rules;

[DbContext(typeof(RulesDbContext))]
[Migration("20261006100000_VersionedSpellPacks")]
public sealed class VersionedSpellPacks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SpellPacks",
            columns: table => new {
                RulesetId = table.Column<string>(type: "TEXT", nullable: false),
                RulesetVersion = table.Column<string>(type: "TEXT", nullable: false),
                PackVersion = table.Column<string>(type: "TEXT", nullable: false),
                ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                DataJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_SpellPacks", x => new { x.RulesetId, x.RulesetVersion, x.PackVersion });
                table.ForeignKey("FK_SpellPacks_Rulesets_RulesetId_RulesetVersion",
                    x => new { x.RulesetId, x.RulesetVersion }, "Rulesets", new[] { "Id", "Version" },
                    onDelete: ReferentialAction.Restrict);
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("SpellPacks");
}
