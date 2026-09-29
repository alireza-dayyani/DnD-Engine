# Phase 2 report

Implemented and validated on 2026-09-29 against the official SRD 5.2.1. Phase 1 architecture, SQLite separation, campaign pin, skill pack/hash, original endpoints and stored events are preserved. No project replacement, MCP, narrative systems, map, class builder or spell engine was introduced.

## Delivered

- Created → Initiative → Active → Completed encounters, shared identical-monster initiative, revised surprise, explicit player/GM tie adjudication, persistent order/round/turn, and one unfinished encounter per character.
- Action budgets with imported attacks per action, Light bonus attacks, opportunity reactions, movement/Dash/Dodge/Disengage, crawl/difficult-terrain costs, and standing.
- Validated sourced content for all 38 weapons, 15 conditions, 13 damage types and 8 mastery definitions. Canonical rules stay in `rules.db`; owned weapons, ammunition, capabilities and condition instances stay in `campaign.db`.
- Weapon ability/proficiency selection, natural attack outcomes, advantage cancellation, reach/range/cover, Finesse/Heavy/Light/Loading/Thrown/TwoHanded/Versatile/Ammunition handling, critical dice, fixed damage, mitigation ordering, temporary HP, existing PC zero-HP rules and explicit monster death policy.
- Reusable condition facets for attacks, saves, initiative, movement, targeting, action inhibition and resistance. Source-specific removal, explicit turn-boundary expiry, grapple release on source incapacitation, Exhaustion and automatically scheduled basic death saves.
- Transport-independent combat use cases, thin HTTP endpoints and structured command results. Audit records carry the facts needed to identify the attacker, target, weapon, round, critical, damage, downing and final blow.
- Additive migrations for combat content/profiles/encounters/enrollment. One transaction commits all participating HP/profiles/resources/events with character and encounter revision guards. Failed random commands return conflicts without rerolling.

## Validation

Baseline, before modifications: restore/build passed; all 100 Phase 1 tests passed; original process smoke passed (`artifacts/smoke-20260929-140817/result.json`).

Final build: **0 warnings, 0 errors**. Full deterministic suite: **167 passed, 0 failed, 0 skipped** (141 Domain, 10 Application, 16 integration); 67 new cases added. Test dice are injected fixed sequences, never random outcome assertions.

Coverage includes initiative/ties/grouping, transitions/order/rounds, action/bonus/reaction/movement consumption/reset, normal/hit/miss/critical/advantage attacks, weapon properties, typed mitigation ordering, temporary HP, zero HP/death, all condition kinds' in-scope facets, source removal/expiry, malformed actions, content tampering, atomic rollback and stale encounter/character writes. The concurrency test commits an intervening character change after combat loading and before the attack roll, verifies exactly one attack/damage roll, and checks no combat event, ammo spend or action spend escaped rollback.

The realistic HTTP scenario runs Vaelaris versus two goblins with fixed dice, a critical kill, counterattack, poison, round advancement and a completely new API host over the same databases. It verifies identical full encounter/HP/conditions/event history after restart, then continues and completes combat. A separate migration test builds the original Phase 1 schema, writes character HP/events, upgrades through the new migrations, and verifies prior data and skill hash unchanged.

Real-process smoke scripts also passed on the final build:

| Script | Evidence | Verified |
|---|---|---|
| `scripts/Smoke.ps1` | `artifacts/smoke-20260929-144820/result.json` | Original checks/HP survive restart; HP 16/20, revision 5, seven events |
| `scripts/CombatSmoke.ps1` | `artifacts/combat-smoke-20260929-144820/result.json` | Three combatants, attack, movement, condition, round 2, identical state/timeline after process restart, continuation/completion |

Smoke scripts run the production roller and assert arithmetic/resource/persistence invariants rather than demand random hits. Their databases, logs and evidence are intentionally ignored by Git. Rerun them to generate local evidence.

## Decisions and limitations

See `COMBAT_API.md` for exact contracts and coverage, and `ARCHITECTURE.md` for effect/persistence extension points. Mastery definitions are stored with Automated=false; no eligibility or effects are inferred. Feature entitlement, armor-derived AC, extra spell damage, general duration triggers, full inventory/recovery, special movement, grappling initiation/escape, rest/stabilization/knockout, skill/sensory/social condition consumers, and narrative consequences remain deferred. Visibility, distance, cover and opportunity triggers are caller-adjudicated facts; no spatial simulation is claimed.

The original Phase 1 endpoints retain standalone semantics. Combat attacks/saves/turn advancement enforce the new rules; the original raw HP/check/death-save endpoints do not consume combat resources or apply all condition/monster hooks. Callers should not manually issue extra death saves during combat. This distinction is explicit rather than silently changing earlier behavior.

Phase 1 health code gained explicit prone/death transitions and an optional death-save modifier used for combat Exhaustion; default standalone behavior is unchanged. Existing tests remain, with only the applied-migration count updated for the additional campaign migration. No unrelated Phase 1 defect required a behavior change.

The supplied attachment ended midway through section 29. The smoke script implements the intended real API combat and restart demonstration based on the preceding complete requirements. No missing subsequent requirements were invented.
