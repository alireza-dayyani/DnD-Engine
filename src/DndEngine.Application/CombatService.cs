using DndEngine.Domain;
using DndEngine.Domain.Combat;
using DndEngine.Domain.Progression;

namespace DndEngine.Application;

public sealed class CombatService(ICampaignStore campaigns, ICombatStore store, ICombatCatalog catalog, IDiceRoller dice, TimeProvider clock,
    IProgressionStore? progressions = null, ISpellCatalog? spellCatalog = null)
{
    private sealed record Session(CombatEncounter Encounter, Campaign Campaign, CombatContent Content,
        Dictionary<Guid, Character> Characters, Dictionary<Guid, CombatProfile> Profiles, List<CampaignEvent> Events,
        Dictionary<Guid, ProgressionState> ProgressionUpdates);
    private sealed record ReactionCast(ProgressionState State, SpellDefinition Spell,
        ClassSpellcasting Casting, HellishRebukeReaction Declaration);

    public async Task<CombatContent> ContentAsync(Guid campaignId, CancellationToken ct = default) =>
        await catalog.GetAsync((await Campaign(campaignId, ct)).Ruleset, ct);
    public async Task<CombatProfileState> ProfileAsync(Guid characterId, CancellationToken ct = default)
    {
        var character = await Character(characterId, ct); await Campaign(character.CampaignId, ct);
        return (await Profile(characterId, ct)).State;
    }
    public async Task<CombatProfileState> ImportAsync(Guid characterId, CombatCapabilities capabilities, CancellationToken ct = default)
    {
        var character = await Character(characterId, ct); var campaign = await Campaign(character.CampaignId, ct);
        if (progressions is not null && await progressions.GetAsync(characterId,ct) is not null)
            throw new RuleViolation("Choice-based characters derive combat capabilities from progression; manual import is unavailable.");
        capabilities.Validate(); var content = await catalog.GetAsync(campaign.Ruleset, ct);
        if (capabilities.WeaponProficiencies.Any(id => content.Weapons.All(w => w.Id != id))) throw new RuleViolation("Unknown weapon proficiency.");
        if (await store.IsEnrolledAsync(characterId, ct)) throw new RuleViolation("Import combat capabilities before enrolling in an encounter.");
        var profile = await store.GetProfileAsync(characterId, ct) ?? new(new(characterId, capabilities, [], []));
        profile.ImportCapabilities(capabilities);
        await store.SaveProfileAsync(character, profile, Event(campaign, "CombatCapabilitiesImported", profile.State, character), ct);
        return profile.State;
    }
    public async Task<OwnedWeapon> GrantWeaponAsync(Guid characterId, GrantWeapon request, CancellationToken ct = default)
    {
        var character = await Character(characterId, ct); var campaign = await Campaign(character.CampaignId, ct);
        if (progressions is not null && await progressions.GetAsync(characterId,ct) is not null)
            throw new RuleViolation("Use the character inventory endpoint for choice-based characters.");
        var profile = await Profile(characterId, ct);
        if (await store.IsEnrolledAsync(characterId, ct)) throw new RuleViolation("Grant equipment before enrolling in combat.");
        var weapon = (await catalog.GetAsync(campaign.Ruleset, ct)).Weapons.SingleOrDefault(x => x.Id == request.DefinitionId)
            ?? throw new RuleViolation("Unknown weapon definition.");
        Guard.Range(request.Ammunition, 0, 100000, "Ammunition");
        if (!weapon.Has(WeaponProperty.Ammunition) && request.Ammunition != 0) throw new RuleViolation("Weapon does not use ammunition.");
        var instance = new OwnedWeapon(Guid.NewGuid(), weapon.Id, request.Ammunition);
        profile.GrantWeapon(instance);
        await store.SaveProfileAsync(character, profile, Event(campaign, "WeaponGranted", instance, character), ct); return instance;
    }
    public async Task<ActiveCondition> RemoveOutsideCombatAsync(Guid characterId, Guid conditionId, CancellationToken ct = default)
    {
        var character = await Character(characterId, ct); var campaign = await Campaign(character.CampaignId, ct);
        if (await store.IsEnrolledAsync(characterId, ct)) throw new RuleViolation("Remove the condition through the encounter while enrolled.");
        var profile = await Profile(characterId, ct); var removed = profile.RemoveCondition(conditionId);
        await store.SaveProfileAsync(character, profile, Event(campaign, "ConditionRemoved", removed, character), ct); return removed;
    }
    public async Task<EncounterState> CreateAsync(Guid campaignId, CreateCombat request, CancellationToken ct = default)
    {
        var campaign = await Campaign(campaignId, ct); await catalog.GetAsync(campaign.Ruleset, ct);
        var encounter = CombatEncounter.Create(campaignId, request.Name);
        await store.SaveEncounterAsync(encounter, [], [], [Event(campaign, "CombatCreated", encounter.State)], true, ct); return encounter.State;
    }
    public async Task<CombatView> GetAsync(Guid id, CancellationToken ct = default)
    {
        var s = await Load(id, ct); return new(s.Encounter.State, s.Encounter.CurrentCombatantId, s.Encounter.Ties(),
            s.Characters.Values.Select(x => CharacterView.From(x)).ToArray(), s.Profiles.Values.Select(x => x.State).ToArray());
    }
    public async Task<CombatCommandResult<CombatantState>> AddAsync(Guid id, AddCombatant request, CancellationToken ct = default)
    {
        var s = await Load(id, ct); var character = await Character(request.CharacterId, ct);
        if (character.CampaignId != s.Campaign.Id) throw new RuleViolation("Combatant belongs to another campaign.");
        if (await store.IsEnrolledAsync(character.Id, ct)) throw new RuleViolation("Character is already enrolled in an unfinished encounter.");
        character.Health.RequireAlive(); var profile = await Profile(character.Id, ct);
        var member = s.Encounter.Add(character.Id, request.Kind, request.ZeroHpPolicy, request.Surprised, request.InitiativeGroup);
        s.Characters.Add(character.Id, character); s.Profiles.Add(character.Id, profile);
        return await Save(s, "CombatantAdded", member, ct);
    }
    public Task<CombatCommandResult<InitiativeResult>> InitiativeAsync(Guid id, CancellationToken ct = default, InitiativeContext? context = null) =>
        Execute(id, "InitiativeRolled", s => s.Encounter.RollInitiative(s.Characters, s.Profiles, dice, context?.VisibleFearSources), ct);
    public Task<CombatCommandResult<EncounterState>> StartAsync(Guid id, StartCombat request, CancellationToken ct = default) =>
        Execute(id, "CombatStarted", s => { s.Encounter.Start(request.Order); StartTurn(s); return s.Encounter.State; }, ct);
    public Task<CombatCommandResult<MovementResult>> MoveAsync(Guid id, MoveCombatant request, CancellationToken ct = default) =>
        Execute(id, "MovementUsed", s => s.Encounter.Move(request.CombatantId, Effects(s, request.CombatantId), request.Distance,
            request.Mode, request.DifficultTerrain, request.ApproachesFear), ct);
    public Task<CombatCommandResult<int>> StandAsync(Guid id, CombatActor request, CancellationToken ct = default) =>
        Execute(id, "CombatantStood", s => { var c = s.Encounter.Combatant(request.CombatantId); return s.Encounter.Stand(c.Id, s.Characters[c.CharacterId], s.Profiles[c.CharacterId]); }, ct, request.CombatantId);
    public Task<CombatCommandResult<TurnResources>> ActionAsync(Guid id, TakeCombatAction request, CancellationToken ct = default) =>
        Execute(id, "CombatActionTaken", s => { s.Encounter.Act(request.CombatantId, Effects(s, request.CombatantId), request.Action); return s.Encounter.Combatant(request.CombatantId).Resources; }, ct, request.CombatantId);
    public async Task<CombatCommandResult<WeaponAttackResult>> AttackAsync(Guid id, AttackCombatant request, CancellationToken ct = default)
    {
        var s = await Load(id,ct);
        if (request.Attack is null) throw new RuleViolation("Attack options are required.");
        if (request.Reaction is not null && request.ExpectedRevision != s.Encounter.State.Revision)
            throw new StateConflictException("Encounter revision changed. Reload before declaring a reaction.");
        var context = request.Attack.Context ?? throw new RuleViolation("Attack context is required.");
        var actor = s.Encounter.Combatant(request.CombatantId); var target = s.Encounter.Combatant(request.Attack.TargetId);
        var profile = s.Profiles[actor.CharacterId]; var weapon = profile.Weapon(request.Attack.WeaponId);
        var reaction = request.Reaction is { } declared
            ? await PrepareReaction(s,target.Id,actor.Id,context.DistanceFeet,
                context.TargetCanSeeAttacker,declared,ct) : null;
        var result = new WeaponAttackResolver(dice).Resolve(s.Encounter, actor.Id, s.Characters[actor.CharacterId], profile,
            s.Characters[target.CharacterId], s.Profiles[target.CharacterId], s.Content.Weapons.Single(x => x.Id == weapon.DefinitionId), request.Attack);
        if (result.Damage is { AppliedDamage: > 0 }) CheckConcentration(s,target.Id,result.Damage.AppliedDamage);
        if (reaction is not null && result.Damage is { AppliedDamage: > 0 } &&
            Effects(s,target.Id).CanAct)
            result = result with { ReactionSpell=ResolveReaction(s,target.Id,actor.Id,
                context.DistanceFeet,context.TargetCanSeeAttacker,
                context.AttackerCanSeeTarget,reaction) };
        ReleaseGrapples(s);
        return await Save(s,"AttackMade",result,ct,actor.Id);
    }
    public async Task<CombatCommandResult<CombatSpellResolution>> CastSpellAsync(Guid id, CastCombatSpell request, CancellationToken ct = default)
    {
        if (progressions is null || spellCatalog is null) throw new RuleViolation("Spellcasting is not installed.");
        var s = await Load(id,ct);
        if (s.Encounter.State.Revision != request.ExpectedRevision)
            throw new StateConflictException("Encounter revision changed. Reload before casting.");
        var actor = s.Encounter.RequireTurn(request.CombatantId);
        var caster = s.Characters[actor.CharacterId];
        var profile = s.Profiles[caster.Id];
        var state = await progressions.GetAsync(caster.Id,ct)
            ?? throw new RuleViolation("Caster has no spellcasting progression.");
        var spell = (await spellCatalog.GetAsync(s.Campaign.Ruleset,
            state.SpellPackVersion ?? SpellPackVersions.Initial,ct))
            .SingleOrDefault(x => x.Id == request.SpellId) ?? throw new RuleViolation("Spell is not in the pinned pack.");
        var granted = FeatureSpells.AlwaysPrepared(state)
            .Any(x => x.ClassId == request.ClassId && x.SpellId == spell.Id);
        var arcanumKnown = request.ClassId == "warlock" &&
            (state.MysticArcanumChoices ?? []).TryGetValue(spell.Level,out var arcanumId) && arcanumId == spell.Id;
        if ((!spell.ClassIds.Contains(request.ClassId) && !granted) ||
            !(spell.Level == 0 ? (state.KnownCantrips ?? []).Any(x => x.ClassId == request.ClassId && x.SpellId == spell.Id)
                : (state.PreparedSpells ?? []).Any(x => x.ClassId == request.ClassId && x.SpellId == spell.Id) || granted || arcanumKnown))
            throw new RuleViolation("Spell is not known or prepared for this class.");
        if (request.Metamagic is not null && request.ClassId != "sorcerer")
            throw new RuleViolation("Metamagic requires a Sorcerer spell.");
        if (request.Metamagic == MetamagicOption.HeightenedSpell &&
            (spell.Effect != SpellEffectKind.SavingThrowDamage || request.MetamagicTargetId is not { } heightenedTarget ||
             request.Targets is null || !request.Targets.Any(x => x.CombatantId == heightenedTarget)) ||
            request.Metamagic != MetamagicOption.HeightenedSpell && request.MetamagicTargetId is not null)
            throw new RuleViolation("Heightened Spell requires one selected saving throw target.");
        if (request.Metamagic == MetamagicOption.DistantSpell && spell.Range.StartsWith("Self",StringComparison.Ordinal))
            throw new RuleViolation("Distant Spell requires a spell with a non-Self range.");
        var castState = request.Metamagic is { } option ? SorceryPoints.Spend(state,option) : state;
        var subtle = request.Metamagic == MetamagicOption.SubtleSpell;
        if (!subtle && (spell.Components.Contains('V') && !request.VerbalAvailable ||
            spell.Components.Contains('S') && !request.SomaticAvailable) ||
            spell.Components.Contains('M') && (spell.MaterialCostGp > 0 || !subtle) && !request.MaterialAvailable)
            throw new RuleViolation("Required spell components are unavailable.");
        if (spell.MaterialItemId is { } materialId && !state.Inventory.Any(x => x.DefinitionId == materialId))
            throw new RuleViolation("The priced spell component is not in the caster's inventory.");
        if (profile.State.Capabilities.UntrainedArmorPenalty)
            throw new RuleViolation("Untrained armor prevents spellcasting.");
        var casting = SpellSlotCalculator.Derive(caster,state)?.Classes.SingleOrDefault(x => x.ClassId == request.ClassId)
            ?? throw new RuleViolation("Class has no spellcasting feature.");
        SpellSlotUsage? usage = null; int? slotBefore = null;
        if (spell.Level == 0)
        {
            if (request.SpellLevel != 0 || request.Pool is not null)
                throw new RuleViolation("Cantrips use no spell slot.");
        }
        else if (arcanumKnown)
        {
            if (request.Pool is not null || request.SpellLevel != spell.Level)
                throw new RuleViolation("Mystic Arcanum casts at its own level without a slot.");
            castState = MysticArcanum.Spend(castState,spell.Level,spell.Id);
        }
        else
        {
            if (request.Pool is null || request.SpellLevel < spell.Level)
                throw new RuleViolation("A spell of level 1+ requires an eligible slot.");
            (usage,slotBefore) = SpellSlotCalculator.Spend(caster,state,request.Pool.Value,request.SpellLevel);
        }
        ReactionCast? reaction = null;
        if (request.Reaction is { } declared)
        {
            if (spell.Damage is null || spell.Damage.Area || request.Targets is not { Length: 1 })
                throw new RuleViolation("Spell-damage reactions currently require one direct damage target.");
            var target = request.Targets[0];
            reaction = await PrepareReaction(s,target.CombatantId,actor.Id,target.DistanceFeet,
                target.TargetCanSeeCaster,declared,ct);
        }
        var quickened = request.Metamagic == MetamagicOption.QuickenedSpell;
        if (quickened && spell.CastingTime != "Action")
            throw new RuleViolation("Quickened Spell requires an Action casting time.");
        var resources = s.Encounter.UseMagic(actor.Id,Effects(s,actor.Id),
            quickened ? "Bonus Action" : spell.CastingTime,spell.Level > 0 && !arcanumKnown,quickened);
        var targets = new SpellCombatResolver(dice).Resolve(s.Encounter,actor.Id,caster,profile,
            s.Characters,s.Profiles,spell,casting,request.Targets,request.SpellLevel,
            request.AreaCenterDistanceFeet,request.Metamagic == MetamagicOption.DistantSpell,
            request.MetamagicTargetId);
        AttackPenaltyEffect[] appliedPenalties = [];
        if (spell.Id == "vicious-mockery" && targets[0].HitOrFailedSave)
        {
            var penalty = s.Encounter.State.AttackPenalties!.Last();
            appliedPenalties = [penalty];
            Emit(s,"AttackPenaltyApplied",penalty,penalty.TargetCombatantId);
        }
        ActiveSpellEffect? activeEffect = null;
        if (spell.ConcentrationTurns > 0)
        {
            if (targets.Length != 1) throw new RuleViolation("Concentration target must be singular.");
            var replaced = s.Encounter.ActiveSpells.SingleOrDefault(x => x.CasterCombatantId == actor.Id);
            activeEffect = s.Encounter.StartConcentration(actor.Id,targets[0].CombatantId,spell.Id,
                spell.ConcentrationTurns);
            if (replaced is not null)
                Emit(s,"ConcentrationEnded",new { Effect=replaced,Reason="replaced" },actor.Id);
        }
        foreach (var target in targets.Where(x => x.Damage is { AppliedDamage: > 0 }))
            CheckConcentration(s,target.CombatantId,target.Damage!.AppliedDamage);
        if (usage is not null || request.Metamagic is not null || arcanumKnown)
            s.ProgressionUpdates.Add(caster.Id,castState with { SpellSlots=usage ?? state.SpellSlots });
        ReactionSpellResolution? reactionResult = null;
        if (reaction is not null && targets[0].Damage is { AppliedDamage: > 0 } &&
            Effects(s,targets[0].CombatantId).CanAct)
        {
            var target = request.Targets[0];
            reactionResult = ResolveReaction(s,target.CombatantId,actor.Id,target.DistanceFeet,
                target.TargetCanSeeCaster,target.CasterCanSeeTarget,reaction);
        }
        ReleaseGrapples(s);
        return await Save(s,"CombatSpellCast",new CombatSpellResolution(request.ClassId,spell.Id,
            request.SpellLevel,request.Pool,slotBefore,resources,targets,activeEffect,
            request.Metamagic,request.Metamagic is null ? null : SorceryPoints.Remaining(castState),
            arcanumKnown,appliedPenalties,reactionResult),ct,actor.Id);
    }

    public async Task<CombatCommandResult<FontOfMagicResult>> ConvertSpellSlotAsync(Guid id,
        CombatConvertSpellSlot request,CancellationToken ct = default)
    {
        if (progressions is null) throw new RuleViolation("Font of Magic is not installed.");
        var s = await Load(id,ct);
        if (s.Encounter.State.Revision != request.ExpectedRevision)
            throw new StateConflictException("Encounter revision changed. Reload before converting a slot.");
        var actor = s.Encounter.RequireTurn(request.CombatantId);
        Effects(s,actor.Id).RequireAction();
        var caster = s.Characters[actor.CharacterId];
        var state = await progressions.GetAsync(caster.Id,ct)
            ?? throw new RuleViolation("Caster has no progression state.");
        var updated = SorceryPoints.ConvertSlot(caster,state,request.Pool,request.SpellLevel);
        s.ProgressionUpdates.Add(caster.Id,updated);
        return await Save(s,"SpellSlotConverted",new FontOfMagicResult(actor.Id,
            SorceryPoints.Remaining(updated),SpellSlotCalculator.Derive(caster,updated)!,
            s.Encounter.Combatant(actor.Id).Resources),ct,actor.Id);
    }

    public async Task<CombatCommandResult<FontOfMagicResult>> CreateSorcerySlotAsync(Guid id,
        CombatCreateSorcerySlot request,CancellationToken ct = default)
    {
        if (progressions is null) throw new RuleViolation("Font of Magic is not installed.");
        var s = await Load(id,ct);
        if (s.Encounter.State.Revision != request.ExpectedRevision)
            throw new StateConflictException("Encounter revision changed. Reload before creating a slot.");
        var actor = s.Encounter.RequireTurn(request.CombatantId);
        var caster = s.Characters[actor.CharacterId];
        var state = await progressions.GetAsync(caster.Id,ct)
            ?? throw new RuleViolation("Caster has no progression state.");
        var updated = SorceryPoints.CreateSlot(state,request.SpellLevel);
        var resources = s.Encounter.UseMagic(actor.Id,Effects(s,actor.Id),"Bonus Action",false);
        s.ProgressionUpdates.Add(caster.Id,updated);
        return await Save(s,"SorcerySlotCreated",new FontOfMagicResult(actor.Id,
            SorceryPoints.Remaining(updated),SpellSlotCalculator.Derive(caster,updated)!,resources),ct,actor.Id);
    }
    public Task<CombatCommandResult<EncounterState>> EndTurnAsync(Guid id, CombatActor request, CancellationToken ct = default) =>
        Execute(id, "TurnAdvanced", s => {
            s.Encounter.RequireTurn(request.CombatantId);
            Emit(s, "TurnEnded", new { request.CombatantId }); Expire(s, ExpiryBoundary.TurnEnd);
            var active = s.Encounter.ActiveSpells;
            var round = s.Encounter.State.Round; s.Encounter.EndTurn(request.CombatantId);
            foreach (var spell in active.Except(s.Encounter.ActiveSpells))
                Emit(s,"SpellEffectExpired",spell,spell.CasterCombatantId);
            if (s.Encounter.State.Round != round) Emit(s, "RoundStarted", new { s.Encounter.State.Round });
            StartTurn(s); return s.Encounter.State;
        }, ct);
    public Task<CombatCommandResult<EncounterState>> EndAsync(Guid id, CancellationToken ct = default) =>
        Execute(id, "CombatEnded", s => { s.Encounter.Complete(); return s.Encounter.State; }, ct);
    public Task<CombatCommandResult<ActiveCondition>> ApplyConditionAsync(Guid id, ApplyCombatCondition request, CancellationToken ct = default) =>
        Execute(id, "ConditionApplied", s => {
            s.Encounter.RequireOpen(); var member = s.Encounter.Combatant(request.CombatantId);
            Guard.Defined(request.Expiry);
            if (request.Expiry == ExpiryBoundary.Manual && request.ExpiresOnTurn is not null ||
                request.Expiry != ExpiryBoundary.Manual && (request.ExpiresOnTurn is null || request.ExpiresOnTurn <= s.Encounter.State.TurnNumber))
                throw new RuleViolation("Timed conditions require a future global encounter turn number.");
            if (request.SourceCharacterId is Guid source && !s.Characters.ContainsKey(source)) throw new RuleViolation("Condition source must be in this encounter.");
            var condition = new ActiveCondition(Guid.NewGuid(), request.Kind, request.Source, request.SourceCharacterId, request.Expiry,
                request.Expiry == ExpiryBoundary.Manual ? null : id, request.ExpiresOnTurn);
            s.Profiles[member.CharacterId].AddCondition(condition, s.Characters[member.CharacterId]); ReleaseGrapples(s); return condition;
        }, ct, request.CombatantId);
    public Task<CombatCommandResult<ActiveCondition>> RemoveConditionAsync(Guid id, Guid combatantId, Guid conditionId, CancellationToken ct = default) =>
        Execute(id, "ConditionRemoved", s => { s.Encounter.RequireOpen(); return s.Profiles[s.Encounter.Combatant(combatantId).CharacterId].RemoveCondition(conditionId); }, ct, combatantId);
    public Task<CombatCommandResult<CombatSaveResult>> SavingThrowAsync(Guid id, CombatSavingThrow request, CancellationToken ct = default) =>
        Execute<CombatSaveResult>(id, "CombatSavingThrowMade", s => {
            s.Encounter.RequireActive(); var member = s.Encounter.Combatant(request.CombatantId); var character = s.Characters[member.CharacterId];
            character.Health.RequireAlive(); var effects = Effects(s, member.Id); var check = request.Check ?? throw new RuleViolation("Saving throw options are required.");
            Guard.Defined(check.Ability); Guard.Range(check.Dc, 0, 1000, "DC"); Guard.Range(check.OtherModifier, -100, 100, "Modifier");
            if (check.VoluntaryFailure || effects.PhysicalSaveFailure && check.Ability is Ability.Strength or Ability.Dexterity)
                return new(member.Id, check.Ability, check.Dc, true, false, null);
            var modifier = character.AbilityModifier(check.Ability) + (character.SavingThrowProficiencies.Contains(check.Ability) ? character.Level.ProficiencyBonus : 0) + check.OtherModifier + effects.D20Penalty;
            var roll = D20Roll.Make(dice, modifier, check.Advantage || check.Ability == Ability.Dexterity && member.Resources.Dodging && effects.CanAct && effects.Speed > 0,
                check.Disadvantage || check.Ability == Ability.Dexterity && effects.Has(ConditionKind.Restrained));
            return new(member.Id, check.Ability, check.Dc, false, roll.Total >= check.Dc, roll);
        }, ct);

    private void StartTurn(Session s)
    {
        Expire(s, ExpiryBoundary.TurnStart); ReleaseGrapples(s);
        var member = s.Encounter.Combatant(s.Encounter.CurrentCombatantId!.Value); var character = s.Characters[member.CharacterId];
        Emit(s, "TurnStarted", new { member.Id, member.CharacterId });
        if (member.ZeroHpPolicy == ZeroHpPolicy.DeathSaves && character.Health.State is { Current: 0, Dead: false, Stable: false })
        {
            var modifier = Effects(s, member.Id).D20Penalty;
            var change = character.Health.DeathSave(dice, modifier);
            Emit(s, "DeathSavingThrowMade", new { member.Id, member.CharacterId, Modifier = modifier, Change = change });
        }
    }
    private void Expire(Session s, ExpiryBoundary boundary)
    {
        foreach (var profile in s.Profiles.Values)
            foreach (var condition in profile.Expire(s.Encounter.State.Id, s.Encounter.State.TurnNumber, boundary))
                Emit(s, "ConditionExpired", new { profile.State.CharacterId, Condition = condition });
    }
    private void ReleaseGrapples(Session s)
    {
        foreach (var profile in s.Profiles.Values)
            foreach (var condition in profile.State.Conditions.Where(c => c.Kind == ConditionKind.Grappled && c.SourceCharacterId is Guid source &&
                s.Characters.TryGetValue(source, out var character) && !new ConditionEffects(character, s.Profiles[source]).CanAct).ToArray())
            { profile.RemoveCondition(condition.Id); Emit(s, "ConditionRemoved", new { profile.State.CharacterId, Condition = condition, Reason = "Grappler incapacitated" }); }
    }
    private async Task<ReactionCast> PrepareReaction(Session s, Guid defenderId, Guid sourceId,
        int distanceFeet, bool sourceVisible, HellishRebukeReaction declared, CancellationToken ct)
    {
        if (progressions is null || spellCatalog is null)
            throw new RuleViolation("Spellcasting is not installed.");
        s.Encounter.RequireActive();
        if (defenderId == sourceId || distanceFeet is < 0 or > 60 || !sourceVisible ||
            declared.SourceCover == Cover.Total)
            throw new RuleViolation("Hellish Rebuke requires a visible damage source within 60 feet.");
        Guard.Defined(declared.SourceCover);
        var defenderMember = s.Encounter.Combatant(defenderId);
        var defender = s.Characters[defenderMember.CharacterId];
        var state = await progressions.GetAsync(defender.Id,ct)
            ?? throw new RuleViolation("Defender has no spellcasting progression.");
        var spell = (await spellCatalog.GetAsync(s.Campaign.Ruleset,
            state.SpellPackVersion ?? SpellPackVersions.Initial,ct))
            .SingleOrDefault(x => x.Id == "hellish-rebuke")
            ?? throw new RuleViolation("Hellish Rebuke is not in the defender's pinned pack.");
        if (!(state.PreparedSpells ?? []).Any(x => x.ClassId == "warlock" && x.SpellId == spell.Id))
            throw new RuleViolation("Hellish Rebuke is not prepared as a Warlock spell.");
        if (s.Profiles[defender.Id].State.Capabilities.UntrainedArmorPenalty ||
            !declared.VerbalAvailable || !declared.SomaticAvailable)
            throw new RuleViolation("The defender cannot supply the spell components.");
        var casting = SpellSlotCalculator.Derive(defender,state)?.Classes
            .SingleOrDefault(x => x.ClassId == "warlock")
            ?? throw new RuleViolation("Defender has no Warlock spellcasting feature.");
        if (declared.SpellLevel < spell.Level)
            throw new RuleViolation("Hellish Rebuke requires an eligible slot.");
        if (defenderMember.Resources.ReactionUsed ||
            s.Encounter.CurrentCombatantId == defenderId &&
            (defenderMember.Resources.SpellSlotCast || defenderMember.Resources.QuickenedSpellUsed) ||
            !Effects(s,defenderId).CanAct)
            throw new RuleViolation("The defender cannot take a spell reaction now.");
        SpellSlotCalculator.Spend(defender,state,declared.Pool,declared.SpellLevel);
        return new(state,spell,casting,declared);
    }

    private ReactionSpellResolution ResolveReaction(Session s, Guid defenderId, Guid sourceId,
        int distanceFeet, bool defenderCanSeeSource, bool sourceCanSeeDefender, ReactionCast prepared)
    {
        var member = s.Encounter.Combatant(defenderId);
        var defender = s.Characters[member.CharacterId];
        var declared = prepared.Declaration;
        var (usage,slotBefore) = SpellSlotCalculator.Spend(defender,prepared.State,
            declared.Pool,declared.SpellLevel);
        var resources = s.Encounter.UseReactionMagic(defenderId,Effects(s,defenderId));
        var spellTarget = new SpellTargetContext(sourceId,distanceFeet,defenderCanSeeSource,
            sourceCanSeeDefender,declared.SourceCover);
        var retaliation = new SpellCombatResolver(dice).Resolve(s.Encounter,defenderId,defender,
            s.Profiles[defender.Id],s.Characters,s.Profiles,prepared.Spell,prepared.Casting,
            [spellTarget],declared.SpellLevel).Single();
        if (retaliation.Damage is { AppliedDamage: > 0 })
            CheckConcentration(s,sourceId,retaliation.Damage.AppliedDamage);
        s.ProgressionUpdates.Add(defender.Id,prepared.State with { SpellSlots=usage });
        return new(prepared.Spell.Id,declared.SpellLevel,declared.Pool,slotBefore,resources,retaliation);
    }
    private void CheckConcentration(Session s,Guid damagedCombatantId,int damage)
    {
        if (!s.Encounter.ActiveSpells.Any(x => x.CasterCombatantId == damagedCombatantId)) return;
        var member = s.Encounter.Combatant(damagedCombatantId);
        var character = s.Characters[member.CharacterId];
        var effects = Effects(s,damagedCombatantId);
        if (!effects.CanAct)
        {
            var ended = s.Encounter.EndConcentration(damagedCombatantId);
            Emit(s,"ConcentrationEnded",new { Effect=ended,Reason="incapacitated" },damagedCombatantId);
            return;
        }
        var dc = Math.Max(10,damage/2);
        var modifier = character.AbilityModifier(Ability.Constitution) +
            (character.SavingThrowProficiencies.Contains(Ability.Constitution) ? character.Level.ProficiencyBonus : 0) +
            effects.D20Penalty;
        var roll = D20Roll.Make(dice,modifier,false,false);
        var success = roll.Total >= dc;
        Emit(s,"ConcentrationChecked",new { Damage=damage,Dc=dc,Roll=roll,Success=success },damagedCombatantId);
        if (!success)
        {
            var ended = s.Encounter.EndConcentration(damagedCombatantId);
            Emit(s,"ConcentrationEnded",new { Effect=ended,Reason="failed save" },damagedCombatantId);
        }
    }
    private static ConditionEffects Effects(Session s, Guid memberId)
    { var member = s.Encounter.Combatant(memberId); return new(s.Characters[member.CharacterId], s.Profiles[member.CharacterId]); }
    private async Task<CombatCommandResult<T>> Execute<T>(Guid id, string type, Func<Session, T> command, CancellationToken ct, Guid? subject = null)
    { var s = await Load(id, ct); var result = command(s); return await Save(s, type, result, ct, subject); }
    private async Task<CombatCommandResult<T>> Save<T>(Session s, string type, T result, CancellationToken ct, Guid? subject = null)
    {
        foreach (var effect in s.Encounter.ActiveSpells.ToArray())
            if (!Effects(s,effect.CasterCombatantId).CanAct)
            {
                s.Encounter.EndConcentration(effect.CasterCombatantId);
                Emit(s,"ConcentrationEnded",new { Effect=effect,Reason="incapacitated" },effect.CasterCombatantId);
            }
        foreach (var member in s.Encounter.State.Combatants)
        {
            var effects = Effects(s, member.Id);
            if (member.Resources.Dodging && (!effects.CanAct || effects.Speed == 0)) s.Encounter.SetResources(member.Id, member.Resources with { Dodging = false });
        }
        // The HTTP/event snapshot should carry the committed revision, like the outer result.
        if (result is EncounterState) result = (T)(object)(s.Encounter.State with { Revision = s.Encounter.State.Revision + 1 });
        Emit(s, type, result, subject);
        if (type == "CombatStarted")
        {
            var started = s.Events[^1]; s.Events.RemoveAt(s.Events.Count - 1); s.Events.Insert(0, started);
        }
        await store.SaveEncounterAsync(s.Encounter, s.Characters.Values.ToArray(), s.Profiles.Values.ToArray(), s.Events, false, ct,
            s.ProgressionUpdates);
        return new(s.Encounter.State.Id, s.Encounter.State.Revision + 1, s.Encounter.State.Round, s.Encounter.State.TurnNumber, result);
    }
    private void Emit<T>(Session s, string type, T data, Guid? subject = null) => s.Events.Add(Event(s.Campaign, type,
        new { EncounterId = s.Encounter.State.Id, EncounterRevision = s.Encounter.State.Revision + 1, s.Encounter.State.Round, s.Encounter.State.TurnNumber, SubjectCombatantId = subject, Data = data }));
    private CampaignEvent Event<T>(Campaign campaign, string type, T data, Character? character = null) =>
        new(0, Guid.NewGuid(), campaign.Id, character?.Id, type, clock.GetUtcNow(), campaign.Ruleset, TimelineSerialization.SchemaVersion,
            character is null ? null : character.Revision + 1, TimelineSerialization.Serialize(data));
    private async Task<Session> Load(Guid id, CancellationToken ct)
    {
        var encounter = await store.GetEncounterAsync(id, ct) ?? throw new NotFoundException("Encounter not found.");
        var campaign = await Campaign(encounter.State.CampaignId, ct); var content = await catalog.GetAsync(campaign.Ruleset, ct);
        var characters = new Dictionary<Guid, Character>(); var profiles = new Dictionary<Guid, CombatProfile>();
        // Read each character revision BEFORE its profile; subsequent writes guard the complete read set.
        foreach (var member in encounter.State.Combatants)
        { characters.Add(member.CharacterId, await Character(member.CharacterId, ct)); profiles.Add(member.CharacterId, await Profile(member.CharacterId, ct)); }
        return new(encounter, campaign, content, characters, profiles, [],new());
    }
    private async Task<Campaign> Campaign(Guid id, CancellationToken ct)
    { var result = await campaigns.GetCampaignAsync(id, ct) ?? throw new NotFoundException("Campaign not found."); result.Ruleset.RequireSupported(); return result; }
    private async Task<Character> Character(Guid id, CancellationToken ct) => await campaigns.GetCharacterAsync(id, ct) ?? throw new NotFoundException("Character not found.");
    private async Task<CombatProfile> Profile(Guid id, CancellationToken ct) => await store.GetProfileAsync(id, ct) ?? throw new RuleViolation("Import combat capabilities first.");
}
