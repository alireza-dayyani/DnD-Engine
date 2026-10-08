using DndEngine.Application;
using Microsoft.EntityFrameworkCore;

namespace DndEngine.Infrastructure;

public sealed class CampaignAccessRow
{
    public Guid CampaignId { get; set; }
    public Guid SubjectId { get; set; }
    public string Role { get; set; } = "";
}

public sealed class CharacterOwnershipRow
{
    public Guid CharacterId { get; set; }
    public Guid CampaignId { get; set; }
    public Guid SubjectId { get; set; }
}

public sealed class SqliteCampaignAccessStore(CampaignDbContext db) : ICampaignAccessStore
{
    public async Task<CampaignRole?> RoleAsync(Guid campaignId,Guid subjectId,CancellationToken ct)
    {
        var row=await db.CampaignAccess.AsNoTracking().SingleOrDefaultAsync(
            x=>x.CampaignId==campaignId && x.SubjectId==subjectId,ct);
        return row?.Role switch { "Dm"=>CampaignRole.Dm,
            "Player"=>CampaignRole.Player,_=>null };
    }

    public Task<bool> OwnsAsync(Guid campaignId,Guid subjectId,Guid characterId,
        CancellationToken ct) => db.CharacterOwnership.AsNoTracking().AnyAsync(
            x=>x.CampaignId==campaignId && x.SubjectId==subjectId &&
                x.CharacterId==characterId,ct);
}
