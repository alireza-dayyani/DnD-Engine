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

## Combat foundation (Phase 2)

`CombatService` adds tool-shaped use cases behind `ICombatStore` and `ICombatCatalog`. `CombatEncounter` owns transitions, initiative, order and resource budgets. `WeaponAttackResolver` composes weapon rules, `ConditionEffects`, `DamageResolver`, and the existing `HitPoints`. The API only binds/routes requests. Mechanics depend on character IDs, scores, capabilities and health, never personality, lore or narrative interpretation.

`rules.db` adds one version-keyed `CombatContent` row containing the small canonical weapons/conditions/masteries pack as validated JSON and its independent SHA-256. Existing skill bytes and `Rulesets.ContentHash` are unchanged. Pack import checks counts, identifiers, all condition kinds, dice/properties/ranges, mastery references and exact installed content. Definitions inherit source URL/license from their ruleset and retain printed page references. No campaign state is stored here.

`campaign.db` adds `CombatProfiles` (character-owned capabilities, weapon instances, condition instances), `Encounters` (snapshot plus optimistic revision), and `CombatMemberships` (unique CharacterId). HP remains solely in `Characters`; encounter members reference it. Unique membership prevents parallel unfinished encounters from spending one character's resources twice. Completing combat releases memberships; dead slots remain in order and may only be advanced, not acted from.

Combat writes increment the encounter revision and every participating character revision, guarding the full mechanical read set, including targets and condition sources. The loader reads a character revision before its profile. Profiles share the character concurrency token. One EF `SaveChanges` transaction commits encounter, profiles, HP, memberships and all events. A stale revision or unique-state conflict yields 409; there is no automatic retry or reroll. This deliberately coarse concurrency scope suits local combat and avoids lost updates across Phase 1 and Phase 2 commands. GET is a read view, not a locking snapshot during concurrent writers; reload after conflicts. Restart checks use quiescent committed state.

Combat events use the existing schema-2 casing/enums and add an envelope with encounter ID/revision, round, global turn number and optional subject combatant. `AttackMade` contains participants, owned/canonical weapon IDs, source, input spatial facts/modifiers, all dice, outcome, mitigation, HP before/after, and resources. It records hits, misses, criticals, downing and deaths without duplicating those facts in several events. A query can identify a final blow by `health.before.dead=false` and `health.after.dead=true`. Membership events resolve combatant IDs to character IDs. Condition events identify their subject and source. Turn, round, expiry and death-save events record meaningful transitions. Events do not invent faction or narrative consequences.

### Extending effects

`ConditionEffects` derives action inhibition, Speed, D20 penalties, attack advantage/disadvantage, targeting restrictions, automatic saves and resistance from source-bearing instances plus HP. Ordinary duplicate condition sources do not multiply their effects; Exhaustion counts levels. `DamageResolver` folds imported defenses and condition resistance in one pipeline. `WeaponAttackResolver` consumes these facets instead of branching on weapon names. This is deliberately a small set of C# components, not an interpreter or universal effect schema.

Future features can supply additional resolved capabilities or introduce focused components for save/attack/damage facets. Before adding a source, define its stacking, expiry and interaction tests; preserve separate source instances. `AttacksPerAction` already separates the action token from individual attack uses. Mastery definitions do not grant mastery or execute effects. Phase 4 adds a bounded combat spell resolver and one persistent concentration effect; general spell triggers still require explicit implementations.

Explicit caller facts cover distance, cover, visibility after senses, nearby ranged threats, visible fear sources, and whether movement provokes an opportunity attack. The engine validates range, resources, conditions and supplied facts; it has no map to independently establish them. Unsupported geometry, grip/equipment location, grappling initiation/escape, special speeds and noncombat timed recovery must not be inferred from successful calls. See `COMBAT_API.md` for these boundaries.

## Character progression (Phase 3)

`rules.db.CharacterContent` is a third independent, SHA-256 checked, version-keyed adapted SRD pack. `campaign.db.Progressions` stores a character's chosen species, background, class allocations, feats, bonuses, hit-die pools, resources, inventory and mastery selections. `Characters` remains the shared Phase 1/2 projection for scores, HP, AC and revisions; `CombatProfiles` remains the combat projection. `CharacterDeriver` rebuilds the rich sheet and combat capabilities from the pinned definitions and choice state. It emits modifier/AC/speed provenance, feature sources, and deferred flags. Canonical rules never hold an individual's current resource use or equipment.

`ProgressionService` owns creation, level-up, item and rest use cases. The progression store commits the character row, progression row, combat profile and one event in a single SQLite transaction. Every mutation checks the character revision and unfinished combat membership; stale or enrolled changes fail without rerolling dice. Phase 1/2 imported characters remain readable and mechanically usable through their original endpoints. Phase 3 sheet/progression operations reject them with an explicit legacy-state error because their choices cannot be reconstructed safely.

The rules pack is intentionally pinned rather than silently overwritten. Content changes after a campaign uses a pack need a new version and migration policy. Phase 3 effect kinds cover passive bonuses, proficiencies, resources, senses and mastery slots; feature action execution and spells are deferred. The capability projection synchronizes new inventory weapons into combat; older Phase 2 manual capability imports and weapon grants reject choice-based characters to preserve that authority boundary.

## Spellcasting foundation (Phase 4)

`SpellSlotCalculator` derives spellcasting abilities, save DC/attack bonus, shared multiclass slots, and a separate Warlock Pact Magic pool. Progression JSON stores slot use, known cantrips, prepared spells, Wizard book entries, Sorcery Point use and Mystic Arcanum choices/use. `rules.db.SpellContent` preserves initial pack 1; `rules.db.SpellPacks` keeps immutable packs 2 and 3. New characters pin pack 3, older characters retain their pin, and `AdoptSpellPackAsync` makes an explicit audited transition. Class-level preparation and cantrip limits stay separate from shared slot availability.

`SpellCombatResolver` handles the pinned pack's healing, spell attacks, saving-throw damage and multi-target area resolution. `CombatEncounter` owns Action/Bonus Action Magic budgets, one slot expenditure per turn and active concentrating effects. `CombatService` checks class entitlement, components and slot/feature resources, then `ICombatStore` commits encounter state, all target HP, progression expenditure, profiles and audit events together behind encounter/character revision guards. Blur supplies an ongoing effect and concentration-save example. Positions, area membership, visibility and component possession are caller facts because this backend has no map or held-item model. See `MAGIC_API.md` and `PHASE4_REPORT.md` for the seven-spell boundary.

## Version expansion

Current commands reject versions other than 5.2.1, even if someone manually installs rows for another version. Before 5.2.2 support, add the content alongside 5.2.1 and introduce explicit version-specific resolver dispatch at the Application boundary; keep regression tests for both. Do not change the existing resolver behavior and assume the database pin alone preserves rules. Future rule-version upgrades must be explicit campaign operations with an audit event.

## Migrations

Both contexts have migrations and model snapshots under `src/DndEngine.Infrastructure/Migrations`. Phase 3's additive migrations were authored directly because `dotnet tool restore` could not fetch the pinned EF CLI in this environment; integration tests verify the resulting model and migration snapshots. Startup runs `MigrateAsync`, never `EnsureCreated`. Repository-local tool version is in `dotnet-tools.json`:

```powershell
dotnet tool restore
dotnet ef migrations add Name --project src/DndEngine.Infrastructure --context CampaignDbContext --output-dir Migrations/Campaign
dotnet ef migrations add Name --project src/DndEngine.Infrastructure --context RulesDbContext --output-dir Migrations/Rules
```

Only use the command for the context actually changed. Back up campaign data before future migrations. Startup migration is a deliberate local development choice, not a deployment strategy for a hosted service.
