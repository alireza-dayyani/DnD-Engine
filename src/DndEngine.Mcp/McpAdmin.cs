using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using DndEngine.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Tokens;

namespace DndEngine.Mcp;

/// <summary>Trusted local provisioning; no administrative operation is exposed as an MCP tool.</summary>
public static class McpAdmin
{
    public static async Task RunAsync(IServiceProvider services,string[] args)
    {
        if (args.Length<1) throw new ArgumentException(
            "Use admin grant-dm <campaign> <subject>, grant-player <campaign> <subject> <character>, or token <subject>.");
        await using var scope=services.CreateAsyncScope();
        var db=scope.ServiceProvider.GetRequiredService<CampaignDbContext>();
        var verb=args[0];
        if (verb=="token" && args.Length==2)
        {
            var subject=Parse(args[1]);
            if (!await db.CampaignAccess.AnyAsync(x=>x.SubjectId==subject))
                throw new InvalidOperationException("Subject has no campaign membership.");
            var auth=scope.ServiceProvider.GetRequiredService<McpLocalAuthOptions>();
            var now=DateTime.UtcNow;
            var token=new JwtSecurityToken(auth.Issuer,auth.Audience,
                [new Claim("sub",subject.ToString("D")),new Claim(JwtRegisteredClaimNames.Jti,
                    Guid.NewGuid().ToString("D"))],now,now.AddMinutes(15),
                new SigningCredentials(new SymmetricSecurityKey(auth.SigningKey),
                    SecurityAlgorithms.HmacSha256));
            Console.WriteLine(new JwtSecurityTokenHandler().WriteToken(token));
            return;
        }
        if (verb is "grant-dm" or "grant-player")
        {
            if (args.Length!=(verb=="grant-dm" ? 3 : 4))
                throw new ArgumentException("Invalid grant arguments.");
            var campaign=Parse(args[1]); var subject=Parse(args[2]);
            if (!await db.Campaigns.AnyAsync(x=>x.Id==campaign))
                throw new InvalidOperationException("Campaign does not exist.");
            Guid? character=null;
            if (verb=="grant-player")
            {
                character=Parse(args[3]);
                if (!await db.Characters.AnyAsync(x=>x.Id==character && x.CampaignId==campaign))
                    throw new InvalidOperationException("Character is not in this campaign.");
            }
            await using var tx=await db.Database.BeginTransactionAsync();
            var access=await db.CampaignAccess.SingleOrDefaultAsync(x=>
                x.CampaignId==campaign && x.SubjectId==subject);
            if (access is null)
                db.CampaignAccess.Add(new() { CampaignId=campaign,SubjectId=subject,
                    Role=verb=="grant-dm" ? "Dm" : "Player" });
            else access.Role=verb=="grant-dm" ? "Dm" : "Player";
            if (character is { } characterId)
            {
                var existing=await db.CharacterOwnership.SingleOrDefaultAsync(x=>x.CharacterId==characterId);
                if (existing is not null && (existing.SubjectId!=subject || existing.CampaignId!=campaign))
                    throw new InvalidOperationException("Character is already owned by another subject.");
                if (existing is null) db.CharacterOwnership.Add(new() { CharacterId=characterId,
                    CampaignId=campaign,SubjectId=subject });
            }
            await db.SaveChangesAsync();
            await tx.CommitAsync();
            Console.WriteLine($"Granted {verb} for subject {subject:D} in campaign {campaign:D}.");
            return;
        }
        throw new ArgumentException("Unknown admin command.");
    }

    private static Guid Parse(string text) => Guid.TryParse(text,out var id) && id!=Guid.Empty
        ? id : throw new ArgumentException("Expected a nonempty GUID.");
}
