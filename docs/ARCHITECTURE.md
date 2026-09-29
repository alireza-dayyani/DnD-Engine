# Architecture

The AI interprets intent, chooses a relevant check/DC, and narrates results. The engine validates commands, resolves mechanics, persists authoritative state, and records structured facts. No narration is generated here.

```text
AI Dungeon Master [future]
        ↓
MCP adapter [future]       Development HTTP adapter
        └─────────────────────────┘
                      ↓
                 Application
                      ↓
                Domain / Rules
                      ↓ persistence port called by Application
                 Infrastructure
                      ↓
           rules.db      campaign.db

Lore retrieval [future, separate knowledge boundary]
```

Runtime flow is not the compile-time dependency direction. **Domain has zero project or external package references. Application references Domain. Infrastructure references Application (and its Domain types). API references Application and Infrastructure for composition.** Domain never calls SQL/HTTP/MCP. A future MCP adapter can call the same services without duplicating rules.

## Practical boundaries

- Domain: validated scores/level/dice value objects, Character aggregate, health transitions, check resolver, definition records.
- Application: `CampaignService`, `CharacterService`, `MechanicsService`; immutable result/command records; narrow `ICampaignStore` and `IRulesCatalog` ports; explicit supported-version guard. These use cases are suitable future tool methods.
- Infrastructure: EF row classes distinct from domain models and DTOs; SQLite contexts, migrations, seed importer; thread-safe production random source. DI and logging use Microsoft.Extensions.
- API: binding, enum serialization, response/error mapping, dependency setup. HTTP does not appear in Application or Domain. No EF entities are returned.

## Storage

`rules.db`: `Rulesets` composite key (Id,Version); `Skills` composite key (RulesetId,Version,Id) with FK. One embedded JSON content pack provides all 18 skills. Import is transactional and idempotent, refuses mismatched hashes/rows, and never overwrites an installed pin. No public write API for rules. This is application-enforced immutability, not protection against manual database edits; startup detects changed skill content.

`campaign.db`: `Campaigns`, `Characters`, `Events`; campaign/character FKs restrict deletion. Character identity, level, AC, campaign ID, and revision are columns. Small aggregate components (six abilities, proficiency sets, health state) are JSON columns reconstructed through domain validation. This avoids unnecessary child tables in Phase 1. Migrate these components to relational tables when actual queries require it; changing JSON shape also requires an explicit data migration.

There are no cross-database FKs. Application validates supported rules and content before creating or mechanically operating on a campaign. A ruleset pin and schema version are different: EF migrations evolve storage; SRD versions select mechanics/content. Back up both databases together with the API stopped (or use SQLite's backup API); copying only a live `.db` can omit WAL transactions.

## Audit and conflicts

One `SaveChangesAsync` transaction commits character changes and an event. Every successful mechanical command increments the character revision, including checks, so check results are ordered relative to state changes. A concurrency token rejects stale updates as 409; no automatic dice reroll/retry. Global event sequence supplies stable cursor pagination; each event has a unique ID, campaign and optional character IDs, type, UTC timestamp, rules pin, payload schema version, and character revision. Unique character/revision pairs prevent duplicate timeline positions.

Events are an append-only application history, not event sourcing: reads use current state, not replay. Creation is also audited. There are no event-edit/delete endpoints. Internal SQL access remains a trusted developer operation; no AI database access. Payload schema 2 uses camelCase properties and string enums, like the outer API contracts, and damage includes the critical flag. Schema 1 was used by the initial development smoke campaign and has PascalCase properties/numeric enums. Those audit entries remain untouched. Consumers must honor SchemaVersion rather than infer payload shape.

Commands have no retry/idempotency token yet. After uncertain network delivery, inspect state/events before retrying. SQLite and optimistic concurrency suffice for local single-user use. Distributed writers, multiplayer, authentication, queues, caching, and server infrastructure are deliberately absent.

## Version expansion

Current commands reject versions other than 5.2.1, even if someone manually installs rows for another version. Before 5.2.2 support, add the content alongside 5.2.1 and introduce explicit version-specific resolver dispatch at the Application boundary; keep regression tests for both. Do not change the existing resolver behavior and assume the database pin alone preserves rules. Future rule-version upgrades must be explicit campaign operations with an audit event.

## Migrations

Both contexts have generated initial migrations and model snapshots under `src/DndEngine.Infrastructure/Migrations`. Startup runs `MigrateAsync`, never `EnsureCreated`. Repository-local tool version is in `dotnet-tools.json`:

```powershell
dotnet tool restore
dotnet ef migrations add Name --project src/DndEngine.Infrastructure --context CampaignDbContext --output-dir Migrations/Campaign
dotnet ef migrations add Name --project src/DndEngine.Infrastructure --context RulesDbContext --output-dir Migrations/Rules
```

Only use the command for the context actually changed. Back up campaign data before future migrations. Startup migration is a deliberate local development choice, not a deployment strategy for a hosted service.
