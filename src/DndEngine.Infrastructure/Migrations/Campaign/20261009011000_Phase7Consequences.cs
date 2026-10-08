using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace DndEngine.Infrastructure.Migrations.Campaign;

public partial class Phase7Consequences : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name: "NarrativeConsequences",
            columns: table => new
            {
                SourceEventId = table.Column<Guid>(type: "TEXT", nullable: false),
                CampaignId = table.Column<Guid>(type: "TEXT", nullable: false),
                Status = table.Column<string>(type: "TEXT", nullable: false),
                ExpectedWorldRevision = table.Column<long>(type: "INTEGER", nullable: false),
                Cause = table.Column<string>(type: "TEXT", nullable: false),
                ChangesJson = table.Column<string>(type: "TEXT", nullable: false),
                AppliedWorldRevision = table.Column<long>(type: "INTEGER", nullable: true),
                ResolvedAtUtc = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
            },
            constraints: table =>
            {
                table.PrimaryKey("PK_NarrativeConsequences", x => x.SourceEventId);
                table.CheckConstraint("CK_NarrativeConsequence_Status",
                    "Status IN ('Proposed', 'Applied', 'Dismissed')");
                table.ForeignKey("FK_NarrativeConsequences_Campaigns_CampaignId",
                    x => x.CampaignId, "Campaigns", "Id",
                    onDelete: ReferentialAction.Restrict);
            });
        migrationBuilder.CreateIndex("IX_NarrativeConsequences_CampaignId",
            "NarrativeConsequences", "CampaignId");
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("NarrativeConsequences");
}
