# Rules sources and license

## Phase 2 research (2026-09-29)

Read the same official 5.2.1 PDF for: combat/initiative/ties/surprise/rounds/turns (p.13); actions, bonus actions and reactions (pp.9–10); movement, difficult terrain, unseen targets (p.14); cover/range/close ranged attacks/opportunity attacks (p.15); weapon damage, fixed damage and critical dice (p.16); mitigation order and zero-HP policies (pp.17–18); weapon properties and all eight mastery properties (pp.89–90), all 38 weapons (p.91); all 13 damage types (p.180); all 15 conditions in the glossary (pp.177–191).

Key revised rules: surprise is initiative disadvantage; tied PCs choose their order, GM resolves monster/mixed ties (no invented Dexterity tie breaker). Identical creatures share one initiative roll. Heavy uses Strength 13 for melee weapons or Dexterity 13 for ranged weapons. Light permits one different-Light-weapon bonus attack without a positive ability damage modifier. Loading limits a weapon to one shot per action/bonus/reaction. Criticals double damage dice, not flat bonuses; fixed Blowgun damage gets no ability modifier. Resistance halves and rounds down before vulnerability doubles; immunity prevents damage entirely. Stunned grants Incapacitated but does NOT itself set Speed to zero in this SRD. Grappled imposes attack disadvantage against targets other than the grappler. Exhaustion applies −2 per level to D20 Tests, −5 feet per level to speed, death at six. Invisible's attack benefits do not apply against a creature that can see it; it grants initiative advantage. Petrified grants resistance to all damage and immunity to Poisoned, not blanket Poison damage immunity.

Condition entries researched: Blinded p.177; Charmed p.178; Deafened/Exhaustion p.181; Frightened/Grappled p.182; Incapacitated/Invisible p.184; Paralyzed/Petrified/Poisoned/Prone p.186; Restrained p.187; Stunned p.189; Unconscious p.191. The engine applies in-scope mechanical effects with source-aware instances and explicit spatial facts. Social/lore, concentration/spell state, carrying transformed equipment, and unsupported action types remain outside Phase 2. Mastery content is definition-only; no automatic mastery eligibility or effect is claimed.

Initiative ties require explicit caller-provided order preserving descending totals. The API has no players/GM authentication: it records the order as an adjudication, with tie groups labeled Players or GameMaster. Grouping identical monsters is an explicit GM assertion; differing effective initiative modifiers/advantage states are rejected. Opportunity attacks are requested before the provoking move; movement geometry and whether a creature actually leaves reach are caller facts. Engine checks the supported trigger, melee reach, seeing the target, action inhibition, reaction token, and Disengage.

Checked **2026-09-29**. Engine identity: `dnd-5.5`, SRD version `5.2.1`. The official landing page identifies 5.2.1 as its latest English download (published May 1, 2025); the page itself reports an update on March 2, 2026. No newer release is listed there. The engine does not claim support for future errata automatically.

Official sources read:

- [SRD landing page and release/version FAQ](https://www.dndbeyond.com/srd).
- [Official English SRD 5.2.1 PDF](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf). Relevant sections, not all 364 pages, were read for this slice.
- [CC BY 4.0 deed](https://creativecommons.org/licenses/by/4.0/) and [legal code](https://creativecommons.org/licenses/by/4.0/legalcode).

## Phase 1 source-to-behavior map (preserved standalone endpoints)

Page numbers below are the PDF's printed pages.

| Source | Behavior | Implementation / verification |
|---|---|---|
| pp. 5–6, six abilities/modifiers | Modifier is floor((score−10)/2); global score bounds 1–30 | `AbilityScore`, modifier boundary tests |
| p. 5, normal adventurer bound | Normal PC maximum 20 unless an applicable feature says otherwise | Sheet import enforces 1–20; exceptional features deferred |
| pp. 6–7, D20 tests/checks/saves | d20 + ability + applicable proficiency + circumstances; total at least DC succeeds | `CheckResolver`; normal/equality/failure tests |
| p. 7, saving throws | A creature may choose to fail without rolling | VoluntaryFailure; null total/selected die and no dice consumed |
| p. 7, attack rules | Natural 1/20 automatic outcomes are specified for attacks | They are not imported into ordinary checks/saves |
| pp. 7–8, advantage/disadvantage | Two dice, high/low; same-kind sources do not stack; any of each cancels | Two boolean presence flags; deterministic tests |
| p. 8, proficiency | PC levels 1–4 +2, 5–8 +3, 9–12 +4, 13–16 +5, 17–20 +6; add only once | `CharacterLevel`, skill/save tests |
| pp. 6, 9, skills | Skill proficiency applies to a relevant ability check; usual ability is not mandatory | Versioned 18-skill JSON; optional GM ability override |
| p. 7, AC | Default base AC 10 + Dexterity modifier | Sheet import default; alternate AC treated as pre-resolved sheet input |
| pp. 16–17, HP/healing | Damage loses HP, floor at zero; healing capped at max | Before/after results and boundary tests |
| pp. 17–18, zero HP | Massive remaining damage at least max HP kills; otherwise unconscious; damage at zero adds failures, two on critical | PC health state and regression tests |
| pp. 17–18, death saves | 10+ succeeds, natural 1 adds two failures, natural 20 heals one; three successes stabilize, three failures kill; counters reset on stabilization/healing | Standalone basic death-save use case; Phase 2 schedules saves at turn start |
| p. 18, temporary HP | Buffer first, never stacks, recipient chooses replacement, healing does not restore it or consciousness | Explicit replacement boolean; tests |
| p. 18, temporary HP duration | Expires when depleted or on completing a Long Rest | Depletion supported; full rest/time progression deferred, no claim of automatic rest expiry |
| p. 191, Unconscious | Strength/Dexterity saves automatically fail; unconsciousness confers Prone, which remains after waking | Save auto-outcome and persistent Prone flag; Phase 2 adds independent condition instances |

## Phase 1 endpoint boundaries and interpretation notes

- Only ordinary checks and saves are resolved. No tools, Expertise, fractional proficiency, Heroic Inspiration, rerolls, Reliable Talent, Exhaustion, special save features, or passive checks. These are not silently simulated through older rules. A caller may supply a known flat circumstance modifier; the engine records it but does not verify its feature source.
- Active ability/skill checks while unconscious are rejected as unsupported actions. Other ability saves still roll unless an implemented auto-failure applies.
- At zero HP the SRD says **any damage** incurs death-save failure, while temporary HP is a buffer against losing actual HP. The implementation applies the damage-at-zero clause even if temporary HP absorbs the HP loss; its instant-death threshold uses incoming resolved damage at zero. This reading follows the two clauses literally; no extra HP-loss prerequisite is invented. A dedicated test and this note make the interaction visible for review.
- Critical damage input is already rolled/resolved; the boolean only determines death-save failures at zero. No attack legality or critical determination is inferred.
- Maximum HP is fixed positive imported state in this slice. Effects reducing maximum HP (including death at max HP zero) are unsupported; zero max on creation fails rather than importing a dead character.
- Basic death saves here are unmodified 1d20. Effects that modify a death save, advantage/disadvantage on death saves, stabilization by Help/Medicine, and stable recovery after 1d4 hours are deferred. The explicit endpoint never pretends to advance a combat turn or time.
- Revised knockout rules reduce a melee target to **1 HP** and give Unconscious (p. 17), unlike the older zero-HP knockout pattern. Knockout is not implemented. No old knockout mechanic was added.
- Setting lore, NPC history, and class/spell content were not sourced from proprietary books. No 2014 rule source is used.

## Licensing and attribution

The SRD is CC BY 4.0, not an OGL dependency. Its first page supplies the exact attribution paragraph reproduced in `ATTRIBUTION.md` and README. Preserve that paragraph when distributing SRD-derived material. Include the source and license link; indicate adaptation; do not imply endorsement or apply additional legal/technical restrictions to the licensed material. The license is not a blanket license for trademarks, settings, or excluded books. Original project code has no selected distribution license yet.

Adaptations are explicitly marked: mechanics translated into code, skills into data, prose into partial summaries. Rules rows retain version, source URL, license, and content-pack SHA-256; skill rows retain version and page source. The hash identifies this adapted JSON pack, not the official PDF. The official PDF is linked, not vendored. Future content versions must be added alongside old versions with separately tested behavior; existing campaign pins must not be rewritten.
