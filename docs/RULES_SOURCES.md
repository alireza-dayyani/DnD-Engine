# Rules sources and license

Checked **2026-09-29**. Engine identity: `dnd-5.5`, SRD version `5.2.1`. The official landing page identifies 5.2.1 as its latest English download (published May 1, 2025); the page itself reports an update on March 2, 2026. No newer release is listed there. The engine does not claim support for future errata automatically.

Official sources read:

- [SRD landing page and release/version FAQ](https://www.dndbeyond.com/srd).
- [Official English SRD 5.2.1 PDF](https://media.dndbeyond.com/compendium-images/srd/5.2/SRD_CC_v5.2.1.pdf). Relevant sections, not all 364 pages, were read for this slice.
- [CC BY 4.0 deed](https://creativecommons.org/licenses/by/4.0/) and [legal code](https://creativecommons.org/licenses/by/4.0/legalcode).

## Source-to-behavior map

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
| pp. 17–18, death saves | 10+ succeeds, natural 1 adds two failures, natural 20 heals one; three successes stabilize, three failures kill; counters reset on stabilization/healing | Explicit basic death-save use case; no turn scheduling |
| p. 18, temporary HP | Buffer first, never stacks, recipient chooses replacement, healing does not restore it or consciousness | Explicit replacement boolean; tests |
| p. 18, temporary HP duration | Expires when depleted or on completing a Long Rest | Depletion supported; full rest/time progression deferred, no claim of automatic rest expiry |
| p. 191, Unconscious | Strength/Dexterity saves automatically fail; unconsciousness confers Prone, which remains after waking | Save auto-outcome and persistent Prone flag; full condition engine deferred |

## Scope boundaries and interpretation notes

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
