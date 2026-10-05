# Magic API: Phase 4 bounded engine

All commands use camelCase JSON and string enum values. Read `GET /spells` for the current immutable spell pack or `GET /spells?packVersion=1` / `=2` for older packs. New characters pin pack 3; old characters retain their pin. `POST /characters/{id}/spell-pack/adopt` accepts `{"packVersion":"3","expectedRevision":n,"knownCantrips":[{"classId":"cleric","spellId":"sacred-flame"}]}`. Adoption is explicit, checks all existing choices against the new pack, appends an audit event, and cannot run while the character is enrolled in an unfinished encounter.

Pack 3 contains Cure Wounds, Healing Word, Fire Bolt, Sacred Flame, Burning Hands, Blur and Circle of Death. The pack is intentionally small. A selected spell must belong to the character's class list unless a modeled class feature grants it. Current feature grant: a level 3+ Fiend Warlock always has Burning Hands prepared without counting against chosen preparation. Unsupported spell effects cannot be cast.

`POST /srd-characters` accepts `preparedSpellIds`, `knownCantripIds`, and for Wizards `wizardSpellbookIds`. Wizard preparation must be a subset of the book. The pack has fewer Wizard spells than the SRD starting book allowance, so a Wizard can begin with a smaller modeled book. `POST /characters/{id}/level-up` accepts `additionalPreparedSpellIds`, `additionalCantripIds`, `cantripReplacement`, `additionalWizardSpellbookIds`, `additionalMetamagicOptions`, and `mysticArcanumSpellId` at the applicable class level. A Wizard may add at most two spells to the book on an existing Wizard level gain, or up to six on first acquiring the class. A Wizard can replace prepared book spells on Long Rest. Sorcerers can choose QuickenedSpell and SubtleSpell at their level 2 Metamagic choice; only those two are executable in this pack. Warlocks may choose Circle of Death as their level 11 Mystic Arcanum. Long Rest resets Sorcery Points and expended Mystic Arcanum uses. The sheet shows known cantrips, spellbook, always-prepared grants, Metamagic options, points, and Arcanum choices/uses.

`POST /characters/{id}/spells/cast-self` is the existing out-of-combat self-healing route for Cure Wounds and Healing Word. It requires a prepared spell, slot, components and the latest character revision. It rejects characters enrolled in an unfinished encounter. `POST /characters/{id}/spell-slots/spend` remains an audited manual slot expenditure and does not cast a spell.

During an active encounter, cast through `POST /combat/{id}/spells/cast`. Supply the latest encounter `expectedRevision`, the acting combatant's ID, its casting `classId`, a known/prepared/granted `spellId`, `spellLevel`, `pool` (`Shared`, `PactMagic`, or `null` for cantrips and Mystic Arcanum), `targets`, and explicit component availability. A spell attack or save rolls inside the command. The result contains each target's roll, damage/healing and HP change. One commit covers the encounter, all character HP, progression expenditure and audit events.

```json
{
  "combatantId":"CASTER-COMBATANT-ID",
  "classId":"cleric",
  "spellId":"sacred-flame",
  "pool":null,
  "spellLevel":0,
  "targets":[{
    "combatantId":"TARGET-COMBATANT-ID",
    "distanceFeet":30,
    "casterCanSeeTarget":true,
    "targetCanSeeCaster":true,
    "cover":"Half"
  }],
  "verbalAvailable":true,
  "somaticAvailable":true,
  "materialAvailable":false,
  "expectedRevision":12
}
```

For Burning Hands, include each creature in the asserted 15-foot cone as a target with its distance from the caster. For Circle of Death, supply `areaCenterDistanceFeet` (at most 150) and each target's `distanceFromAreaCenterFeet` (at most 60); a component worth 500 GP must be asserted available. The engine validates the provided numbers but has no map to discover omitted targets or verify positions. `ignoreBlur` asserts Blindsight/Truesight for an attacker where applicable. Quickened or Subtle Spell may be requested as `"metamagic":"QuickenedSpell"` or `"SubtleSpell"` for a Sorcerer spell; the Sorcery Point cost commits with the cast. Quickened changes an Action cast to a Bonus Action. Subtle waives ordinary V/S and no-cost M components, but not a priced component. Only one spell slot can be spent to cast spells on a turn; a Quickened spell also prevents a later level 1+ cast that turn.

Blur is a concentrating self spell lasting up to ten rounds. Its effect is persisted in the encounter, causes attack disadvantage unless the attacker asserts a qualifying special sense, and ends on expiry, incapacity, or a failed Constitution save after damage. The engine records concentration checks and endings as campaign events. As with the combat API's weapon attacks, spatial and visibility facts come from the caller; there is no geometric or inventory adjudicator.
