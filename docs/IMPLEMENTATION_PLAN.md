# Phase 1 implementation plan

Repository inspected 2026-09-29: empty; .NET SDK 10.0.400 available. Official SRD landing page identifies 5.2.1 as latest. Relevant source sections read: pp. 5–9, 16–18, and Unconscious p. 191; legal notice p. 1 and CC BY 4.0.

1. Domain: validated ability scores, character level and proficiency, dice expressions/roller port, character aggregate with HP/death transitions, structured checks.
2. Application: transport-independent commands/results, narrow persistence and content ports; orchestration and version dispatch. No narrative behavior.
3. Infrastructure: separate EF Core SQLite contexts, migrations, versioned skill JSON import, atomic state/event saves, optimistic concurrency.
4. API: local development adapter, DTOs, explicit validation/not-found/conflict responses, no EF objects exposed.
5. Verify: deterministic domain/application tests, real SQLite integration tests, HTTP smoke script including restart. Run restore/build/test and report evidence.

Risks and decisions before implementation:
- Ordinary checks/saves do not inherit attack natural-1/20 rules. Saves may voluntarily fail.
- Zero HP needs unconscious state, massive damage, damage-at-zero failures and healing restrictions. Include basic explicit death-save use case; no turn scheduler/combat.
- Temporary HP does not stack; caller chooses replacement. It expires on long-rest completion; full rest resolution is deferred and no partial rest endpoint will pretend otherwise.
- Rules DB cannot have a cross-database FK. Application validates campaign rules pin and skill references; import never overwrites a version. Unsupported versions fail explicitly.
- Events accompany ordinary state, not event sourcing. State and audit event commit in one transaction. Conflicts must not silently overwrite state.
- Character creation is a validated sheet import, not a class/background character builder. Scores above 20 require future feature support and are rejected on import.
- No proprietary setting material or speculative future tables. Future domain concepts are documented rather than scaffolded as empty entities.
