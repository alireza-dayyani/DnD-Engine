# Roadmap

Phase 1 is the character/check/HP/persistence foundation. No Phase 2 work is authorized or implemented.

Recommended Phase 2: a narrowly bounded weapon-combat slice, after approval.

1. Complete the supporting condition/action/time lifecycle: Prone/standing, Incapacitated/Unconscious, full rest completion and temporary-HP expiry, basic stabilization and recovery, explicit death-save situational modifiers. Keep revised knockout behavior distinct from HP-zero unconsciousness.
2. Add a small sourced weapon/armor data pack, equipped item instances, and AC calculation with documented provenance.
3. Add a combat encounter with initiative/tie decisions, turn order, action economy, attack versus AC, critical hits and typed damage with resistance/immunity/vulnerability in the SRD order. Restrict the initial supported weapons/conditions to a documented testable subset.
4. Audit every state transition atomically; add command idempotency before an AI tool adapter can automatically retry commands. Keep existing campaigns and source pins intact through migrations.

Acceptance: one small encounter can start, resolve legal weapon turns and zero-HP recovery, resume after restart, end, and yield an explainable timeline. Exclude full spellcasting, all classes/features, grids, lore retrieval, multiplayer, and AI narration. MCP should follow stable command/error/idempotency contracts rather than arrive before them.

Later independent work: character creation/progression, spell effects/resources, richer campaign NPC/quest/relationship state, licensed lore retrieval, then an MCP adapter. Each requires a separate approved scope.
