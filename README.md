# D&D campaign engine — Phase 1

A local .NET 10 backend for deterministic mechanics, persistent campaign state, and an audit timeline. A future AI Dungeon Master supplies interpretation and narration; the engine owns dice and state. No AI calls, MCP, graphical UI, or complete combat system are implemented.

## Run

Requires .NET SDK 10.0.400 or a later 10.0.4xx patch (see `global.json`). From the repository root:

```powershell
dotnet restore
dotnet build
dotnet test
dotnet run --project src/DndEngine.Api --no-build --launch-profile http
```

The development API listens on `http://127.0.0.1:5225`. Startup applies separate EF Core migrations, imports the 18 SRD skills, and validates the installed content pack. Databases normally reside at repository-root `data/rules.db` and `data/campaign.db`; `--DataDirectory C:\path\to\data` overrides this. The directory must be writable. Use only a local development listener: there is no authentication.

Run the actual HTTP/process-restart demonstration after building:

```powershell
powershell -ExecutionPolicy Bypass -File scripts/Smoke.ps1
```

It creates a new demonstration campaign in `data/`, verifies checks and damage/healing, restarts the API, checks persistent state/events, and exits. It refuses an occupied port; stop the development API first or pass `-Port 5226`. Evidence and process logs go to `artifacts/smoke-<timestamp>/`. It does not delete campaigns.

## Mechanical scope

Validated character sheet import; six scores; levels 1–20 and derived proficiency; skills/saves; unarmored AC by default or explicit imported AC; dice expressions; checks/saves and DCs; advantage/disadvantage cancellation; voluntary failed saves; damage/healing/temporary HP; basic PC zero-HP/death-save transitions. Rich results and atomic state/audit writes. Rules pinned to `dnd-5.5 / 5.2.1`.

This is **not a character builder**: class, species, background, equipment, and feature eligibility are not checked. Import resolved sheet values. Scores above 20 require unsupported features and are rejected. Damage is an already-resolved amount, not an attack or resistance calculation. Full rests, time passage, stabilizing actions, and effect expiry are deferred. Do not advance a campaign through those operations using this phase alone.

## Development API

| Method | Route | Purpose |
|---|---|---|
| GET | `/health` | Startup readiness and supported ruleset |
| POST / GET | `/campaigns`, `/campaigns/{id}` | Create/read campaign and immutable rules pin |
| POST / GET | `/characters`, `/characters/{id}` | Import/read mechanical sheet |
| POST | `/characters/{id}/checks/ability` | Plain ability check |
| POST | `/characters/{id}/checks/skill` | Skill check; optional ability override |
| POST | `/characters/{id}/saving-throws` | Ability save, including voluntary failure |
| POST | `/characters/{id}/damage`, `/heal` | Apply resolved HP change |
| POST | `/characters/{id}/temporary-hp` | Keep existing buffer or replace it |
| POST | `/characters/{id}/death-saving-throws` | Basic unmodified death save |
| GET | `/campaigns/{id}/events?after=0&limit=100` | Ordered timeline; maximum page size 500 |

Enums use strings, skill IDs use lower-case kebab-case (`deception`, `animal-handling`). Both advantage flags mean a normal roll. Modifiers/DCs are explicit GM inputs; feature automation is deferred. API uses 400 for invalid rules/input, 404 for missing entities, 409 for stale concurrent writes. After a conflict, reload and explicitly decide whether to repeat; random rolls are not automatically retried. Commands have no idempotency keys, so do not blindly resend after a timeout.

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
- `src/DndEngine.Infrastructure`: SQLite/EF, migrations, skill importer, random dice.
- `src/DndEngine.Api`: HTTP development adapter and dependency composition.
- `tests/`: domain, application, real SQLite, and HTTP tests. All test dice are fixed.
- [Architecture](docs/ARCHITECTURE.md), [rules sources](docs/RULES_SOURCES.md), [domain model](docs/DOMAIN_MODEL.md), [roadmap](docs/ROADMAP.md), [implementation plan](docs/IMPLEMENTATION_PLAN.md), [Phase 1 report](docs/PHASE1_REPORT.md).

## Attribution

This work includes material from the System Reference Document 5.2.1 (“SRD 5.2.1”) by Wizards of the Coast LLC, available at https://www.dndbeyond.com/srd. The SRD 5.2.1 is licensed under the Creative Commons Attribution 4.0 International License, available at https://creativecommons.org/licenses/by/4.0/legalcode.

See [ATTRIBUTION.md](ATTRIBUTION.md) for the adaptation notice. No license for original project code has been selected by the owner; do not infer that the SRD license licenses all original code.
