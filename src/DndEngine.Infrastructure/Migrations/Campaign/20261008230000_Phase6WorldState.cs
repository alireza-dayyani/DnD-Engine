using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace DndEngine.Infrastructure.Migrations.Campaign;

[DbContext(typeof(CampaignDbContext))]
[Migration("20261008230000_Phase6WorldState")]
public sealed class Phase6WorldState : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.CreateTable(
            name:"WorldStates",
            columns:table=>new {
                CampaignId=table.Column<Guid>(type:"TEXT",nullable:false),
                Revision=table.Column<long>(type:"INTEGER",nullable:false),
                StateJson=table.Column<string>(type:"TEXT",nullable:false)
            },
            constraints:table=>{
                table.PrimaryKey("PK_WorldStates",x=>x.CampaignId);
                table.ForeignKey("FK_WorldStates_Campaigns_CampaignId",x=>x.CampaignId,
                    "Campaigns","Id",onDelete:ReferentialAction.Restrict);
                table.CheckConstraint("CK_WorldState_Revision","Revision >= 1");
            });
    }

    protected override void Down(MigrationBuilder migrationBuilder) =>
        migrationBuilder.DropTable("WorldStates");
}
