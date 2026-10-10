# MCP tool catalog

All 30 tools are discovered through the official SDK. Each requires a valid bearer token and `campaignId`; the server checks persisted membership. `characterId` and `combatantId` identify a target, never an authority. Commands additionally require a stable `operationId` GUID and return typed, structured results or MCP errors. On a conflict, reload state and decide explicitly whether to retry; never replace an operation ID merely to obtain a new random roll.

| Read tool | Access and result |
| --- | --- |
| `get_campaign_summary` | Member; identity, pinned ruleset, time and revision |
| `get_character_sheet`, `get_inventory` | Owned character or DM; sheet and inventory |
| `get_active_encounter` | Member; full DM view or reduced player turn view |
| `get_available_actions` | Owned character or DM; supported actions and current turn resources |
| `get_visible_world` | Member; full DM world or Phase 6 public projection |
| `get_npc`, `get_location`, `get_active_quests` | Member; DM record or player-visible projection |
| `get_character_knowledge` | Owned character or DM; holder beliefs, known truth only |
| `get_recent_events`, `get_relevant_history` | DM full 20-event page; player metadata for an owned character only |
| `get_dm_context` | Member; role-filtered, ordered and clipped campaign context |
| `get_pending_consequences` | DM; eligible unresolved source events and proposals |

| Mechanical command | Preconditions and effect |
| --- | --- |
| `make_skill_check`, `make_saving_throw` | Owned character or DM, supported check; engine rolls and audits |
| `roll_dice` | Member, supported dice expression; standalone persisted roll result, no game effect |
| `start_encounter` | DM, existing prepared encounter after initiative; starts turn order |
| `perform_attack` | Owned actor or DM; a declared defender reaction also requires control of the defender; engine validates turn, weapon, targets and action economy |
| `cast_spell` | Owned actor or DM; a declared defender reaction also requires control of the defender; prepared supported spell, components, resources and revision |
| `use_item` | Owned actor or DM; supported combat consumable and revision |
| `end_turn` | Owned actor or DM; advances authoritative turn |
| `complete_encounter` | DM, valid outcome and current revision; records completion and rewards state |

| Narrative command | Preconditions and effect |
| --- | --- |
| `apply_world_changes` | DM; 1–50 typed Phase 6 changes, cause and expected world revision |
| `update_npc`, `update_relationship`, `grant_knowledge`, `update_quest` | DM; one validated Phase 6 change via the same world command boundary |
| `propose_narrative_consequence` | DM; creates or revises an unresolved typed proposal for one eligible mechanical source event without applying it; use a fresh operation ID and world revision when revising |
| `record_narrative_consequence` | DM; applies or dismisses the exact proposal identified by its `reviewToken` exactly once; a changed proposal requires fresh review |

The engine does not expose arbitrary damage, HP edits, SQL, filesystem access, custom scripts or unsupported rules as MCP shortcuts. Combat spatial facts such as distance and visibility are caller declarations subject to engine validation; the prototype has no map sensor that can independently prove them. Tool descriptions and schemas are generated from the typed C# methods in `CampaignMcpTools`.
