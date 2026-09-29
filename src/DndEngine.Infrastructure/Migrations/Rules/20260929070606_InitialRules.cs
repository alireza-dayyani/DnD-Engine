using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndEngine.Infrastructure.Migrations.Rules
{
    /// <inheritdoc />
    public partial class InitialRules : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Rulesets",
                columns: table => new
                {
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    SourceUrl = table.Column<string>(type: "TEXT", nullable: false),
                    License = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHash = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Rulesets", x => new { x.Id, x.Version });
                });

            migrationBuilder.CreateTable(
                name: "Skills",
                columns: table => new
                {
                    RulesetId = table.Column<string>(type: "TEXT", nullable: false),
                    Version = table.Column<string>(type: "TEXT", nullable: false),
                    Id = table.Column<string>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Ability = table.Column<string>(type: "TEXT", nullable: false),
                    Source = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Skills", x => new { x.RulesetId, x.Version, x.Id });
                    table.ForeignKey(
                        name: "FK_Skills_Rulesets_RulesetId_Version",
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
                name: "Skills");

            migrationBuilder.DropTable(
                name: "Rulesets");
        }
    }
}
