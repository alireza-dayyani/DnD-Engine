# D&D campaign engine — Phase 7 local MCP foundation

A local .NET 10 backend for SRD 5.2.1 characters, combat, magic and encounters, plus persistent campaign narrative state and an audit timeline. A future AI Dungeon Master supplies interpretation and narration; the engine owns rules and current state. Phase 7 adds a separate authenticated, loopback-only MCP host and a scripted official-SDK client playtest. There are no built-in AI calls or graphical UI.

## Run

Requires .NET SDK 10.0.400 or a later 10.0.4xx patch (see `global.json`). From the repository root:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/DndEngine.Api --no-build --launch-profile http
```

The development API listens on `http://127.0.0.1:5225`. Startup applies separate EF Core migrations and validates pinned content: 18 skills, 38 weapons, 15 conditions, 8 mastery definitions, and the Phase 3 character catalog. Databases normally reside at repository-root `data/rules.db` and `data/campaign.db`; `--DataDirectory C:\path\to\data` overrides this. The directory must be writable. Use only a local development listener: there is no authentication.

Run the actual HTTP/process-restart demonstration after building:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Smoke.ps1
powershell -ExecutionPolicy Bypass -File scripts/CombatSmoke.ps1
powershell -ExecutionPolicy Bypass -File scripts/CharacterSmoke.ps1
powershell -ExecutionPolicy Bypass -File scripts/EncounterSmoke.ps1
powershell -ExecutionPolicy Bypass -File scripts/WorldSmoke.ps1
powershell -ExecutionPolicy Bypass -File scripts/McpSmoke.ps1
```

Each smoke uses isolated databases under `artifacts/`, exercises actual HTTP and a process restart, saves evidence/logs, and stops its process in `finally`. The Phase 5 script covers combat, spell and item use, XP and loot. `WorldSmoke.ps1` covers explicit world changes, secret isolation, quests, restart and retry. `McpSmoke.ps1` uses authenticated DM/player SDK clients for discovery, commands, consequence review, secret checks and restart. Scripts refuse occupied ports (existing defaults 5225/5226/5227/5528/5530; MCP defaults 5546/5547); override the appropriate `-Port`, `-ApiPort` or `-McpPort`.

## Mechanical scope

Validated character sheet import; six scores; levels 1–20 and derived proficiency; skills/saves; unarmored AC by default or explicit imported AC; dice expressions; checks/saves and DCs; advantage/disadvantage cancellation; voluntary failed saves; damage/healing/temporary HP; basic PC zero-HP/death-save transitions. Rich results and atomic state/audit writes. Rules pinned to `dnd-5.5 / 5.2.1`.

Phase 2 adds encounter lifecycle, shared monster initiative and explicit ties, durable turns/rounds, action/bonus/reaction resources, movement/Dash/Dodge/Disengage, weapon ownership and ammunition, typed attacks/criticals/damage mitigation, source-aware conditions, combat saves, and automatic death saves at turn start. See [combat API and boundaries](docs/COMBAT_API.md).

Phase 3 adds a choice-based path to create an SRD character and derive its mechanical sheet. It models 9 species, 4 backgrounds, 12 classes, feats, levels 1–20, passive feature effects, multiclassing foundations, class resources, armor and inventory, hit dice and rests. Phase 4 derives class casting stats, multiclass and Pact Magic slots, and preparation/cantrip limits. The current pack has five cantrips, area damage, Blur concentration and Mystic Arcanum. Character choices include Wizard books, a Fiend spell grant and four Sorcerer Metamagic options. Font of Magic, Arcane Recovery, Sorcerous Restoration and Memorize Spell have executable paths. Combat casting spends the correct turn/resource budget and atomically saves target HP, spell resources and audit events. See [Magic API](docs/MAGIC_API.md), [Phase 3 report](docs/PHASE3_REPORT.md), and [Phase 4 report](docs/PHASE4_REPORT.md). Many active class/subclass/feat/species powers remain deferred.

Phase 5 uses the same combat and spell resolvers for Goblin Minion, Skeleton and Priest Acolyte. Characters can acquire, transfer, drop, pick up, equip and consume supported items, track copper, complete an encounter with an explicit outcome, claim defeated-monster gear and record XP allocations. See [Encounter API](docs/ENCOUNTER_API.md) and [Phase 5 report](docs/PHASE5_REPORT.md).

Phase 6 stores a separate narrative world with named NPCs, hierarchical locations, factions, directional relationships, world truth and holder-specific beliefs, and explicit quest progress. A player-safe read omits secret/private state; full state and mutations require loopback access to the local DM development route. Narrative commands never infer consequences from combat or grant mechanical rewards. Phase 7 exposes an authenticated MCP adapter and explicit consequence review; see [MCP architecture](docs/MCP_ARCHITECTURE.md), [tools](docs/MCP_TOOLS.md), [security](docs/SECURITY.md) and [Phase 7 report](docs/PHASE7_REPORT.md).

## Development API

| Method | Route | Purpose |
|---|---|---|
| GET | `/health` | Startup readiness and supported ruleset |
| POST / GET | `/campaigns`, `/campaigns/{id}` | Create/read campaign and immutable rules pin |
| POST | `/campaigns/{id}/time/advance` | Advance revisioned campaign game time between encounters |
| POST / GET | `/characters`, `/characters/{id}` | Import/read mechanical sheet |
| GET / POST | `/character-choices`, `/srd-characters` | Read choices/create choice-based SRD character |
| GET / POST | `/characters/{id}/sheet`, `/characters/{id}/level-up` | Derived sheet and progression |
| GET | `/characters/{id}/spellcasting` | Casting abilities and current/maximum slot balances |
| GET | `/spells` | Read an immutable spell pack (current pack 7 by default) |
| POST | `/characters/{id}/spell-slots/spend` | Record one shared or Pact Magic slot expenditure |
| POST | `/characters/{id}/spells/cast-self` | Cast a prepared self-healing spell on the caster outside combat |
| POST | `/characters/{id}/spells/comprehend-languages/cast`, `/check` | Cast and check a timed language-comprehension effect |
| POST | `/characters/{id}/spell-pack/adopt` | Explicitly adopt current spell content |
| POST | `/combat/{id}/spells/cast` | Cast an implemented spell in an active encounter |
| POST | `/characters/{id}/inventory`, `/inventory/equip`, `/inventory/unequip`, `/inventory/remove` | Legacy Phase 3 item routes |
| GET / POST | `/campaigns/{id}/monster-definitions`, `/monsters`, `/combat/{id}/monsters` | Pinned monsters and encounter enrollment |
| GET / POST | `/characters/{id}/inventory/state`, `/inventory/items`, `/inventory/transfer` | Quantity-aware inventory and transfer |
| POST | `/combat/{id}/items/use`, `/combat/{id}/complete` | Consumable turn action and explicit outcome |
| GET / POST | `/combat/{id}/rewards`, `/rewards/experience`, `/rewards/loot` | Reward ledger and claims |
| POST | `/characters/{id}/resources/spend`, `/rests/short`, `/rests/long` | Resource use and recovery |
| POST | `/characters/{id}/checks/ability` | Plain ability check |
| POST | `/characters/{id}/checks/skill` | Skill check; optional ability override |
| POST | `/characters/{id}/saving-throws` | Ability save, including voluntary failure |
| POST | `/characters/{id}/damage`, `/heal` | Apply resolved HP change |
| POST | `/characters/{id}/temporary-hp` | Keep existing buffer or replace it |
| POST | `/characters/{id}/death-saving-throws` | Basic unmodified death save |
| GET | `/campaigns/{id}/events?after=0&limit=100` | Ordered timeline; maximum page size 500 |
| GET | `/campaigns/{id}/world` | Player-safe public/discovered world projection |
| GET / POST | `/dm/campaigns/{id}/world/`, `/changes` | Loopback-only DM state and atomic consequences |

Enums use strings, skill IDs use lower-case kebab-case (`deception`, `animal-handling`). Both advantage flags mean a normal roll. DCs and situational modifiers remain explicit GM inputs. API uses 400 for invalid rules/input, 404 for missing entities, 409 for stale concurrent writes. After a conflict, reload and explicitly decide whether to repeat; random rolls are not automatically retried. New Phase 5–6 mutations require `X-Operation-Id`; legacy mutation routes accept it optionally. Reuse the same GUID with identical request bytes to recover the original successful response after a timeout. The ordinary campaign timeline excludes Phase 6 narrative events because their audit payloads may contain secrets; DM history is available only on the loopback route.

Examples after creating IDs:

```json
{"skillId":"deception","dc":15,"advantage":true,"disadvantage":false,"otherModifier":0}
```

```json
{"ability":"Wisdom","dc":15,"voluntaryFailure":false}
```

See `scripts/Smoke.ps1` for a complete character-creation request and executable walkthrough.

## Project and documentation

- `src/DndEngine.Domain`: pure mechanics; no infrastructure dependencies.
- `src/DndEngine.Application`: DTOs, use cases, content and persistence ports.
- `src/DndEngine.Infrastructure`: SQLite/EF, migrations, pinned skills/combat/character content importers, random dice.
- `src/DndEngine.Api`: HTTP development adapter and dependency composition.
- `tests/`: domain, application, real SQLite, and HTTP tests. All test dice are fixed.
- [Architecture](docs/ARCHITECTURE.md), [rules sources](docs/RULES_SOURCES.md), [domain model](docs/DOMAIN_MODEL.md), [roadmap](docs/ROADMAP.md), [Phase 1 report](docs/PHASE1_REPORT.md), [Phase 2 report](docs/PHASE2_REPORT.md), [Phase 3 report](docs/PHASE3_REPORT.md), [Phase 4 report](docs/PHASE4_REPORT.md), [Phase 5 report](docs/PHASE5_REPORT.md), [Phase 6 report](docs/PHASE6_REPORT.md), [Phase 7 report](docs/PHASE7_REPORT.md), [combat API](docs/COMBAT_API.md), [character API](docs/CHARACTER_API.md), [magic API](docs/MAGIC_API.md), [encounter API](docs/ENCOUNTER_API.md), [world API](docs/WORLD_API.md), [MCP architecture](docs/MCP_ARCHITECTURE.md), [MCP tools](docs/MCP_TOOLS.md), [MCP security](docs/SECURITY.md), [DM context](docs/DM_CONTEXT.md).

## Attribution

This work includes material from the System Reference Document 5.2.1 (“SRD 5.2.1”) by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

See [ATTRIBUTION.md](ATTRIBUTION.md) for the adaptation notice. No license for original project code has been selected by the owner; do not infer that the SRD license licenses all original code.
