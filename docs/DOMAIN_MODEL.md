# Domain model and expansion decisions

## Implemented

| Concept | Representation and invariant |
|---|---|
| Ruleset | `(Id, Version)`; currently only `dnd-5.5 / 5.2.1` executes |
| Campaign | ID, validated name, immutable rules pin |
| Character | Identity/campaign, name, level, six scores, proficiency sets, AC, health, revision; controlled state changes |
| AbilityScore | Immutable 1–30, floor modifier; normal imported PCs restricted to 1–20 |
| CharacterLevel | Immutable 1–20; derived PC proficiency bonus |
| Skill | Canonical versioned definition with stable ID, name, usual ability and source |
| Proficiency | Distinct character skill IDs and saving-throw abilities; no duplicated bonus |
| SavingThrow | Use case and result, not a persistent entity; automatic/voluntary failures explicit |
| HitPoints | State snapshot plus behavior; current 0..max, separate temporary buffer, death/stability counters |
| ArmorClass | Positive imported resolved integer, defaults to unarmored formula; equipment calculations deferred |
| DiceExpression | Count, sides, modifier; supports d4/6/8/10/12/20/100; one homogeneous pool plus modifier |
| CheckResult | Character, kind, optional skill, ability, raw dice, selected die, all modifier components, total/DC/outcome, effective advantage and auto-outcome |
| HealthChange | Requested amount, before/after health, HP lost/regained, temporary absorption, excess damage, optional death roll |
| CampaignEvent | Ordered structured audit entry beside current state; not event-sourced aggregate |

Bounds such as 1,000 dice, HP/damage 1,000,000, AC 1–1,000, DC 0–1,000,000, and names up to 200 characters are **application validation limits**, not claimed SRD limits. Negative damage/healing is rejected; zero is a valid no-op and still audited. Dice may total below zero with a negative modifier; the dice parser does not decide damage semantics. Duplicate/missing proficiencies and malformed expressions fail explicitly.

HP-derived unconsciousness is not a general condition store. Prone persists when healing wakes a character; clearing it, standing, and unrelated sources of Unconscious need the later action/condition layer. Current health snapshots cannot represent a fully built class's feature interactions, resistances, or timed effects.

## Investigated, intentionally deferred

| Concept | Intended boundary and modeling approach |
|---|---|
| CharacterClass / CharacterLevel allocations | Versioned class definitions plus character class-level allocations; total level drives proficiency. Avoid subclass/multiclass builder until needed |
| Resource | Character-owned bounded current/max pools with recovery policy; spell slots are a specialized resource, not arbitrary flags |
| Condition / ActiveCondition | Versioned definition plus source-bearing campaign application, duration, expiration and stacking rules; reusable C# effects |
| Item / Weapon / Armor | Canonical structured definitions, damage dice/type/properties or AC calculation; never switch on display name |
| Inventory | Character/campaign-owned item instances referencing definitions, quantity and instance overrides; no canonical mutation |
| Spell / SpellSlot | Versioned spell definition and reusable effects; slots tracked on the character; no universal scripting language |
| NPC | Canonical definition/reference separated from mutable campaign NPC state; no proprietary lore ingestion |
| Relationship | Directed campaign participant IDs, state and source events; narrative interpretation remains external |
| Quest / QuestObjective | Campaign-owned progress and explicit transitions; not rules catalog content |
| WorldFlag | Namespaced typed campaign fact with mutation event; avoid arbitrary SQL or overwrite-only memory |
| CombatEncounter / Combatant / Initiative | Campaign aggregate, participant references, initiative entries and explicit tie/order decisions; one authoritative turn state |
| Lore knowledge | Separate retrieval interface with provenance and permissions; never confused with events that actually occurred |

None of these deferred concepts has an empty placeholder table. Their introduction should follow a tested vertical use case.
