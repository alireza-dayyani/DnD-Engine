using DndEngine.Domain;
using DndEngine.Domain.Combat;

namespace DndEngine.Application;

public sealed class CombatService(ICampaignStore campaigns, ICombatStore store, ICombatCatalog catalog, IDiceRoller dice, TimeProvider clock,
    IProgressionStore? progressions = null)
{
    private sealed record Session(CombatEncounter Encounter, Campaign Campaign, CombatContent Content,
        Dictionary<Guid, Character> Characters, Dictionary<Guid, CombatProfile> Profiles, List<CampaignEvent> Events);

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
    public Task<CombatCommandResult<WeaponAttackResult>> AttackAsync(Guid id, AttackCombatant request, CancellationToken ct = default) =>
        Execute(id, "AttackMade", s => {
            if (request.Attack is null) throw new RuleViolation("Attack options are required.");
            var actor = s.Encounter.Combatant(request.CombatantId); var target = s.Encounter.Combatant(request.Attack.TargetId);
            var profile = s.Profiles[actor.CharacterId]; var weapon = profile.Weapon(request.Attack.WeaponId);
            var result = new WeaponAttackResolver(dice).Resolve(s.Encounter, actor.Id, s.Characters[actor.CharacterId], profile,
                s.Characters[target.CharacterId], s.Profiles[target.CharacterId], s.Content.Weapons.Single(x => x.Id == weapon.DefinitionId), request.Attack);
            ReleaseGrapples(s);
            return result;
        }, ct);
    public Task<CombatCommandResult<EncounterState>> EndTurnAsync(Guid id, CombatActor request, CancellationToken ct = default) =>
        Execute(id, "TurnAdvanced", s => {
            s.Encounter.RequireTurn(request.CombatantId);
            Emit(s, "TurnEnded", new { request.CombatantId }); Expire(s, ExpiryBoundary.TurnEnd);
            var round = s.Encounter.State.Round; s.Encounter.EndTurn(request.CombatantId);
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
    private static ConditionEffects Effects(Session s, Guid memberId)
    { var member = s.Encounter.Combatant(memberId); return new(s.Characters[member.CharacterId], s.Profiles[member.CharacterId]); }
    private async Task<CombatCommandResult<T>> Execute<T>(Guid id, string type, Func<Session, T> command, CancellationToken ct, Guid? subject = null)
    { var s = await Load(id, ct); var result = command(s); return await Save(s, type, result, ct, subject); }
    private async Task<CombatCommandResult<T>> Save<T>(Session s, string type, T result, CancellationToken ct, Guid? subject = null)
    {
        foreach (var member in s.Encounter.State.Combatants)
        {
            var effects = Effects(s, member.Id);
            if (member.Resources.Dodging && (!effects.CanAct || effects.Speed == 0)) s.Encounter.SetResources(member.Id, member.Resources with { Dodging = false });
        }
        // The HTTP/event snapshot should carry the committed revision, like the outer result.
        if (result is EncounterState state) result = (T)(object)(state with { Revision = s.Encounter.State.Revision + 1 });
        Emit(s, type, result, subject);
        if (type == "CombatStarted")
        {
            var started = s.Events[^1]; s.Events.RemoveAt(s.Events.Count - 1); s.Events.Insert(0, started);
        }
        await store.SaveEncounterAsync(s.Encounter, s.Characters.Values.ToArray(), s.Profiles.Values.ToArray(), s.Events, false, ct);
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
        return new(encounter, campaign, content, characters, profiles, []);
    }
    private async Task<Campaign> Campaign(Guid id, CancellationToken ct)
    { var result = await campaigns.GetCampaignAsync(id, ct) ?? throw new NotFoundException("Campaign not found."); result.Ruleset.RequireSupported(); return result; }
    private async Task<Character> Character(Guid id, CancellationToken ct) => await campaigns.GetCharacterAsync(id, ct) ?? throw new NotFoundException("Character not found.");
    private async Task<CombatProfile> Profile(Guid id, CancellationToken ct) => await store.GetProfileAsync(id, ct) ?? throw new RuleViolation("Import combat capabilities first.");
}
