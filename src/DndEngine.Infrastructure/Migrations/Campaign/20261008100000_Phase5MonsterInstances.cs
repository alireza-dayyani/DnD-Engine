using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DndEngine.Infrastructure.Migrations.Campaign;

[DbContext(typeof(CampaignDbContext))]
[Migration("20261008100000_Phase5MonsterInstances")]
public sealed class Phase5MonsterInstances : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "MonsterInstances",
            columns: table => new {
                Id = table.Column<Guid>(type: "TEXT", nullable: false),
                CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                DefinitionId = table.Column<string>(type: "TEXT", nullable: false),
                PackVersion = table.Column<string>(type: "TEXT", nullable: false),
                SpellPackVersion = table.Column<string>(type: "TEXT", nullable: false),
                LimitedUsesJson = table.Column<string>(type: "TEXT", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_MonsterInstances", x => x.Id);
                table.ForeignKey("FK_MonsterInstances_Characters_Id", x => x.Id,
                    "Characters", "Id", onDelete: ReferentialAction.Restrict);
                table.ForeignKey("FK_MonsterInstances_Campaigns_CampaignId", x => x.CampaignId,
                    "Campaigns", "Id", onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name: "IX_MonsterInstances_CampaignId",
            table: "MonsterInstances", column: "CampaignId");
        migrationBuilder.CreateTable(
            name: "IdempotencyOperations",
            columns: table => new {
                OperationId = table.Column<Guid>(type: "TEXT", nullable: false),
                RequestHash = table.Column<string>(type: "TEXT", nullable: false),
                StatusCode = table.Column<int>(type: "INTEGER", nullable: false),
                ResponseBody = table.Column<byte[]>(type: "BLOB", nullable: false),
                ContentType = table.Column<string>(type: "TEXT", nullable: true),
                Location = table.Column<string>(type: "TEXT", nullable: true),
                CompletedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
            },
            constraints: table => table.PrimaryKey("PK_IdempotencyOperations",x=>x.OperationId));
        migrationBuilder.CreateTable(
            name: "InventoryStates",
            columns: table => new {
                OwnerId = table.Column<Guid>(type: "TEXT", nullable: false),
                ItemsJson = table.Column<string>(type: "TEXT", nullable: false),
                CopperPieces = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_InventoryStates",x=>x.OwnerId);
                table.ForeignKey("FK_InventoryStates_Characters_OwnerId",x=>x.OwnerId,
                    "Characters","Id",onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "EncounterRewards",
            columns: table => new {
                EncounterId = table.Column<Guid>(type: "TEXT", nullable: false),
                Outcome = table.Column<string>(type: "TEXT", nullable: false),
                AvailableExperience = table.Column<int>(type: "INTEGER", nullable: false),
                AwardsJson = table.Column<string>(type: "TEXT", nullable: false),
                DefeatedMonsterIdsJson = table.Column<string>(type: "TEXT", nullable: false),
                Revision = table.Column<long>(type: "INTEGER", nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_EncounterRewards",x=>x.EncounterId);
                table.ForeignKey("FK_EncounterRewards_Encounters_EncounterId",x=>x.EncounterId,
                    "Encounters","Id",onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateTable(
            name: "DroppedItems",
            columns: table => new {
                Id = table.Column<Guid>(type: "TEXT",nullable: false),
                CampaignId = table.Column<Guid>(type: "TEXT",nullable: false),
                SourceOwnerId = table.Column<Guid>(type: "TEXT",nullable: false),
                DefinitionId = table.Column<string>(type: "TEXT",nullable: false),
                Quantity = table.Column<int>(type: "INTEGER",nullable: false)
            },
            constraints: table => {
                table.PrimaryKey("PK_DroppedItems",x=>x.Id);
                table.ForeignKey("FK_DroppedItems_Campaigns_CampaignId",x=>x.CampaignId,
                    "Campaigns","Id",onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex(name:"IX_DroppedItems_CampaignId",
            table:"DroppedItems",column:"CampaignId");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropTable("IdempotencyOperations");
        migrationBuilder.DropTable("EncounterRewards");
        migrationBuilder.DropTable("DroppedItems");
        migrationBuilder.DropTable("InventoryStates");
        migrationBuilder.DropTable("MonsterInstances");
    }
}
