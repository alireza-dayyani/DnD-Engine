# Phase 2 plan and baseline

2026-09-29: inspected Phase 1 code, documentation, migrations and tests. Baseline `dotnet restore`, `dotnet build`, `dotnet test` passed (100 tests, zero warnings). Phase 1 real-process smoke passed: `artifacts/smoke-20260929-140817/result.json`. Working tree clean, `main` equals `origin/main` at `cd37c14`.

1. Add pure combat domain behavior: encounter transitions, explicit initiative ties, action/bonus/reaction economy, walking/crawling/difficult terrain, typed damage, weapon attacks and composable condition effects.
2. Add independently versioned weapons/conditions/masteries content packs without modifying the Phase 1 skill pack or its hash. Import all 38 SRD weapon definitions and 15 conditions; mastery definitions only (no progression eligibility or mastery automation).
3. Persist mechanical combat profiles/active conditions and character-owned weapon instances separately from canonical rules. Reuse existing character HP; never copy it into encounter state. Add encounter snapshots and membership constraints. Transactions check character and encounter revisions and save effects/events together; never retry dice commands.
4. Add Application use cases and thin API routes. Spatial facts (distance, cover, visibility, fear-source sight, opportunity trigger) are explicit caller input, not a fake map. Engine owns their mechanical consequences. No narrative systems or MCP.
5. Test deterministic initiative/ties/actions/movement/attacks/damage/conditions/death, upgrades from Phase 1 DBs, transactional rollback, concurrency, restart and HTTP. Run both real API smoke scripts, update docs/report, review diff, commit with short messages, push configured GitHub remote.

Scope decisions: keep Phase 1 ordinary behavior unchanged. Reuse PC HP resolution and add explicit monster-zero-HP policy. New combat-profile import supplies resolved speed/proficiencies/defenses; class eligibility remains external. Use Light weapon extra attacks for a real bonus-action use case; no arbitrary free bonus actions. Death saves occur once at actual turn start. Dead slots remain in initiative order (cannot act); GM explicitly ends battle. Full rests, armor derivation, spell duration/saving triggers, special movement speeds, mastery automation, grappling initiation/escape, spellcasting and full inventory are deferred and must be documented.

Attachment ends mid-sentence in section 29; implement the intended real API combat/restart smoke test from the preceding requirements.
