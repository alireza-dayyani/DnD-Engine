# Character and magic API

All routes are local development endpoints. JSON uses camelCase properties and string enum names. `GET /character-choices` returns the pinned SRD 5.2.1 catalog: species/variants, backgrounds, classes/subclasses, feats, armor/gear and tools. Read it before constructing choice requests. Errors use the existing 400/404/409 conventions. Every successful character mutation increments `revision`; send the latest value as `expectedRevision` on the next command. Reload after a 409. No command is automatically retried after dice are rolled.

Create a campaign through `POST /campaigns`, then create a character:

```http
POST /srd-characters
Content-Type: application/json

{
  "campaignId": "<campaign-guid>",
  "name": "Mira",
  "speciesId": "dwarf",
  "speciesVariantId": null,
  "size": "Medium",
  "backgroundId": "criminal",
  "classId": "fighter",
  "baseAbilities": {"Strength":15,"Dexterity":14,"Constitution":14,"Intelligence":10,"Wisdom":12,"Charisma":8},
  "backgroundBonuses": {"Dexterity":2,"Constitution":1},
  "classSkills": ["athletics","perception"],
  "startingItemIds": ["chain-mail","greatsword"],
  "masteredWeaponIds": ["greatsword"],
  "fightingStyleFeat": "defense"
}
```

`GET /characters/{id}/sheet` returns scores, modifiers, skill/save breakdowns, HP, hit dice, AC and speed provenance, senses, feature sources/deferred markers, resources, inventory, selected weapon masteries, combat capabilities and (for casting classes) a spellcasting summary. `GET /characters/{id}/spellcasting` returns only that summary. It includes class casting abilities/attack bonuses/save DCs, remaining shared spell slots and separate Warlock Pact Magic slots. The old `GET /characters/{id}` remains the resolved Phase 1 projection.

`POST /characters/{id}/spell-slots/spend` records one slot expenditure using `{ "pool": "Shared", "spellLevel": 1, "expectedRevision": 0 }` or pool `PactMagic` at the Warlock's current Pact slot level. The character must have a remaining slot in that pool and level. The command increments the character revision and appends a `SpellSlotSpent` event; a stale revision returns 409. A Short Rest restores Pact Magic slots and may use Arcane Recovery; a Long Rest restores both pools and removes created slots. This endpoint records slot use only; it does not cast or validate a spell. Font of Magic conversion and creation have separate routes described in the Magic API.

`GET /spells` lists current immutable pack 4, with ten executable spells; `?packVersion=1`, `=2` and `=3` inspect older pinned packs. New sheets report `spellPackVersion: "4"`; older characters retain their original pin until a revision-checked `POST /characters/{id}/spell-pack/adopt`. Creation accepts `preparedSpellIds`, `knownCantripIds`, and a Wizard's `wizardSpellbookIds`. The self-cast route retains out-of-combat Cure Wounds and Healing Word behavior; combat casting uses `/combat/{id}/spells/cast`. See [Magic API](MAGIC_API.md) for choices, casts and limits.

To replace a currently prepared spell when completing a Long Rest, include `spellReplacements: [{ "classId": "cleric", "fromSpellId": "cure-wounds", "toSpellId": "healing-word" }]` in the usual rest body with `expectedRevision`. Clerics, Druids and Wizards may replace any number (Wizards only from their spellbook); Paladins and Rangers may replace one. A Bard, Sorcerer or Warlock may include one `spellReplacement` when gaining a level in that class. `cantripReplacement` follows the class's level-up timing, except Wizards may replace one on Long Rest.

When a class gains a level, `POST /characters/{id}/level-up` accepts `additionalPreparedSpellIds`, `additionalCantripIds`, and class-specific choices detailed in the Magic API. Choices belong to the class named by `classId`, use that class's own spell-level access and allowance, and must exist in the character's pinned pack. `spellcasting.classes` reports both prepared and cantrip counts/maxima. The ten-spell pack cannot fill every class allowance.

`POST /characters/{id}/level-up` accepts `{ "classId":"fighter", "expectedRevision":0, "hpMethod":"Fixed" }`. `hpMethod` may be `Fixed` or `Roll`; a roll uses the injected die and is not retried on a write conflict. At the class level that grants an improvement, include `featId` and (if required) `abilityIncreases`; e.g. `{"classId":"fighter","expectedRevision":4,"featId":"ability-score-improvement","abilityIncreases":{"Strength":2}}`. A first class level 3 requires `subclassId`. A new class validates the multiclass prerequisites and may require `multiclassSkill` or `multiclassTool`; no second class level-1 HP maximum is granted. A newly acquired Fighting Style requires `fightingStyleFeat`. `featProficiencies` resolves a Skilled feat choice.

Inventory routes use the item instance GUID returned on the sheet:

| Route | Body |
|---|---|
| `POST /characters/{id}/inventory` | `{ "definitionId":"shield", "expectedRevision":n }` |
| `POST /characters/{id}/inventory/equip` | `{ "itemId":"<guid>", "expectedRevision":n }` |
| `POST /characters/{id}/inventory/unequip` | Same item/revision shape |
| `POST /characters/{id}/inventory/remove` | Same item/revision shape |
| `POST /characters/{id}/resources/spend` | `{ "resourceId":"second-wind", "amount":1, "expectedRevision":n }` |
| `POST /characters/{id}/rests/short` | `{ "hitDieSides":[10], "expectedRevision":n }` |
| `POST /characters/{id}/rests/long` | `{ "expectedRevision":n, "masteredWeaponIds":["greatsword"] }` |

The optional Long-Rest `masteredWeaponIds` changes eligible selections when that rest completes. Equipment acquisition after creation accepts a canonical item; starting items during creation must belong to the selected background/class starter lists. Armor can be worn without training: the sheet reports the penalty, Strength/Dexterity D20 tests have disadvantage, and an untrained shield gives no AC bonus. The self-cast route rejects a caster wearing untrained armor.

Creation and level-up require the listed mechanical choices but do not resolve all SRD class/feature choices. `Deferred` feature markers identify rules that have no complete execution yet. A Long Rest call asserts that eight hours have elapsed; the service checks the interval since the previous completed rest. A Short Rest call likewise asserts its duration. Neither endpoint advances a world clock. Legacy imported characters have no choice state and cannot use these Phase 3/4 operations; continue using their original Phase 1/2 endpoints or create a new SRD character.
