using System.Security.Claims;

namespace DndEngine.Application;

public enum CampaignRole { Player, Dm }

public interface ICampaignAccessStore
{
    Task<CampaignRole?> RoleAsync(Guid campaignId, Guid subjectId, CancellationToken ct);
    Task<bool> OwnsAsync(Guid campaignId, Guid subjectId, Guid characterId, CancellationToken ct);
}

public sealed class AccessDeniedException : Exception
{
    public AccessDeniedException() : base("Campaign access denied.") { }
}

public sealed class CampaignAccessService(ICampaignAccessStore store)
{
    public static Guid Subject(ClaimsPrincipal principal)
    {
        var value=principal.FindFirst(ClaimTypes.NameIdentifier)?.Value ??
            principal.FindFirst("sub")?.Value;
        return Guid.TryParse(value,out var id) && id!=Guid.Empty
            ? id : throw new AccessDeniedException();
    }

    public async Task<CampaignRole> RequireMemberAsync(ClaimsPrincipal principal,
        Guid campaignId,CancellationToken ct=default)
    {
        if (campaignId==Guid.Empty) throw new AccessDeniedException();
        return await store.RoleAsync(campaignId,Subject(principal),ct)
            ?? throw new AccessDeniedException();
    }

    public async Task RequireDmAsync(ClaimsPrincipal principal,Guid campaignId,
        CancellationToken ct=default)
    {
        if (await RequireMemberAsync(principal,campaignId,ct)!=CampaignRole.Dm)
            throw new AccessDeniedException();
    }

    public async Task RequireCharacterAsync(ClaimsPrincipal principal,Guid campaignId,
        Guid characterId,CancellationToken ct=default)
    {
        var role=await RequireMemberAsync(principal,campaignId,ct);
        if (role==CampaignRole.Dm) return;
        if (characterId==Guid.Empty ||
            !await store.OwnsAsync(campaignId,Subject(principal),characterId,ct))
            throw new AccessDeniedException();
    }
}
