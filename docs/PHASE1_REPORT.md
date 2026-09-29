# Phase 1 completion report

Verified on **2026-09-29** in `C:\Users\Alireza\source\repos\D&D Engine`.

The foundation and first mechanical slice are implemented. Restore and build succeeded; **100 tests passed, zero failed, zero skipped**. A real HTTP walkthrough with a full process restart also passed. Phase 2 has not been implemented.

## Research

- Exact target: **SRD 5.2.1**, ruleset ID `dnd-5.5`. The [official SRD landing page](https://www.dndbeyond.com/srd) still lists this as the latest English release. Checked September 29, 2026; the page reports a March 2, 2026 update.
- Primary mechanical source: [official English PDF](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf), particularly printed pp. 5–9, 16–18, and p. 191. The research covered the relevant slice, not every spell/class/monster in the entire document.
- Licensing: SRD is **CC BY 4.0**. Its exact required attribution paragraph is included in `ATTRIBUTION.md` and README, with adaptation notice and links. [License deed](https://creativecommons.org/licenses/by/4.0/) and [legal code](https://creativecommons.org/licenses/by/4.0/legalcode) were read. Original code has no owner-selected distribution license yet.
- No proprietary Forgotten Realms books or 2014 rules were imported. Rules, campaign facts, and future setting knowledge remain separate.
- Findings that directly affected implementation: ordinary checks/saves use total-versus-DC, not attack natural-1/20 behavior; advantage/disadvantage cancel regardless of source counts; saves may be failed voluntarily; temporary HP never stacks and expires on long-rest completion; zero HP requires proper unconscious/death state; unconscious Strength/Dexterity saves automatically fail; waking does not remove Prone.
- Revised melee knockout would leave **1 HP**, so the older zero-HP knockout behavior was not added. Knockouts are deferred with combat.

`RULES_SOURCES.md` contains page-level mappings, explicit scope boundaries, and the documented reading of damage at zero while temporary HP absorbs HP loss. The official PDF is linked rather than copied into the repository.

## Architecture

```text
src/
  DndEngine.Domain/
  DndEngine.Application/
  DndEngine.Infrastructure/
  DndEngine.Api/
tests/
  DndEngine.Domain.Tests/
  DndEngine.Application.Tests/
  DndEngine.IntegrationTests/
docs/
scripts/Smoke.ps1
```

Domain has no external/package/project dependencies. Application depends on Domain. Infrastructure implements Application ports and references Application. API references Application and Infrastructure only to bind requests and compose dependencies. HTTP, persistence details, and future MCP do not enter domain behavior.

The engine owns mechanical truth; no AI calls or narrative-generation logic exist. A future MCP adapter can call `CampaignService`, `CharacterService`, and `MechanicsService` directly without database access or copied game logic.

Separate SQLite contexts preserve the rules/campaign boundary. Current-state tables remain authoritative, with structured audit events beside them. State and its event commit together; stale character writes fail explicitly. No event sourcing, generic repository framework, mediator, distributed cache, queues, or cloud services were added.

Technology: .NET SDK 10.0.400, target `net10.0`, C#, EF Core **10.0.12**, SQLite, xUnit, Microsoft dependency injection and logging. Nullable types and warnings-as-errors are enabled. EF tooling is pinned locally in `dotnet-tools.json`.

Restore initially exposed a NuGet filesystem restriction; the authorized restore succeeded with the necessary tool permission. Restore also detected the vulnerable transitive SQLite 2.1.11 native bundle. The final project explicitly uses `SQLitePCLRaw.bundle_e_sqlite3` **3.0.5**; final restore/build has no dependency audit warnings. This is a version override of the existing SQLite provider dependency, not another database technology.

## Implementation

Domain models: `Ruleset`, `Campaign`, `Character`, `AbilityScore`, `CharacterLevel`, `SkillDefinition`, `DiceExpression`, `DiceResult`, `HitPoints`, `HealthState`, `HealthChange`, and structured `CheckResult`. Proficiency sets and AC remain deliberately small concepts rather than unnecessary standalone entity hierarchies.

Dice: d4, d6, d8, d10, d12, d20, d100; count and signed integer modifier; explicit parse errors; immutable expression/results. `IDiceRoller` has `RandomDiceRoller` for production and `FixedDiceRoller` for deterministic tests. Every automated test that rolls dice uses injected fixed dice; manual production smoke checks assert arithmetic and die-selection invariants instead of lucky results.

Application use cases:

- Create/read campaign with immutable rules pin.
- Import/read character fundamentals, six scores, level-derived proficiency, skill/save training, HP and AC.
- Ability check, skill check with optional ability override, ability saving throw.
- Apply damage/healing, choose temporary-HP replacement, make a basic death save.
- Retrieve ordered campaign events with cursor pagination.

The 18 canonical skills are an embedded JSON pack imported into `rules.db`, with stable IDs, usual abilities, source/version, license metadata and a content-pack SHA-256. Startup is idempotent and refuses modified installed content. No weapon/spell/monster content is hardcoded as name-based behavior.

API routes:

| Method | Path |
|---|---|
| GET | `/health` |
| POST | `/campaigns` |
| GET | `/campaigns/{id}` |
| POST | `/characters` |
| GET | `/characters/{id}` |
| POST | `/characters/{id}/checks/ability` |
| POST | `/characters/{id}/checks/skill` |
| POST | `/characters/{id}/saving-throws` |
| POST | `/characters/{id}/damage` |
| POST | `/characters/{id}/heal` |
| POST | `/characters/{id}/temporary-hp` |
| POST | `/characters/{id}/death-saving-throws` |
| GET | `/campaigns/{id}/events` |

DTOs are separate from EF rows. Invalid input/rules return 400, missing entities 404, optimistic conflicts 409. Required JSON constructor fields are enforced; enums are names. The development profile binds loopback and does not launch a browser.

Events include creation, ability/skill checks, saves, damage, healing, temporary HP and death saves. Each includes actor/campaign IDs, type, timestamp, source pin, payload schema, revision, and structured results. Final payload schema is **2**, with camelCase fields and string enums. Schema 1 from the first development smoke campaign is retained unchanged; no historical event was silently rewritten.

## Exact mechanics and boundaries

Implemented: floor ability modifiers; PC proficiency progression through level 20; one applicable proficiency bonus; raw d20 and modifier breakdown; equality succeeds; high/low selection; cancellation; voluntary/automatic failed saves; HP loss floored at zero; healing capped at maximum; temporary buffer and replacement choice; massive damage; unconsciousness and persistent Prone; damage-at-zero failures including critical hits; basic death saves, natural 1/20, stabilization after three successes, death after three failures; healing resets counters and ends HP-zero unconsciousness. Ordinary healing cannot revive a dead character.

Character creation is a **sheet import**, not a class/background builder. Normal PC scores are constrained to 1–20. The domain value object supports up to 30 for future explicitly supported features; import does not pretend those features already exist. AC defaults to 10 + Dexterity modifier or accepts an already-resolved AC. Maximum HP is fixed positive sheet data in this slice.

Deferred mechanics are explicit: feature-derived proficiency/Expertise, tools, Heroic Inspiration/rerolls, all class features, attacks and critical determination, resistance/immunity/vulnerability, spellcasting/resources, full conditions, knockout, full rests/time progression, Help/Medicine stabilization, timed stable recovery, maximum-HP reduction, resurrection, and modified death saves. Temporary HP depletes correctly, but automatic long-rest expiry awaits a complete rest use case. No endpoint falsely claims to execute a rest.

## Verification

Final commands completed successfully:

```text
dotnet restore
dotnet build
dotnet test --logger "trx;LogFilePrefix=final" --results-directory artifacts/test-results
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/Smoke.ps1
```

Build: **0 warnings, 0 errors**.

| Suite | Passed | Failed | Skipped |
|---|---:|---:|---:|
| Domain | 82 | 0 | 0 |
| Application | 10 | 0 | 0 |
| Integration, including HTTP | 8 | 0 | 0 |
| Total | **100** | **0** | **0** |

Coverage includes all requested rule categories and invalid dice/state inputs; natural-1/20 ordinary-check boundaries; unconscious saves; temp HP/zero/death transitions; campaign/character persistence; exact fixed-die skill results through Application; damage/healing reloads; audit persistence/pagination; migration/model consistency; importer idempotence/tamper detection; stale-write rejection; rollback when event insertion fails; HTTP contracts and error codes. This is a test count, not a claim of 100% code or rules coverage.

TRX evidence:

- `artifacts/test-results/final_net10.0_20260929104342.trx` — Domain.
- `artifacts/test-results/final_net10.0_20260929104345.trx` — Application.
- `artifacts/test-results/final_net10.0_20260929104350.trx` — Integration.

Final real-process HTTP walkthrough evidence: `artifacts/smoke-20260929-104331/result.json` and adjacent startup/restart logs. Created campaign `75488aa7-928a-48f8-a24a-197c7193239b`, character `0dad16a6-56fc-4ce8-b934-99bcedd6c5ff`.

Observed results:

1. Campaign and character creation/retrieval succeeded; initial HP 20/20.
2. Normal Deception: d20 **13**, ability +4, proficiency +3, total **20** versus DC 15: success.
3. Advantage Deception: **[13, 3]**, selected **13**, total **20**: success.
4. Wisdom save: **12 + 3 + 3 = 18**, versus DC 15: success.
5. Damage 7: HP **20 → 13**. Healing 3: HP **13 → 16**.
6. API process stopped and a new process started against the same files.
7. Retrieved **16/20 HP**, character revision **5**, and **7 events**, ending in `CharacterHealed`.

The smoke script stopped its API process afterward. It can be rerun; each run creates another demonstration campaign. The fixed-die automated integration test separately verifies the requested illustrative total of **11 + 4 + 3 = 18**.

## Database

Actual development database locations:

- `C:\Users\Alireza\source\repos\D&D Engine\data\rules.db`
- `C:\Users\Alireza\source\repos\D&D Engine\data\campaign.db`

`rules.db` has Rulesets and Skills. `campaign.db` has Campaigns, Characters, and Events. Each context also has EF migration metadata. Character component snapshots use JSON columns; identity, level, AC and concurrency revision have ordinary columns. Events are ordered by an integer sequence and indexed by campaign; unique IDs and character/revision pairs protect timeline consistency. Foreign keys remain within each database. Application validates cross-database rules references.

Initial generated migrations:

- `20260929070602_InitialCampaign` with Campaign model snapshot.
- `20260929070606_InitialRules` with Rules model snapshot.

Startup applies migrations with `MigrateAsync`; no `EnsureCreated` shortcut. Both snapshots match the current EF model, verified by tests. No destructive migration or automatic campaign rules-version upgrade exists. Data and generated test artifacts are ignored by version control. Integration tests use isolated temporary directories; no test points at the normal development campaign database.

## Known limitations

- This is an intentionally partial rules engine, not legal full character creation or full combat. It cannot automatically verify imported class-derived HP, proficiencies, AC, or circumstantial modifiers.
- Only SRD 5.2.1 is executable. Future versions require explicit behavior dispatch and regression tests, not just changing metadata. Existing campaigns retain their pins.
- Full rest/condition/time handling is absent; do not claim long-rest completion, timed recovery, or standing has occurred through this API.
- Death saves are basic unmodified rolls; no advantage, feature bonuses or voluntary failure option is exposed for that separate endpoint.
- No command idempotency key yet. A client must inspect state/events before retrying an uncertain request. Conflicts do not automatically retry consumed random rolls.
- Rules immutability and timeline append-only behavior are application guarantees, not protection against a human editing SQLite files. The API has no authentication and is intended for loopback/local use.
- Bounds on dice counts, names, DC, damage/HP and AC are documented engine input limits, not additional SRD claims.
- No code-distribution license selected, no hosted deployment, no Git commit/PR requested or created.

## Changes from the specification

The requested solution structure and technologies were retained. Small additions are limited to what makes the slice coherent:

- Basic death-save and temporary-HP endpoints, plus campaign read and readiness routes. These allow testing zero-HP behavior without building combat. Review the documented limits before using modified death saves or time-dependent effects.
- Creation events, optimistic concurrency, event schema versions, and bounded timeline pagination. These preserve audit reliability and future compatibility without adopting event sourcing.
- Small character components are persisted as JSON instead of speculative child tables. No future class/inventory/spell tables were created; the domain expansion decisions are documented. Review this choice when query requirements expand.
- Explicit sheet import rather than a complete character creator; exceptional scores above 20 are intentionally refused until supporting features exist. This follows the prompt's limited phase but should be clear to callers.
- A patched transitive SQLite bundle is pinned because dependency audit rejected the older default.

The SRD reading for damage at zero while temporary HP is present is highlighted in `RULES_SOURCES.md` for review. No silent 2014/revised-rule combination or proprietary setting content was introduced.

## Next phase recommendation

After approval, implement a bounded weapon-combat slice: supporting condition/rest/stabilization lifecycle; a small weapon/armor pack with equipment-derived AC; initiative and turn order; legal attacks, criticals and typed damage mitigation; atomic encounter history and restart recovery. Add idempotent commands before automatic AI-tool retries. Keep complete spells/classes, lore retrieval, grids and multiplayer outside that phase. See `ROADMAP.md` for acceptance criteria.

**Phase 2 awaits user approval.**
