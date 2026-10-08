# Roadmap

Phases 1–3 delivered character/check/HP persistence, weapon combat, and choice-based characters with progression. Phase 4 delivered bounded magic. Phase 5 added three pinned SRD monsters, inventory, playable encounter completion, loot and an XP ledger. Phase 6 added a persistent narrative world, player-safe/DM-local query boundary, explicit consequences and quest/knowledge state. Phase 7 adds a separate authenticated loopback MCP host, typed tools, bounded context, durable direct-call replay, explicit event-linked consequence review and a scripted official-SDK playtest. See `PHASE7_REPORT.md` for verified scope and limits. Older priorities below are historical context.

Phase 4's bounded magic engine remains described in `PHASE4_REPORT.md` and `MAGIC_API.md`. Recommended later priorities, subject to separate approval:

1. Expand the immutable spell catalog and effect resolvers, including higher Mystic Arcanum choices, more class-granted spells, rituals and reactions. Add explicit pack-adoption regression tests for each expansion.
2. Finish feature and subclass action behavior, feature choices, expertise, mastery-property execution and resource triggers around the new character catalog.
3. Exact starting equipment packages, encumbrance, ammunition replenishment, held-item state, and recovery of thrown weapons. Phase 5 covers ordinary dropped inventory objects.
4. World-time rest lifecycle, stabilization/recovery and revised knockout; complete condition effects on checks and senses.
5. Require operation IDs for legacy mutation routes after a compatibility migration, then design stable tool contracts. MCP remains an adapter over Application, not a second rules engine.
6. If remote AI access is separately approved, implement and verify standards-compatible OAuth, HTTPS deployment isolation, consent and rate controls before exposing MCP publicly. Separately sourced/licensed lore retrieval remains future work. Mechanical events supply history; the AI supplies narrative interpretation.

Maps, pathfinding, a GUI, multiplayer, distributed servers and automatic narrative consequences remain outside this local backend's current scope. Phase 8 has not begun.
