# Combat API and supported boundaries

Phase 1 campaign and sheet creation stay unchanged. All IDs are UUIDs; enum values are strings. See `scripts/CombatSmoke.ps1` for a complete executable flow. The deterministic HTTP integration scenario in `CombatIntegrationTests` creates Vaelaris and two goblins, resolves a critical kill and counterattack, advances to round 2, restarts the API, compares full state/events, continues, and ends combat.

## Routes

| Method | Route | Body / result |
|---|---|---|
| GET | `/campaigns/{id}/combat-content` | Pinned weapons, conditions and mastery definitions |
| PUT / GET | `/characters/{id}/combat-profile` | Import resolved capabilities / read profile |
| POST | `/characters/{id}/weapons` | `{ "definitionId":"longsword", "ammunition":0 }`; returns owned instance |
| POST | `/campaigns/{id}/combat` | `{ "name":"Bridge ambush" }`; returns Created encounter |
| POST | `/combat/{id}/combatants` | CharacterId, Kind, ZeroHpPolicy, Surprised, InitiativeGroup |
| GET | `/combat/{id}` | Encounter, current combatant, ties, current sheets and profiles |
| POST | `/combat/{id}/initiative` | `{}` or optional `visibleFearSources` dictionary keyed by combatant ID, values source character IDs |
| POST | `/combat/{id}/start` | `{}` if no ties; otherwise `{ "order":["combatant-id", "..."] }` |
| POST | `/combat/{id}/move` | CombatantId, Distance, Mode (Walk/Crawl), DifficultTerrain, ApproachesFear |
| POST | `/combat/{id}/stand` | `{ "combatantId":"..." }`; result is movement cost |
| POST | `/combat/{id}/action` | CombatantId, Action (Dash/Disengage/Dodge) |
| POST | `/combat/{id}/attack` | CombatantId and nested Attack; example below |
| POST | `/combat/{id}/saving-throws` | CombatantId and nested Check (Ability, Dc, optional advantage/disadvantage/modifier/voluntary failure) |
| POST | `/combat/{id}/conditions` | CombatantId, Kind, Source, optional SourceCharacterId/Expiry/ExpiresOnTurn |
| DELETE | `/combat/{id}/combatants/{combatantId}/conditions/{conditionId}` | Remove one source instance |
| POST | `/combat/{id}/end-turn` | `{ "combatantId":"..." }`; advances and starts next turn atomically |
| POST | `/combat/{id}/end` | `{}`; completes active encounter and releases enrollment |
| DELETE | `/characters/{id}/conditions/{conditionId}` | Remove an applied condition outside an unfinished encounter |

Combat command responses include EncounterId, the **committed** Revision, Round, TurnNumber and Result. Nested encounter snapshots carry that same committed revision. Validation is 400, unknown entities 404, concurrent writes 409. Reload after a conflict; no rolls are retried. Commands lack idempotency keys: inspect state/events before resending after uncertain delivery.

## Import and attack examples

Import capabilities before enrollment; proficiency IDs must exist in the campaign's content pack:

```json
{
  "speed":30,
  "weaponProficiencies":["longsword","shortbow"],
  "resistances":["Fire"],
  "immunities":[],
  "vulnerabilities":[],
  "conditionImmunities":[],
  "attacksPerAction":1,
  "initiativeBonus":0,
  "initiativeAdvantage":false,
  "initiativeDisadvantage":false
}
```

Grant weapons before enrollment. The returned owned ID, not a canonical weapon ID, is used to attack:

```json
{
  "combatantId":"ATTACKER-COMBATANT-ID",
  "attack":{
    "targetId":"TARGET-COMBATANT-ID",
    "weaponId":"OWNED-WEAPON-ID",
    "mode":"Melee",
    "use":"Action",
    "hands":1,
    "context":{
      "distanceFeet":5,
      "attackerCanSeeTarget":true,
      "targetCanSeeAttacker":true,
      "cover":"None"
    }
  }
}
```

Modes are Melee/Ranged/Thrown. Uses are Action/LightBonus/Opportunity. Finesse optionally specifies Strength/Dexterity; otherwise it chooses the higher modifier (Strength on equality). Other weapons enforce their usual ability. Hands, FreeHandToLoad and Mounted declare handling. Two-handed weapons require Hands=2 except the mounted lance exception; versatile dice apply to two-handed melee attacks. Positive ability damage is omitted from Light bonus attacks, and fixed Blowgun damage has no ability modifier. Nonproficient attacks are allowed without PB. Heavy uses ability-score thresholds, not size. Loading is tracked by weapon within the Attack action. Ammunition is spent on hits and misses. Thrown instances become unavailable; recovery/replenishment is not implemented yet.

## Adjudication and lifecycle

- At least two combatants; one unfinished encounter per character. Profiles/equipment must exist before enrolling. PC members require DeathSaves; Monster members explicitly choose Die or DeathSaves. These are imported mechanical sheets, not a monster/CR builder.
- Identical-monster InitiativeGroup is a GM assertion, with matching initiative modifiers/circumstances checked by the engine. One roll is shared. Surprise gives initiative Disadvantage and does not remove a turn. Tied PCs choose their order; monster/mixed ties are GM decisions. The API labels the required decision maker and validates the supplied order; it has no role authentication.
- Start begins round 1 / global turn 1. End-turn advances, wraps rounds, resets the incoming combatant's action/bonus/reaction/movement, expires applicable conditions, and rolls a death save if dying. Stable/dead combatants do not roll. Dead slots remain in order and cannot act; callers still end their turn. No separate arbitrary round/start-turn command exists.
- Action attacks share the imported AttacksPerAction budget. Dash/Dodge/Disengage consume the action; Light permits one bonus attack with a different Light weapon after an Attack-action Light attack. Reactions reset at the start of the owner's next turn, not the next round. Other bonus actions/reactions need future defined features.
- Movement uses current Speed, Dash allowance, and spent distance. Crawl and difficult terrain each add one foot of cost per foot; both cost three. Standing costs half current Speed rounded down, cannot happen at Speed zero, and removes Prone instances/health flag. Stunned/Incapacitated alone do not force Speed zero. Walking/crawling only; no climbing/swimming/flying/pathfinding.
- Spatial facts are caller adjudications: distance, cover, whether creatures can see one another after senses/invisibility, wrong guessed target location, nearby qualifying ranged threat, visible fear sources, and approach toward fear. The engine does not detect them geometrically. Blinded still imposes its specified attack penalties; special feature exceptions beyond supplied visibility are unsupported.
- OpportunityProvoked asserts a creature you can see is leaving reach using its movement/action/bonus/reaction, not teleportation or involuntary movement without those resources. Request before that move. Engine enforces melee reach, sight, no Disengage, ability to react and unused reaction. No interrupt stack or movement rollback exists.
- Applied conditions are explicit GM operations, not spell/feature casting or eligibility checks; they spend no action. Manual sources persist until removed. Timed sources use Expiry=TurnStart/TurnEnd and a strictly future global ExpiresOnTurn. On that boundary they expire everywhere in the encounter. Unexpired effects remain after combat and require explicit removal; ending combat does not imply elapsed time or a rest.

## Condition coverage

All 15 definitions are imported. Implemented facets include attack advantage/disadvantage and source restrictions, Speed/action inhibition, prone/standing, exhaustion penalties/death, initiative effects, automatic Strength/Dexterity failures, restrained Dexterity save disadvantage, helpless close-hit criticals, and Petrified damage resistance/Poisoned immunity. Duplicate ordinary sources do not multiply the same effect; Exhaustion instances are levels. Removing one source leaves others. An incapacitated/dead grappler's applied Grappled sources are removed during combat reconciliation. Dodge is lost on incapacitation or Speed zero.

Sight/hearing-dependent ability-check failure, charmer social advantage, speech, concentration, transformed mass, dropping held objects and equipment locations are definition metadata for future consumers, not automated subsystems. General skill checks still use the Phase 1 endpoint without the condition layer. Grappling initiation, escape, dragging and out-of-reach termination require external adjudication; explicit removal handles termination. Long-rest Exhaustion recovery is deferred. Healing HP-zero unconsciousness does not remove independently applied Unconscious and does not stand the character up.

All eight mastery definitions are stored with Automated=false. Neither possession of a weapon nor imported proficiency grants mastery. No mastery effect is executed. Spell/class/feat/species entitlement, AC from armor, damage riders, feature reactions, maximum-HP changes, revised knockout and full rests are not implemented.

The Phase 1 checks, `/damage`, `/heal`, temporary HP and standalone death-save endpoints retain their original semantics. They are trusted standalone adjudication primitives, not substitutes for combat commands; they do not consume turn resources, apply monster policy or run all combat condition hooks. In particular, use automatic turn-start saves during combat to avoid manually rolling an extra save. Their revision checks still prevent lost updates against combat writes.
