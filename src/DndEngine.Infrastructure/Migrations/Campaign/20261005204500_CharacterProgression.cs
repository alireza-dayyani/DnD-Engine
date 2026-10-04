using DndEngine.Infrastructure;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DndEngine.Infrastructure.Migrations.Campaign;

[DbContext(typeof(CampaignDbContext))]
[Migration("20261005204500_CharacterProgression")]
public sealed class CharacterProgression : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "Progressions",
            columns: table => new {
                CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                StateJson = table.Column<string>(type: "TEXT", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_Progressions", x => x.CharacterId);
                table.ForeignKey("FK_Progressions_Characters_CharacterId", x => x.CharacterId,
                    "Characters", "Id", onDelete: ReferentialAction.Restrict);
            });
    }
    protected override void Down(MigrationBuilder migrationBuilder) => migrationBuilder.DropTable("Progressions");
}
