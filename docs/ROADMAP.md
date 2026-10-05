# Roadmap

Phase 1 delivered character/check/HP/persistence foundations. Phase 2 delivered the bounded weapon-combat foundation described in `PHASE2_REPORT.md`. Phase 3 adds SRD character choices, a derived sheet, level progression, a general inventory, armor, class resources, rests and a multiclass foundation. See `PHASE3_REPORT.md` for tested scope and deliberate gaps. Older paragraphs below are retained as historical planning context; they are not a description of the current implementation.

Phase 4 is underway on `codex/phase4-magic-foundation`. It derives spellcasting abilities, attack/save DCs, shared full/half-caster slots, Warlock Pact Magic slots and class-specific prepared-spell allowances; persists slot expenditure and rest recovery; and supports prepared Cure Wounds and Healing Word self-casts outside combat. Spell packs are pinned per character. Eligible spells can be added on class level-up or replaced at the class-appropriate time. See `PHASE4_REPORT.md`. Recommended priorities for the rest of Phase 4:

1. Expand the versioned spell catalog and model cantrips, Wizard spellbooks and always-prepared spells; then add combat casting, concentration and reusable effects for more spells.
2. Finish feature and subclass action behavior, feature choices, expertise, mastery-property execution and resource triggers around the new character catalog.
3. Exact starting equipment package choices and quantities, encumbrance, ammunition replenishment, held-item state, and recovery of thrown/dropped items.
4. World-time rest lifecycle, stabilization/recovery and revised knockout; complete condition effects on checks and senses.
5. Idempotency and stable tool contracts before an AI adapter retries commands. MCP remains an adapter over Application, not a second rules engine.
6. Campaign NPC identity, relationships, quests and world state; separately sourced/licensed lore retrieval. Mechanical events supply history; the AI supplies narrative interpretation.

Maps, pathfinding, a GUI, multiplayer, distributed servers and automatic narrative consequences remain outside this local backend's current scope.
