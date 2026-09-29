using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndEngine.Infrastructure.Migrations.Rules
{
    /// <inheritdoc />
    public partial class CombatContent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CombatContent",
                columns: table => new
                {
                    RulesetId = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false),
                    DataJson = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CombatContent", x => new { x.RulesetId, x.Version });
                    table.ForeignKey(
                        name: "FK_CombatContent_Rulesets_RulesetId_Version",
                        columns: x => new { x.RulesetId, x.Version },
                        principalTable: "Rulesets",
                        principalColumns: new[] { "Id", "Version" },
                        onDelete: ReferentialAction.Restrict);
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CombatContent");
        }
    }
}
