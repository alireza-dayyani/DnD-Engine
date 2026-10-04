# D&D campaign engine — Phase 4 foundation

A local .NET 10 backend for SRD 5.2.1 character choices and progression, weapon combat, persistent campaign state, and an audit timeline. A future AI Dungeon Master supplies interpretation and narration; the engine owns dice and state. Phase 4 has started with derived spellcasting abilities and slot summaries. Spell definitions and casting are still in progress; there are no AI calls, MCP or graphical UI.

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
```

Each smoke uses isolated databases under `artifacts/`, exercises actual HTTP and a process restart, saves evidence/logs, and stops its API process in `finally`. The first checks Phase 1 mechanics, the second combat, and the third character creation, progression, inventory and rests. Scripts refuse occupied ports (defaults 5225/5226/5227; override with `-Port`).

## Mechanical scope

Validated character sheet import; six scores; levels 1–20 and derived proficiency; skills/saves; unarmored AC by default or explicit imported AC; dice expressions; checks/saves and DCs; advantage/disadvantage cancellation; voluntary failed saves; damage/healing/temporary HP; basic PC zero-HP/death-save transitions. Rich results and atomic state/audit writes. Rules pinned to `dnd-5.5 / 5.2.1`.

Phase 2 adds encounter lifecycle, shared monster initiative and explicit ties, durable turns/rounds, action/bonus/reaction resources, movement/Dash/Dodge/Disengage, weapon ownership and ammunition, typed attacks/criticals/damage mitigation, source-aware conditions, combat saves, and automatic death saves at turn start. See [combat API and boundaries](docs/COMBAT_API.md).

Phase 3 adds a choice-based path to create an SRD character and derive its mechanical sheet. It models 9 species, 4 backgrounds, 12 classes, feats, levels 1–20, passive feature effects, multiclassing foundations, class resources, armor and inventory, hit dice and rests. Phase 4 currently derives each casting class's ability, attack bonus and save DC, shared multiclass spell slots, and Warlock Pact Magic slots. It does not yet track slot expenditure or prepared spells, or resolve spell effects. See [character API](docs/CHARACTER_API.md), [Phase 3 report](docs/PHASE3_REPORT.md), and [Phase 4 report](docs/PHASE4_REPORT.md). Many active class/subclass/feat/species powers remain deferred.

## Development API

| Method | Route | Purpose |
|---|---|---|
| GET | `/health` | Startup readiness and supported ruleset |
| POST / GET | `/campaigns`, `/campaigns/{id}` | Create/read campaign and immutable rules pin |
| POST / GET | `/characters`, `/characters/{id}` | Import/read mechanical sheet |
| GET / POST | `/character-choices`, `/srd-characters` | Read choices/create choice-based SRD character |
| GET / POST | `/characters/{id}/sheet`, `/characters/{id}/level-up` | Derived sheet and progression |
| GET | `/characters/{id}/spellcasting` | Derived casting abilities and SRD slot maxima |
| POST | `/characters/{id}/inventory`, `/inventory/equip`, `/inventory/unequip`, `/inventory/remove` | Acquire and equip items |
| POST | `/characters/{id}/resources/spend`, `/rests/short`, `/rests/long` | Resource use and recovery |
| POST | `/characters/{id}/checks/ability` | Plain ability check |
| POST | `/characters/{id}/checks/skill` | Skill check; optional ability override |
| POST | `/characters/{id}/saving-throws` | Ability save, including voluntary failure |
| POST | `/characters/{id}/damage`, `/heal` | Apply resolved HP change |
| POST | `/characters/{id}/temporary-hp` | Keep existing buffer or replace it |
| POST | `/characters/{id}/death-saving-throws` | Basic unmodified death save |
| GET | `/campaigns/{id}/events?after=0&limit=100` | Ordered timeline; maximum page size 500 |

Enums use strings, skill IDs use lower-case kebab-case (`deception`, `animal-handling`). Both advantage flags mean a normal roll. DCs and situational modifiers remain explicit GM inputs; Phase 3 derives the supported character features. API uses 400 for invalid rules/input, 404 for missing entities, 409 for stale concurrent writes. After a conflict, reload and explicitly decide whether to repeat; random rolls are not automatically retried. Commands have no idempotency keys, so do not blindly resend after a timeout.

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
- [Architecture](docs/ARCHITECTURE.md), [rules sources](docs/RULES_SOURCES.md), [domain model](docs/DOMAIN_MODEL.md), [roadmap](docs/ROADMAP.md), [Phase 1 report](docs/PHASE1_REPORT.md), [Phase 2 report](docs/PHASE2_REPORT.md), [Phase 3 report](docs/PHASE3_REPORT.md), [Phase 4 report](docs/PHASE4_REPORT.md), [combat API](docs/COMBAT_API.md), [character API](docs/CHARACTER_API.md).

## Attribution

This work includes material from the System Reference Document 5.2.1 (“SRD 5.2.1”) by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

See [ATTRIBUTION.md](ATTRIBUTION.md) for the adaptation notice. No license for original project code has been selected by the owner; do not infer that the SRD license licenses all original code.
