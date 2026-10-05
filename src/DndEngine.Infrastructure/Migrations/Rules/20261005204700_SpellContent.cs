using DndEngine.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DndEngine.Infrastructure.Migrations.Rules;

[DbContext(typeof(RulesDbContext))]
[Migration("20261005204700_SpellContent")]
public sealed class SpellContent : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "SpellContent",
            columns: table => new {
                RulesetId = table.Column<string>(type: "TEXT", nullable: false),
                Version = table.Column<string>(type: "TEXT", nullable: false),
                ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                DataJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_SpellContent", x => new { x.RulesetId, x.Version });
                table.ForeignKey("FK_SpellContent_Rulesets_RulesetId_Version", x => new { x.RulesetId, x.Version },
                    "Rulesets", new[] { "Id", "Version" }, onDelete: ReferentialAction.Restrict);
            });
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("SpellContent");
}
