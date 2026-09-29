# Roadmap

Phase 1 delivered character/check/HP/persistence foundations. Phase 2 delivers the bounded weapon-combat foundation described in `PHASE2_REPORT.md`: sourced weapon/condition/mastery definitions, turn resources, attacks and mitigation, condition facets, durable encounters, and explainable transactional events. Existing sheets/events/rules pins survive additive migration. The user's Phase 2 brief superseded the earlier tentative roadmap; armor, rests and full recovery were not part of this implementation.

Follow-on work needs an explicit scope:

1. General duration/time and rest lifecycle, stabilization/recovery and revised knockout, complete condition effects on skill/sensory/social checks, concentration and automatic trigger handling.
2. Full inventory/equipment/armor, ammunition replenishment and thrown/dropped item recovery, additional movement modes, contextual grappling and escape.
3. Character progression and feature eligibility, mastery execution, extra resources and targeted action-economy extensions. Imported attack counts do not establish feature entitlement.
4. Spell definitions, casting, slots and reusable spell effects, with interaction tests.
5. Idempotency and stable tool contracts before an AI adapter retries commands. MCP remains an adapter over Application, not a second rules engine.
6. Campaign NPC identity, relationships, quests and world state; separately sourced/licensed lore retrieval. Mechanical events supply history; the AI supplies narrative interpretation.

Maps, pathfinding, a GUI, multiplayer, distributed servers and automatic narrative consequences remain outside this local backend's current scope.
