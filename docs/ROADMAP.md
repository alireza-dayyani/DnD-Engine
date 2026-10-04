# Roadmap

Phase 1 delivered character/check/HP/persistence foundations. Phase 2 delivered the bounded weapon-combat foundation described in `PHASE2_REPORT.md`. Phase 3 adds SRD character choices, a derived sheet, level progression, a general inventory, armor, class resources, rests and a multiclass foundation. See `PHASE3_REPORT.md` for tested scope and deliberate gaps. Older paragraphs below are retained as historical planning context; they are not a description of the current implementation.

Phase 4 should be separately approved and scoped before implementation. Recommended priorities:

1. Spell definitions, prepared/known spells, slots, multiclass spell-slot calculation, casting and concentration with reusable spell effects.
2. Finish feature and subclass action behavior, feature choices, expertise, mastery-property execution and resource triggers around the new character catalog.
3. Exact starting equipment package choices and quantities, encumbrance, ammunition replenishment, held-item state, and recovery of thrown/dropped items.
4. World-time rest lifecycle, stabilization/recovery and revised knockout; complete condition effects on checks and senses.
5. Idempotency and stable tool contracts before an AI adapter retries commands. MCP remains an adapter over Application, not a second rules engine.
6. Campaign NPC identity, relationships, quests and world state; separately sourced/licensed lore retrieval. Mechanical events supply history; the AI supplies narrative interpretation.

Maps, pathfinding, a GUI, multiplayer, distributed servers and automatic narrative consequences remain outside this local backend's current scope.
