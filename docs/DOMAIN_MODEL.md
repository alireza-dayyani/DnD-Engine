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

HP-derived unconsciousness remains distinct from applied Unconscious conditions. Phase 2 combines them through `ConditionEffects`: healing above zero removes only HP-derived unconsciousness; independent sources persist. Prone remains after waking and standing costs movement. Health snapshots remain the single HP authority.

## Phase 2 additions

| Concept | Representation and invariant |
|---|---|
| CombatEncounter | Created → Initiative → Active → Completed; ID/campaign, members, explicit order, round, index, global turn number, revision |
| CombatantState | Local combatant ID references a character; PC/monster category, zero-HP policy, surprise, optional identical-monster initiative group, roll, resources |
| D20Roll | All dice, selected die, modifier, total, effective advantage; injected roller |
| TurnResources | Action used, remaining attacks in that action, bonus/reaction used, movement spent, Dash/Dodge/Disengage, weapon-use tracking |
| CombatCapabilities | Imported speed, weapon proficiency IDs, damage/condition defenses, attacks per action, initiative modifiers; no entitlement calculation |
| WeaponDefinition | Stable sourced ID, category/kind, dice or fixed damage, damage type, properties, mastery, ranges, ammunition and mounted handling |
| OwnedWeapon | Character-owned instance ID references definition; ammunition count and availability independent of canonical content |
| ActiveCondition | Unique instance, kind, provenance, optional source character and explicit turn-boundary expiry; normal effects nonstacking, Exhaustion level-counted |
| ConditionEffects | Small derived C# facets combining active conditions and health for rules consumers |
| DamageResolution | Raw/flat-adjusted damage, type, immunity/resistance/vulnerability, rounded intermediate and final damage |
| WeaponAttackResult | Participants, weapon, ability/proficiency/condition modifiers, dice/AC/outcome, critical, typed damage, HP delta, resources, original options and source |

No separate Round, Turn, Attack or Damage database entities are needed: current encounter state and structured timeline payloads carry these facts. Walking/crawling and difficult terrain spend a budget; there are no coordinates. Imported attacks per action (1–10), speed (0–1,000 feet), ammunition (0–100,000), and up to 100 encounter members are application limits.

## Investigated, intentionally deferred

| Concept | Intended boundary and modeling approach |
|---|---|
| CharacterClass / CharacterLevel allocations | Versioned class definitions plus character class-level allocations; total level drives proficiency. Avoid subclass/multiclass builder until needed |
| Resource | Character-owned bounded current/max pools with recovery policy; spell slots are a specialized resource, not arbitrary flags |
| General effects / durations | Extend implemented condition instances with specific spell/feature triggers and world time, not arbitrary scripts |
| Item / Armor | Extend the implemented weapon definition/ownership pattern; armor and equipped AC derivation remain deferred |
| Full inventory | Add quantities, location, carried/held/equipped state and item recovery beyond current owned weapon availability |
| Spell / SpellSlot | Versioned spell definition and reusable effects; slots tracked on the character; no universal scripting language |
| NPC | Canonical definition/reference separated from mutable campaign NPC state; no proprietary lore ingestion |
| Relationship | Directed campaign participant IDs, state and source events; narrative interpretation remains external |
| Quest / QuestObjective | Campaign-owned progress and explicit transitions; not rules catalog content |
| WorldFlag | Namespaced typed campaign fact with mutation event; avoid arbitrary SQL or overwrite-only memory |
| Lore knowledge | Separate retrieval interface with provenance and permissions; never confused with events that actually occurred |

None of these deferred concepts has an empty placeholder table. Their introduction should follow a tested vertical use case.
