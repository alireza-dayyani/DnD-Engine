using System.Net;
using System.Text;
using DndEngine.Infrastructure;
using DndEngine.Mcp;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using ModelContextProtocol.AspNetCore;

if (args.Length>0 && args[0].Equals("client-probe",StringComparison.OrdinalIgnoreCase))
{
    await McpClientProbe.RunAsync(args.Skip(1).ToArray());
    return;
}
if (args.Length>0 && args[0].Equals("client-playtest",StringComparison.OrdinalIgnoreCase))
{
    await McpPlaytest.RunAsync(args.Skip(1).ToArray());
    return;
}
if (args.Length>0 && args[0].Equals("client-resume",StringComparison.OrdinalIgnoreCase))
{
    await McpPlaytest.ResumeAsync(args.Skip(1).ToArray());
    return;
}
var admin=args.Length>0 && args[0].Equals("admin",StringComparison.OrdinalIgnoreCase);
var builder=WebApplication.CreateBuilder(admin ? [] : args);
var keyText=builder.Configuration["DND_MCP_AUTH_KEY"] ??
    throw new InvalidOperationException("DND_MCP_AUTH_KEY is required.");
byte[] key;
try { key=Convert.FromBase64String(keyText); }
catch (FormatException) { throw new InvalidOperationException("DND_MCP_AUTH_KEY must be base64."); }
if (key.Length<32) throw new InvalidOperationException("DND_MCP_AUTH_KEY must contain at least 32 random bytes.");
var issuer=builder.Configuration["DND_MCP_ISSUER"] ?? "dnd-engine-local";
var audience=builder.Configuration["DND_MCP_AUDIENCE"] ?? "dnd-engine-mcp";
if (string.IsNullOrWhiteSpace(issuer) || string.IsNullOrWhiteSpace(audience))
    throw new InvalidOperationException("MCP issuer and audience are required.");
if (!admin)
{
    var url=builder.Configuration["DND_MCP_URL"] ?? "http://127.0.0.1:5544";
    if (!Uri.TryCreate(url,UriKind.Absolute,out var parsed) ||
        parsed.Scheme!=Uri.UriSchemeHttp || parsed.Port<=0 ||
        parsed.Host is not ("127.0.0.1" or "localhost" or "::1") ||
        !string.IsNullOrEmpty(parsed.UserInfo))
        throw new InvalidOperationException("The local MCP host must bind to a loopback HTTP URL.");
    builder.WebHost.UseUrls(url);
}
builder.Logging.ClearProviders();
if (!admin) builder.Logging.AddConsole();
builder.Services.AddSingleton(new McpLocalAuthOptions(issuer,audience,key));
builder.Services.AddHttpContextAccessor();
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options=>
{
    options.MapInboundClaims=false;
    options.TokenValidationParameters=new()
    {
        ValidateIssuer=true,ValidIssuer=issuer,ValidateAudience=true,ValidAudience=audience,
        ValidateIssuerSigningKey=true,IssuerSigningKey=new SymmetricSecurityKey(key),
        ValidateLifetime=true,RequireExpirationTime=true,RequireSignedTokens=true,
        ClockSkew=TimeSpan.Zero,NameClaimType="sub"
    };
});
builder.Services.AddAuthorization();
var dataDirectory=builder.Configuration["DataDirectory"] ??
    Path.Combine(AppContext.BaseDirectory,"data");
Directory.CreateDirectory(Path.Combine(dataDirectory,"mcp-keys"));
builder.Services.AddDataProtection().SetApplicationName("DndEngine.Mcp")
    .PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataDirectory,"mcp-keys")));
builder.Services.AddDndEngine(dataDirectory);
builder.Services.AddMcpServer()
    .WithHttpTransport(options=>options.SessionMode=HttpServerSessionMode.Stateless)
    .AddAuthorizationFilters()
    .WithTools<CampaignMcpTools>();

var app=builder.Build();
await app.Services.InitializeDndEngineAsync();
if (admin)
{
    await McpAdmin.RunAsync(app.Services,args.Skip(1).ToArray());
    return;
}
app.Use(async (context,next)=>
{
    var host=context.Request.Host.Host;
    if (context.Connection.RemoteIpAddress is not { } address ||
        !IPAddress.IsLoopback(address) ||
        host is not ("127.0.0.1" or "localhost" or "[::1]" or "::1"))
    {
        context.Response.StatusCode=403;
        return;
    }
    await next(context);
});
app.UseAuthentication();
app.UseAuthorization();
app.MapMcp("/mcp").RequireAuthorization();
await app.RunAsync();

public sealed record McpLocalAuthOptions(string Issuer,string Audience,byte[] SigningKey);
public partial class Program;
