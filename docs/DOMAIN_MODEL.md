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
| ArmorClass | Phase 1 accepts a resolved imported integer; Phase 3 derives it for choice-based characters from armor, shield, Dexterity and effects |
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

## Phase 3 additions

| Concept | Representation and invariant |
|---|---|
| CharacterRules | Version-keyed species, background, class, subclass, feat and item definitions in `rules.db`; content hash detects drift |
| ProgressionState | Per-character choices and current state in `campaign.db`; class levels sum to total level; legacy characters have no progression row |
| CharacterDeriver | Recomputes skills, saves, HP projection, armor AC, speed, training, senses, resources, selected masteries and combat capabilities; source breakdowns explain derived values |
| ResourceState | Current/max, recovery policy and feature source; resource spend is bounded and audited |
| HitDiePool | Die sides, total and available; class-specific gains and deterministic short-rest spending |
| InventoryItem | Stable instance ID referencing a canonical item or Phase 2 weapon; equip state never modifies canonical definitions |
| FeatureGrant | Source and `Deferred` marker for each acquired species/background/class/subclass/feat benefit |

## Phase 5 additions

| Concept | Representation and invariant |
|---|---|
| MonsterDefinition / MonsterPack | Immutable, validated SRD monster data in a versioned, hash-checked rules pack; supported attacks/spells identify existing resolvers and deferred mechanics carry reasons |
| MonsterInstance | Campaign creature ID, definition/pack pins, limited-use resources and revision; HP/conditions/weapons use existing character/combat projections |
| InventoryState | Owner's item instances, quantities and copper pieces; synchronized with choice-based progression when present |
| EncounterItemPack | Immutable supplemental item definitions for implemented consumables; Potion of Healing is the first entry |
| DroppedItem | Recoverable campaign-owned item instance or split stack outside an inventory; one pickup removes it atomically |
| EncounterRewardState | Explicit outcome, defeated monster IDs, printed XP pool, award ledger and optimistic revision; XP is recorded, not converted into levels |
| IdempotencyOperation | API operation GUID, exact request fingerprint and original response, committed with the command in campaign SQLite |

## Investigated, intentionally deferred

| Concept | Intended boundary and modeling approach |
|---|---|
| Complete feature/subclass behavior | Phase 3 records the choice and feature marker; future focused behaviors should consume the existing definitions |
| Spell slots | Spell slots and multiclass spellcasting need a specialized Phase 4 model, not arbitrary feature counters |
| General effects / durations | Extend implemented condition instances with specific spell/feature triggers and world time, not arbitrary scripts |
| Complete equipment economy | Phase 5 adds quantities, copper, drop/pickup and one consumable. Exact package branches, prices/purchases, encumbrance and held-hand state remain deferred |
| Spell / SpellSlot | Versioned spell definition and reusable effects; slots tracked on the character; no universal scripting language |
| NPC | Canonical definition/reference separated from mutable campaign NPC state; no proprietary lore ingestion |
| Relationship | Directed campaign participant IDs, state and source events; narrative interpretation remains external |
| Quest / QuestObjective | Campaign-owned progress and explicit transitions; not rules catalog content |
| WorldFlag | Namespaced typed campaign fact with mutation event; avoid arbitrary SQL or overwrite-only memory |
| Lore knowledge | Separate retrieval interface with provenance and permissions; never confused with events that actually occurred |

None of these deferred concepts has an empty placeholder table. Their introduction should follow a tested vertical use case.
