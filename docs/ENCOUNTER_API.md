# Phase 5 encounter API

All new `POST` routes below require `X-Operation-Id: <nonempty GUID>`. Keep the same method, URL, query and exact JSON bytes when retrying after a timeout or restart. The original successful status/body is replayed; reusing a GUID for different bytes returns 409. A failed command may be corrected and retried with the same GUID because its claim is rolled back. Older mutation routes accept the header but do not require it, preserving existing clients. Send a fresh GUID for each new command.

The API uses string enums and camel-case JSON. Invalid rules/input return 400, unknown IDs 404, and stale revisions or conflicting operation IDs 409. Use the revision from the latest GET or command result. An encounter mutation advances the encounter and every participating character revision; inventory mutations outside combat advance owner revisions.

| Method | Route | Request / response |
|---|---|---|
| GET | `/campaigns/{id}/monster-definitions?packVersion=1` | Pinned monster pack, with supported/deferred actions and spells |
| POST | `/monsters` | `{campaignId,definitionId,packVersion?,name?,ammunition?}` → monster instance, character and combat profile |
| GET | `/monsters/{id}` | Pinned definition plus current HP, limited uses and profile |
| POST | `/combat/{id}/monsters` | `{monsterId,surprised?,initiativeGroup?}` → combatant |
| GET | `/combat/{id}` | Existing encounter/character/profile view |
| POST | `/combat/{id}/monsters/spells/cast` | `{combatantId,spellId,targets,verbalAvailable,somaticAvailable,materialAvailable,expectedRevision}` |
| POST | `/combat/{id}/items/use` | `{combatantId,itemId,targetCombatantId,distanceFeet,expectedRevision}` → rolls, healing and turn resources |
| POST | `/combat/{id}/complete` | `{outcome,expectedRevision}`; outcome is `Victory`, `Defeat`, `Retreat` or `Other` |
| GET | `/combat/{id}/rewards` | Outcome, printed-XP pool, existing awards and remaining gear on defeated monsters |
| POST | `/combat/{id}/rewards/experience` | `{awards:[{characterId,amount}],expectedRevision}` → revised award ledger |
| POST | `/combat/{id}/rewards/loot` | `{monsterId,recipientId,itemId,quantity,expectedMonsterRevision,expectedRecipientRevision}` → source inventory |
| GET | `/campaigns/{id}/item-definitions` | Supplemental pinned item pack, currently Potion of Healing |
| GET | `/characters/{id}/inventory/state` | Item instance IDs, definitions, quantities, equipment flags, copper and owner revision |
| POST | `/characters/{id}/inventory/items` | `{definitionId,quantity,expectedRevision}` → revised inventory |
| POST | `/characters/{id}/inventory/transfer` | `{targetId,itemId,quantity,expectedSourceRevision,expectedTargetRevision}` → source inventory |
| POST | `/characters/{id}/inventory/items/remove` | `{itemId,quantity,expectedRevision}` → revised inventory |
| POST | `/characters/{id}/inventory/items/drop` | `{itemId,quantity,expectedRevision}` → dropped item |
| GET | `/campaigns/{id}/dropped-items` | Recoverable dropped item instances |
| POST | `/characters/{id}/inventory/items/pickup` | `{droppedItemId,expectedRevision}` → revised inventory |
| POST | `/characters/{id}/inventory/equipment` | `{itemId,equipped,expectedRevision}` → revised inventory |
| POST | `/characters/{id}/inventory/currency` | `{deltaCopper,expectedRevision}` → revised inventory |
| POST | `/characters/{id}/inventory/items/use` | `{itemId,targetId,expectedRevision}` → self-healing rolls outside combat |

Example: after a `GET /combat/{id}` yields revision `12`, use one Potion of Healing as a Bonus Action:

```http
POST /combat/{id}/items/use
X-Operation-Id: 4ba2fcae-4f37-4cec-8b7f-31c982d03599
Content-Type: application/json

{"combatantId":"<acting-combatant-guid>","itemId":"<owned-potion-guid>","targetCombatantId":"<target-combatant-guid>","distanceFeet":5,"expectedRevision":12}
```

Combat item use requires the actor's turn, ability to act, an unused Bonus Action, an owned potion and a living target within the supplied 5-foot distance. A caller supplies positions and sight facts; there is no map. Outside combat, a potion can only be drunk by its owner. Inventory transfer and removal reject equipped items until unequipped. Supported stacks merge by definition; split stacks get new instance IDs. Drop/pickup preserves a recoverable campaign item and prevents double pickup. Currency is nonnegative copper; buying/selling is not automated.

`/complete` is deliberately manual: dead monsters do not end combat automatically, and a caller may choose Retreat or Defeat with survivors. Its transaction records the outcome, defeated monster IDs and eligible printed XP. `/rewards/experience` only records validated allocations to participating PCs, at most the eligible pool; it does not grant a level. `/rewards/loot` transfers an actual remaining item from a defeated monster to a participating PC. The older `/combat/{id}/end` remains for Phase 2 compatibility and does not create a Phase 5 reward ledger.

The first monster pack contains Goblin Minion, Skeleton and Priest Acolyte. Their supported attacks use the existing weapon resolver, and Priest Acolyte's single currently modeled Divine Aid Healing Word use uses the existing spell resolver. The use persists when spent; automatic renewal on a later day is deferred. Definitions mark Nimble Escape, radiant attack riders and Radiant Flame as deferred. Other spells, monster reactions and special abilities are not inferred from prose. See [Phase 5 report](PHASE5_REPORT.md) for the exact boundary.
