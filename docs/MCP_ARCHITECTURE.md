# Phase 7 MCP architecture

`DndEngine.Mcp` is a separate .NET 10 ASP.NET Core process using the official `ModelContextProtocol.AspNetCore` 2.2.0 SDK and stateless Streamable HTTP at `/mcp`. It is a thin transport adapter: tool methods call Application services for rules, combat, inventory, world state and consequences. The existing development API remains separate and keeps its Phase 1–6 routes. Both processes use the same `rules.db` and `campaign.db` under `DataDirectory`.

```text
MCP SDK client -> loopback /mcp -> JWT validation -> campaign/character access
           -> typed tool -> Application service -> SQLite state and audit events
                         -> direct-call idempotency transaction for commands
```

The local host accepts `http://127.0.0.1`, `localhost` or `::1` only. It checks the remote address and Host header as well as binding to loopback. The MCP endpoint requires a valid 15-minute bearer token. A validated `sub` GUID identifies a subject; the database, not a role claim or tool argument, defines that subject's DM/player membership and character ownership. Trusted local CLI commands provision those rows. Every tool checks campaign membership; character-specific player tools check ownership. The adapter has no arbitrary SQL, filesystem or script tool.

Commands use the existing Phase 5 `IdempotencyOperations` table. `DurableMcpCommandRunner` fingerprints the authenticated subject, tool and typed input, then commits its claim, Application-layer changes, audit events and exact successful result in one SQLite transaction. A retry with the same operation ID and input returns the saved result. A different input conflicts; failed commands roll back. Concurrent duplicates were exercised in integration tests. The HTTP API keeps its own existing middleware and compatible table.

`NarrativeConsequenceService` identifies eligible `MonsterDefeated` and `EncounterCompleted` events. The DM can propose typed Phase 6 world changes and revise an unresolved proposal after the world changes. Applying or dismissing requires the `reviewToken` returned with the exact proposal the DM reviewed; a changed proposal requires a new review. Source event ID is the primary key of the consequence record; apply and resolution commit together. No event changes world state automatically.

For local setup, create a campaign with the existing API and keep that API on loopback. Build the solution, then in PowerShell configure one shared data directory and a fresh random key:

```powershell
$env:DataDirectory = 'C:\path\to\private-dnd-data'
$key = New-Object byte[] 48
$rng = [Security.Cryptography.RandomNumberGenerator]::Create()
try { $rng.GetBytes($key) } finally { $rng.Dispose() }
$env:DND_MCP_AUTH_KEY = [Convert]::ToBase64String($key)
$env:DND_MCP_URL = 'http://127.0.0.1:5544'
$mcp = 'src/DndEngine.Mcp/bin/Debug/net10.0/DndEngine.Mcp.dll'
dotnet $mcp admin grant-dm <campaign-guid> <subject-guid>
dotnet $mcp admin grant-player <campaign-guid> <subject-guid> <character-guid>
dotnet $mcp admin token <subject-guid>
dotnet $mcp
```

Use the same key for token issuance and hosting, protect it and the data directory with OS permissions, and renew tokens after 15 minutes. The `token` command prints a bearer credential to stdout; do not put it in repository files or shell history. The host also accepts `DND_MCP_ISSUER` and `DND_MCP_AUDIENCE` if both issuance and validation use the same values. A client-specific illustrative configuration is:

```json
{
  "mcpServers": {
    "dnd-engine-local": {
      "url": "http://127.0.0.1:5544/mcp",
      "headers": { "Authorization": "Bearer <short-lived-local-token>" }
    }
  }
}
```

Client configuration formats vary. The reproducible SDK client is `scripts/McpSmoke.ps1`; it seeds isolated data, runs discovery and the playtest, and verifies restart. `DND_MCP_PROBE_TOKEN` is used only by its local client probe command.

Remote connectivity is a design task, not an enabled mode. A remote service would need HTTPS, an OAuth 2.1 authorization server with authorization code and PKCE, protected-resource metadata, per-user consent/scopes, token audience validation, and deployment isolation for the development API. The [MCP authorization specification](https://modelcontextprotocol.io/specification/2025-11-25/basic/authorization) and [official SDK transport guidance](https://github.com/modelcontextprotocol/csharp-sdk/blob/main/docs/concepts/transports/transports.md) inform that design. This local JWT prototype is not a remote OAuth implementation.
