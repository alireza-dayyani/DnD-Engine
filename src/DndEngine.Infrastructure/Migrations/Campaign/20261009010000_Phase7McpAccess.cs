using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndEngine.Infrastructure.Migrations.Campaign
{
    /// <inheritdoc />
    public partial class Phase7McpAccess : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CampaignAccess",
                columns: table => new
                {
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Role = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CampaignAccess", x => new { x.CampaignId, x.SubjectId });
                    table.CheckConstraint("CK_CampaignAccess_Role", "Role IN ('Dm', 'Player')");
                    table.ForeignKey(
                        name: "FK_CampaignAccess_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "CharacterOwnership",
                columns: table => new
                {
                    CharacterId = table.Column<Guid>(type: "TEXT", nullable: false),
                    CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                    SubjectId = table.Column<Guid>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CharacterOwnership", x => x.CharacterId);
                    table.ForeignKey(
                        name: "FK_CharacterOwnership_Campaigns_CampaignId",
                        column: x => x.CampaignId,
                        principalTable: "Campaigns",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CharacterOwnership_Characters_CharacterId",
                        column: x => x.CharacterId,
                        principalTable: "Characters",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_CharacterOwnership_CampaignId_SubjectId",
                table: "CharacterOwnership",
                columns: new[] { "CampaignId", "SubjectId" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CampaignAccess");

            migrationBuilder.DropTable(
                name: "CharacterOwnership");
        }
    }
}
