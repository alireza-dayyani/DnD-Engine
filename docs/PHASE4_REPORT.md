# Phase 4 spellcasting foundation

Phase 4 has started on branch `codex/phase4-magic-foundation`.

The first increment adds a pure domain derivation for class spellcasting ability, spell attack bonus, spell save DC, shared slot maxima, and separate Warlock Pact Magic slot maxima. It follows SRD 5.2.1 multiclass rules: Bard, Cleric, Druid, Sorcerer and Wizard levels count in full; Paladin and Ranger levels count at half rounded up; Warlock Pact Magic stays separate. A spellcasting query is available at `GET /characters/{id}/spellcasting`, and the summary is also present on the derived sheet. It requires no database change because the result is derived from existing progression state.

The second increment stores shared and Pact Magic slot expenditure in the character's existing progression state. `POST /characters/{id}/spell-slots/spend` spends one slot with a revision check and an audit event. The sheet and spellcasting query now report remaining slots; Short Rest restores Pact Magic and Long Rest restores both pools. Existing progression JSON needs no migration: absent expenditure means no slots spent. Integration coverage checks both pools across a process restart and verifies the HTTP route and stale-write response.

This is a slot ledger, not a cast operation. It does not yet include cantrips, class spell lists, prepared/known spells, spellbooks, Mystic Arcanum, spell components, casting time/action budgets, concentration, targets, saves, attack resolution, or spell effects. A slot can currently be marked spent without naming a spell. No spell can yet be cast through the API. Canonical versioned spell definitions and character-owned selected/prepared spells are needed before a cast route is safe to expose.

Source research: [official SRD 5.2.1 PDF](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf), multiclass spellcasting pp. 23–25; Bard p. 30, Cleric p. 35, Druid p. 40, Paladin p. 52, Ranger p. 57, Sorcerer p. 64, Warlock pp. 70–71, Wizard p. 76; spellcasting rules pp. 103–106; Concentration p. 178. `RULES_SOURCES.md` records this source boundary.

Next implementation slice: add an independently pinned spell catalog for the SRD spell entries and model class-by-class spell preparation and cantrips, including each class's replacement timing and multiclass eligibility. Then implement one auditable cast vertical slice covering validation, action/slot use, event persistence and a small set of structured spell effects before expanding to all spell rules.
