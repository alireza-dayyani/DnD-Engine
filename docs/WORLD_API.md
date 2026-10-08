# Narrative world API (Phase 6)

The world API is a local development interface. Its player-safe routes are `GET /campaigns/{id}/world` and the existing `GET /campaigns/{id}/events`. The former returns only discovered locations, public NPC identity/information, public faction identity/status, public facts without provenance, and public discovered quests without rewards/consequences. The latter excludes every narrative event payload. Neither returns knowledge records or private/secret fields.

DM reads and writes live under `/dm/campaigns/{id}/world`. The API rejects requests whose **remote connection address is not loopback**. A role header or body flag is ignored. This is a local-machine trust boundary, not user authentication: anyone able to make a loopback request on the host can act as DM. Do not expose this development API through a remote listener or reverse proxy. A future remote or MCP adapter needs authenticated principals and per-campaign, per-character authorization before exposing DM or knowledge methods.

## Reads

| Route under `/dm/campaigns/{id}/world` | Result |
|---|---|
| `GET /` | Full authoritative `state` plus campaign `gameSeconds` |
| `GET /npcs/{npcId}` | Narrative NPC, including private information and optional mechanical link |
| `GET /locations/{locationId}` | Location and parent/control state |
| `GET /factions/{factionId}` | Faction, memberships referenced through the full state |
| `GET /relationships/{relationshipId}` | Directional relationship dimensions and note |
| `GET /facts` | All world truth, including secret facts |
| `GET /knowledge/{kind}/{holderId}` | NPC, Character or Faction knowledge and possibly false beliefs |
| `GET /quests/active` | Active quests |
| `GET /events?after=0&limit=100` | Narrative-only audit events; maximum 500 per page |

## Mutations

Every new mutation requires `X-Operation-Id: <nonempty GUID>`. A successful response is committed in the same SQLite transaction as world state and audit events. Repeating the same method, path, query and exact body bytes with the same GUID returns the original response, including after restart; changing the request gets 409. Each request supplies `expectedRevision` from the DM or player-safe world view and a nonempty `cause`. A stale revision gets 409. IDs in payloads are caller-generated GUIDs so a batch can reference entities created earlier in the same batch.

| Route | Payload `value` or operation |
|---|---|
| `POST /npcs`, `PUT /npcs/{npcId}` | `NarrativeNpc` |
| `POST /locations`, `PUT /locations/{locationId}` | `WorldLocation` |
| `POST /factions`, `PUT /factions/{factionId}` | `WorldFaction` |
| `POST /memberships` | `FactionMembership` (create or update by ID; `active:false` revokes) |
| `POST /relationships` | `WorldRelationship` (create or update by ID) |
| `POST /facts`, `PUT /facts/{factId}` | `WorldFact` |
| `POST /facts/{factId}/disclose` | Same secret fact with `visibility:"Public"`; emits `SecretDisclosed` |
| `POST /knowledge` | `KnowledgeRecord` (create or update by ID) |
| `POST /quests`, `PUT /quests/{questId}` | `WorldQuest` |
| `POST /changes` | Atomic 1–50 typed world changes |

Single-entity commands have the envelope `{"expectedRevision":0,"cause":"session zero","value":{...}}`. The batch envelope is `{"expectedRevision":0,"cause":"session zero","changes":[{"kind":"CreateLocation","location":{...}},...]}`. A batch validates each change against the state produced by earlier changes and commits once. Route IDs must match body IDs. The response is `{ "state": ..., "gameSeconds": ... }`.

The change kinds are `CreateNpc`, `UpdateNpc`, `CreateLocation`, `UpdateLocation`, `CreateFaction`, `UpdateFaction`, `SetMembership`, `SetRelationship`, `EstablishFact`, `UpdateFact`, `GrantKnowledge`, `DiscloseSecret`, `CreateQuest`, and `UpdateQuest`. Each change carries exactly one matching typed payload. Example:

```json
{
  "expectedRevision": 0,
  "cause": "Session zero",
  "changes": [
    {"kind":"CreateLocation","location":{"id":"11111111-1111-4111-8111-111111111111","name":"Elda","description":"The known world","kind":"World","isDiscovered":true}},
    {"kind":"EstablishFact","fact":{"id":"22222222-2222-4222-8222-222222222222","key":"artifact-holder","value":{"subject":"Vaelaris","predicate":"possesses","object":"the amber relic"},"source":"court wizard","visibility":"Secret"}}
  ]
}
```

Location parentage follows World → Region → Settlement → District → Building → Room, and cycles are rejected. NPCs may exist without combat statistics; a `mechanicalCharacterId` must point to a character in the same campaign. Faction membership is separate from relationship state. A relationship has distinct trust/respect/affection/fear/suspicion/hostility values from −5 to 5, plus a label and note. Knowledge references a world fact but may record a different `beliefValue`; it never changes that fact. Facts marked Secret cannot become Public through `UpdateFact`: use the explicit disclose command. Quest transitions are Unknown → Discovered → Active → Completed/Failed/Abandoned; prerequisites, objective dependencies and cycles are checked. Narrative updates never grant XP, items, or automatic combat consequences.

`gameSeconds` and every narrative event use the Phase 4 campaign clock. World commands do not advance time. The command cause, relevant before/after state, campaign time and world revisions are audited. Current state is authoritative; the event log explains changes and is not replayed to rebuild the world.

Run `scripts/WorldSmoke.ps1` for a complete HTTP demonstration with a secret, divergent knowledge, quest transitions, restart and exact replay.
