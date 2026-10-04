# Phase 3 character system and progression report

## Delivered

The engine now has a choice-based SRD 5.2.1 character path. `rules.db` holds an immutable hash-checked character pack; `campaign.db` holds chosen species, background, class levels, feats, scores, hit dice, resources, inventory and mastery selections. A domain deriver projects a rich sheet and a Phase 1/2-compatible character/combat profile. This keeps canonical rules separate from individual state and explains where passive values come from. Existing imported characters and campaigns remain intact.

The adapted catalog indexes all 9 SRD species, 4 revised backgrounds, 12 classes with one SRD subclass each, Origin/General/Fighting Style/Epic Boon feat entries, 13 mundane armor/shield types, tool IDs, starter item references, and the existing 38 Phase 2 weapons. The class tables have level 1–20 feature markers; the implemented effects include selected training, HP, AC, speed, resistance, darkvision, attack count, initiative, resource maximum/recovery and weapon-mastery slots. Unimplemented active rules are marked deferred on the sheet.

Creation checks size/variant, background ability allocation, class skills/tools, human extra Origin feat, starting Fighting Style, starter-item eligibility and weapon mastery eligibility. Level-up checks class level/total level 20, multiclass primary scores and additional training, HP method, subclass at class level 3, applicable feat/ASI choices, ability caps and resource growth. Short Rest spends typed Hit Dice sequentially and recovers supported feature resources; Long Rest restores HP/dice/resources, expires temporary HP, reduces one Exhaustion condition and can change eligible mastery selections. Inventory equip/unequip drives armor AC and speed; untrained armor imposes the revised D20 penalty, and trained shield AC applies only when appropriate.

New HTTP routes and request shapes are in `CHARACTER_API.md`. Both databases have additive third migrations. Progression commands use character revisions and one transaction for character, progression, combat profile and one event. Failed choices and stale revisions do not leave partial levels; active combat membership blocks progression mutation. Creation, level-up, resource/item/rest events extend the campaign timeline without altering older records.

## Research and verification

Source: [official SRD 5.2.1 PDF](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf), especially character creation/multiclass pp. 20–25; classes pp. 27–77; backgrounds pp. 80–81; species pp. 82–85; feats pp. 86–88; equipment pp. 89–96; rests pp. 184/186. `RULES_SOURCES.md` maps the supported mechanics and interpretations.

Deterministic integration coverage includes a 36-combination species/background creation matrix, all 12 classes at level 1, Fighter 1→20 advancement, persistence and API restart, Fighter/Rogue multiclass, resources and typed Hit Dice, armor/Stealth/untrained penalties, mastery change eligibility, stale revision and atomic failure.

On 2026-10-05, `dotnet restore`, `dotnet build --no-restore`, and `dotnet test --no-build` passed (141 Domain, 10 Application, 25 Integration tests). Phase 1, Phase 2 combat and Phase 3 character HTTP/process-restart smokes passed; evidence is under `artifacts/smoke-20261005-010321/`, `artifacts/combat-smoke-20261005-011527/`, and `artifacts/character-smoke-20261005-011651/`. The default API was also started against the existing repository databases and returned a ready health response plus 9 species/12 classes; all 17 pre-existing character rows remained and no progression rows were fabricated. A full backup of the provisional rules DB was saved as `artifacts/phase3-provisional-rules-backup.db` before refreshing its single provisional character-content row.

## Deliberate boundaries and deviations

- Full spellcasting, spell slots and multiclass casting are Phase 4. Class/feat/species spell benefits remain markers, not usable spells.
- Most active class, subclass, species and feat powers are cataloged but not executable. The catalog provides one subclass per class, with a placeholder deferred feature rather than full subclass progression. Expertise and other required noncombat feature choices are not yet resolved. Do not treat a feature marker as proof of full rules execution.
- Starter packages are eligibility lists, not the SRD's exact mutually exclusive packages, quantities or gold alternative. General inventory lacks quantities, currency, encumbrance, held/dropped state and full Phase 2 ammunition synchronization. The older Phase 2 capability-import and weapon-grant commands now reject choice-based characters so they cannot bypass eligibility or create weapons outside their general inventory.
- Weapon mastery selection is validated, but the Phase 2 mastery properties are still definition-only; attack effects are not executed. Mastery changes are supported at Long Rest completion.
- Rest calls assert that the rest duration occurred. The service stores completion time and uses a 24-hour completion-to-completion Long-Rest guard; interruption, travel/activity limits and world-time scheduling are not modeled. Supported resources recover; spell recovery is absent.
- Armor training penalties apply to Strength/Dexterity checks and saves through Phase 1 endpoints and to Phase 2 weapon attacks. The sheet exposes a spellcasting-block flag for the future casting engine. Full item/armor integration with every combat situation is not yet claimed.
- Existing imported characters have no recoverable choice state. Phase 3 operations explicitly reject them instead of inventing species/class/feat history; original operations remain usable. No automatic retroactive migration is attempted.

The next separately approved phase should start with spell definitions, casting, slots and multiclass spell-slot rules, then connect deferred feature choices and effects. Do not add magic by writing unexplained numeric projections into the character row.
