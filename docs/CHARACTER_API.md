# Character API (Phase 3)

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

`POST /characters/{id}/spell-slots/spend` records one slot expenditure using `{ "pool": "Shared", "spellLevel": 1, "expectedRevision": 0 }` or pool `PactMagic` at the Warlock's current Pact slot level. The character must have a remaining slot in that pool and level. The command increments the character revision and appends a `SpellSlotSpent` event; a stale revision returns 409. A Short Rest restores Pact Magic slots, while a Long Rest restores both pools. This endpoint records slot use only; it does not cast or validate a spell.

`GET /spells` lists the current pack (version 2): Cure Wounds and Healing Word. Pass `?packVersion=1` to inspect the original pack used by older characters. On `POST /srd-characters`, optional `preparedSpellIds` selects either or both for the starting class if they are on its spell list. Cure Wounds belongs to Bard, Cleric, Druid, Paladin and Ranger; Healing Word belongs to Bard, Cleric and Druid. New sheets report `spellPackVersion: "2"`; existing characters without that state field stay on the original Cure Wounds-only pack (version 1). `POST /characters/{id}/spells/cast-self` accepts `{ "classId": "cleric", "spellId": "healing-word", "pool": "Shared", "spellLevel": 1, "expectedRevision": 0, "componentsAvailable": true }`. It requires a prepared spell from that character's pinned pack, matching class, available spell components and slot, a conscious caster and trained worn armor. It casts only on the caster, outside an unfinished encounter. Cure Wounds heals 2d8 per slot level plus the class's casting modifier; Healing Word heals 2d4 per slot level plus that modifier. Healing, slot use and one `SpellCast` event commit together. `componentsAvailable` is a caller assertion because speech and held-item state are not modeled yet.

To replace a currently prepared spell when completing a Long Rest, include `spellReplacements: [{ "classId": "cleric", "fromSpellId": "cure-wounds", "toSpellId": "healing-word" }]` in the usual rest body with `expectedRevision`. Clerics and Druids may replace any number; Paladins and Rangers may replace one. Other classes cannot replace spells at a Long Rest through this API. A Bard, Sorcerer, or Warlock may instead include a single `spellReplacement` object with that shape when gaining a level in that same class through `/characters/{id}/level-up`. The replacement must come from that class's existing prepared list and target an eligible spell in the character's pinned pack; duplicate preparations are rejected. Existing choices for other classes remain unchanged.

When a class gains a level, `POST /characters/{id}/level-up` also accepts `additionalPreparedSpellIds: ["healing-word"]`. These spells are assigned to the class named by `classId`; each must be in the character's pinned pack, on that class's spell list, and of a level the individual class can prepare. The class's SRD prepared-spell maximum bounds the resulting list. `spellcasting.classes` on the sheet reports `preparedCount` and `preparedMaximum` for each class. The current pack has only two spells, so a class can legitimately have fewer prepared spells than its SRD allowance. Wizard additions are withheld until spellbooks are modeled.

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
