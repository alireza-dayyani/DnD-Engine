namespace DndEngine.Domain.Combat;

public sealed record D20Roll(IReadOnlyList<int> Rolls, int SelectedRoll, int Modifier, int Total, AdvantageState AdvantageState)
{
    public static D20Roll Make(IDiceRoller dice, int modifier, bool advantage, bool disadvantage)
    {
        var state = advantage == disadvantage ? AdvantageState.Normal : advantage ? AdvantageState.Advantage : AdvantageState.Disadvantage;
        var rolls = dice.Roll(new(state == AdvantageState.Normal ? 1 : 2, 20)).Rolls;
        var selected = state == AdvantageState.Disadvantage ? rolls.Min() : rolls.Max();
        return new(rolls, selected, modifier, selected + modifier, state);
    }
}
public sealed record TurnResources(bool ActionUsed = false, int AttacksRemaining = 0, bool BonusActionUsed = false,
    bool ReactionUsed = false, int MovementUsed = 0, int Dashes = 0, bool Dodging = false, bool Disengaging = false,
    Guid[]? LightWeaponsUsed = null, Guid[]? LoadingWeaponsUsed = null, bool SpellSlotCast = false,
    bool QuickenedSpellUsed = false);
public sealed record CombatantState(Guid Id, Guid CharacterId, CombatantKind Kind, ZeroHpPolicy ZeroHpPolicy,
    bool Surprised, string? InitiativeGroup, D20Roll? Initiative, TurnResources Resources);
public sealed record EncounterState(Guid Id, Guid CampaignId, string Name, EncounterStatus Status,
    CombatantState[] Combatants, Guid[] Order, int Round, int TurnIndex, long TurnNumber, long Revision,
    ActiveSpellEffect[]? ActiveSpells = null, AttackPenaltyEffect[]? AttackPenalties = null);
public sealed record InitiativeTie(int Total, Guid[] Combatants, string DecidedBy);
public sealed record InitiativeResult(CombatantState[] Combatants, InitiativeTie[] Ties);
public sealed record MovementResult(Guid CombatantId, int Distance, MovementMode Mode, bool DifficultTerrain, int Cost, int Speed, int Used, int Remaining);
public sealed record ActiveSpellEffect(Guid CasterCombatantId, Guid TargetCombatantId, string SpellId,
    long ExpiresOnTurn);
public sealed record AttackPenaltyEffect(Guid TargetCombatantId, string SpellId, long ExpiresOnTurn);

public sealed class CombatEncounter
{
    public EncounterState State { get; private set; }
    public ActiveSpellEffect[] ActiveSpells => State.ActiveSpells ?? [];
    public Guid? CurrentCombatantId => State.Status == EncounterStatus.Active ? State.Order[State.TurnIndex] : null;
    public CombatEncounter(EncounterState state)
    {
        Guard.Name(state.Name); Guard.Defined(state.Status);
        if (state.Id == Guid.Empty || state.CampaignId == Guid.Empty || state.Revision < 0 || state.Combatants is null || state.Order is null)
            throw new RuleViolation("Invalid encounter state.");
        if (state.Combatants.Select(x => x.Id).Distinct().Count() != state.Combatants.Length ||
            state.Combatants.Select(x => x.CharacterId).Distinct().Count() != state.Combatants.Length)
            throw new RuleViolation("Duplicate combatants.");
        if (state.Status == EncounterStatus.Active && (state.Round < 1 || state.Order.Length != state.Combatants.Length ||
            state.TurnIndex < 0 || state.TurnIndex >= state.Order.Length || state.Order.Distinct().Count() != state.Order.Length ||
            state.Order.Any(id => !state.Combatants.Any(x => x.Id == id)))) throw new RuleViolation("Invalid active turn state.");
        if ((state.ActiveSpells ?? []).Any(x => x.ExpiresOnTurn <= state.TurnNumber ||
            !state.Combatants.Any(c => c.Id == x.CasterCombatantId) ||
            !state.Combatants.Any(c => c.Id == x.TargetCombatantId)) ||
            (state.ActiveSpells ?? []).GroupBy(x => x.CasterCombatantId).Any(x => x.Count() > 1))
            throw new RuleViolation("Invalid active spell effects.");
        if ((state.AttackPenalties ?? []).Any(x => x.ExpiresOnTurn <= state.TurnNumber ||
            !state.Combatants.Any(c => c.Id == x.TargetCombatantId)))
            throw new RuleViolation("Invalid spell attack penalty.");
        State = state;
    }
    public static CombatEncounter Create(Guid campaignId, string name) => new(new(Guid.NewGuid(), campaignId, Guard.Name(name),
        EncounterStatus.Created, [], [], 0, 0, 0, 0));
    public CombatantState Combatant(Guid id) => State.Combatants.SingleOrDefault(x => x.Id == id) ?? throw new RuleViolation("Combatant is not in this encounter.");
    public void RequireOpen() { if (State.Status == EncounterStatus.Completed) throw new RuleViolation("Encounter is completed."); }
    public void RequireActive() { if (State.Status != EncounterStatus.Active) throw new RuleViolation("Combat is not active."); }
    public CombatantState RequireTurn(Guid id)
    {
        RequireActive();
        if (CurrentCombatantId != id) throw new RuleViolation("It is not this combatant's turn.");
        return Combatant(id);
    }
    public CombatantState Add(Guid characterId, CombatantKind kind, ZeroHpPolicy deathPolicy, bool surprised, string? group)
    {
        if (State.Status != EncounterStatus.Created) throw new RuleViolation("Combatants can only be added before initiative.");
        Guard.Defined(kind); Guard.Defined(deathPolicy);
        if (kind == CombatantKind.PlayerCharacter && deathPolicy != ZeroHpPolicy.DeathSaves) throw new RuleViolation("Player characters use death saves.");
        if (State.Combatants.Length >= 100 || State.Combatants.Any(x => x.CharacterId == characterId)) throw new RuleViolation("Duplicate combatant or encounter is full.");
        if (group is not null) { Guard.Name(group); if (kind != CombatantKind.Monster) throw new RuleViolation("Initiative groups are for identical monsters."); }
        var member = new CombatantState(Guid.NewGuid(), characterId, kind, deathPolicy, surprised, group, null, new());
        State = State with { Combatants = [.. State.Combatants, member] }; return member;
    }
    public InitiativeResult RollInitiative(IReadOnlyDictionary<Guid, Character> characters, IReadOnlyDictionary<Guid, CombatProfile> profiles, IDiceRoller dice,
        IReadOnlyDictionary<Guid, Guid[]>? visibleFearSources = null)
    {
        if (State.Status != EncounterStatus.Created || State.Combatants.Length < 2) throw new RuleViolation("Initiative requires at least two combatants and can be rolled only once.");
        var inputs = State.Combatants.Select(c => {
            var character = characters[c.CharacterId]; character.Health.RequireAlive();
            var profile = profiles[c.CharacterId]; var effects = new ConditionEffects(character, profile);
            return (Combatant:c, Modifier:character.AbilityModifier(Ability.Dexterity) + profile.State.Capabilities.InitiativeBonus + effects.D20Penalty,
                Advantage:profile.State.Capabilities.InitiativeAdvantage || effects.Has(ConditionKind.Invisible),
                Disadvantage:profile.State.Capabilities.InitiativeDisadvantage || c.Surprised || effects.Has(ConditionKind.Incapacitated) || effects.Has(ConditionKind.Poisoned) ||
                    effects.FearVisible(visibleFearSources is not null && visibleFearSources.TryGetValue(c.Id, out var sources) ? sources ?? [] : []));
        }).ToArray();
        foreach (var group in inputs.Where(x => x.Combatant.InitiativeGroup is not null).GroupBy(x => x.Combatant.InitiativeGroup))
            if (group.Select(x => (x.Modifier, x.Advantage, x.Disadvantage)).Distinct().Count() != 1)
                throw new RuleViolation("Identical-creature initiative group has different modifiers or roll circumstances.");
        var groupRolls = new Dictionary<string, D20Roll>();
        var members = inputs.Select(x => {
            D20Roll roll;
            if (x.Combatant.InitiativeGroup is string group && groupRolls.TryGetValue(group, out var shared)) roll = shared;
            else {
                roll = D20Roll.Make(dice, x.Modifier, x.Advantage, x.Disadvantage);
                if (x.Combatant.InitiativeGroup is string key) groupRolls.Add(key, roll);
            }
            return x.Combatant with { Initiative = roll };
        }).ToArray();
        State = State with { Status = EncounterStatus.Initiative, Combatants = members };
        return new(members, Ties());
    }
    public InitiativeTie[] Ties() => State.Combatants.Where(x => x.Initiative is not null).GroupBy(x => x.Initiative!.Total)
        .Where(g => g.Count() > 1).Select(g => new InitiativeTie(g.Key, g.Select(x => x.Id).ToArray(),
            g.All(x => x.Kind == CombatantKind.PlayerCharacter) ? "Players" : "GameMaster")).ToArray();
    public void Start(Guid[]? chosenOrder)
    {
        if (State.Status != EncounterStatus.Initiative) throw new RuleViolation("Roll initiative before starting combat.");
        if (chosenOrder is null && Ties().Length > 0) throw new RuleViolation("Tied initiative requires an explicit adjudicated order.");
        var order = chosenOrder ?? State.Combatants.OrderByDescending(x => x.Initiative!.Total).Select(x => x.Id).ToArray();
        if (order.Length != State.Combatants.Length || order.Distinct().Count() != order.Length || order.Any(x => !State.Combatants.Any(c => c.Id == x)))
            throw new RuleViolation("Initiative order must include each combatant exactly once.");
        for (var i = 1; i < order.Length; i++)
            if (Combatant(order[i]).Initiative!.Total > Combatant(order[i - 1]).Initiative!.Total) throw new RuleViolation("Order must preserve descending initiative totals.");
        State = State with { Status = EncounterStatus.Active, Order = order.ToArray(), Round = 1, TurnIndex = 0, TurnNumber = 1 };
        ResetCurrentTurn();
    }
    public void EndTurn(Guid actor)
    {
        var old = RequireTurn(actor);
        SetResources(actor, old.Resources with { Disengaging = false });
        var index = (State.TurnIndex + 1) % State.Order.Length;
        State = State with { TurnIndex = index, Round = State.Round + (index == 0 ? 1 : 0), TurnNumber = checked(State.TurnNumber + 1) };
        State = State with { ActiveSpells = ActiveSpells.Where(x => x.ExpiresOnTurn > State.TurnNumber).ToArray() };
        State = State with { AttackPenalties = (State.AttackPenalties ?? []).Where(x => x.ExpiresOnTurn > State.TurnNumber).ToArray() };
        ResetCurrentTurn();
    }
    private void ResetCurrentTurn() => SetResources(CurrentCombatantId!.Value, new());
    public void Complete()
    {
        RequireActive(); State = State with { Status = EncounterStatus.Completed, ActiveSpells = [], AttackPenalties = [] };
    }
    public void SetResources(Guid id, TurnResources resources) => State = State with {
        Combatants = State.Combatants.Select(x => x.Id == id ? x with { Resources = resources } : x).ToArray() };
    public int MovementRemaining(Guid id, int speed)
    {
        var r = Combatant(id).Resources; return Math.Max(0, speed * (1 + r.Dashes) - r.MovementUsed);
    }
    public MovementResult Move(Guid actor, ConditionEffects effects, int distance, MovementMode mode, bool difficult, bool approachesFear)
    {
        var c = RequireTurn(actor); Guard.Defined(mode); Guard.Range(distance, 1, 1000, "Movement distance");
        if (effects.Speed == 0) throw new RuleViolation("Speed is zero.");
        if (effects.Has(ConditionKind.Prone) && mode != MovementMode.Crawl) throw new RuleViolation("Prone combatants must crawl or stand.");
        if (effects.Has(ConditionKind.Frightened) && approachesFear) throw new RuleViolation("Cannot willingly approach the source of fear.");
        var cost = distance * (1 + (mode == MovementMode.Crawl ? 1 : 0) + (difficult ? 1 : 0));
        if (cost > MovementRemaining(actor, effects.Speed)) throw new RuleViolation("Insufficient movement.");
        SetResources(actor, c.Resources with { MovementUsed = c.Resources.MovementUsed + cost });
        return new(actor, distance, mode, difficult, cost, effects.Speed, Combatant(actor).Resources.MovementUsed, MovementRemaining(actor, effects.Speed));
    }
    public int Stand(Guid actor, Character character, CombatProfile profile)
    {
        var c = RequireTurn(actor); var effects = new ConditionEffects(character, profile);
        if (!effects.Has(ConditionKind.Prone) || effects.Speed == 0) throw new RuleViolation("Combatant cannot stand now.");
        var cost = effects.Speed / 2;
        if (MovementRemaining(actor, effects.Speed) < cost) throw new RuleViolation("Insufficient movement to stand.");
        character.Health.SetProne(false); profile.RemoveProne();
        SetResources(actor, c.Resources with { MovementUsed = c.Resources.MovementUsed + cost }); return cost;
    }
    public void Act(Guid actor, ConditionEffects effects, CombatAction action)
    {
        var c = RequireTurn(actor); effects.RequireAction(); Guard.Defined(action);
        if (c.Resources.ActionUsed) throw new RuleViolation("Action has already been used.");
        SetResources(actor, c.Resources with { ActionUsed = true,
            Dashes = c.Resources.Dashes + (action == CombatAction.Dash ? 1 : 0),
            Dodging = action == CombatAction.Dodge && effects.Speed > 0,
            Disengaging = action == CombatAction.Disengage });
    }

    public TurnResources UseMagic(Guid actor, ConditionEffects effects, string castingTime, bool usesSlot,
        bool quickened = false)
    {
        var c = RequireTurn(actor); effects.RequireAction();
        var resources = c.Resources;
        if (usesSlot && (resources.SpellSlotCast || resources.QuickenedSpellUsed) ||
            quickened && resources.SpellSlotCast)
            throw new RuleViolation("Only one spell slot can be expended to cast a spell on a turn.");
        resources = castingTime switch
        {
            "Action" when !resources.ActionUsed => resources with { ActionUsed = true, AttacksRemaining = 0 },
            "Bonus Action" when !resources.BonusActionUsed => resources with { BonusActionUsed = true },
            _ => throw new RuleViolation("Casting time is unavailable on this turn.")
        };
        resources = resources with { SpellSlotCast = resources.SpellSlotCast || usesSlot,
            QuickenedSpellUsed = resources.QuickenedSpellUsed || quickened };
        SetResources(actor,resources);
        return resources;
    }

    public TurnResources UseReactionMagic(Guid actor, ConditionEffects effects)
    {
        RequireActive();
        var combatant = Combatant(actor);
        effects.RequireAction();
        if (combatant.Resources.ReactionUsed || CurrentCombatantId == actor &&
            (combatant.Resources.SpellSlotCast || combatant.Resources.QuickenedSpellUsed))
            throw new RuleViolation("Reaction or spell slot casting is unavailable.");
        var resources = combatant.Resources with { ReactionUsed = true,
            SpellSlotCast = combatant.Resources.SpellSlotCast || CurrentCombatantId == actor };
        SetResources(actor,resources);
        return resources;
    }

    public ActiveSpellEffect StartConcentration(Guid caster, Guid target, string spellId, int rounds)
    {
        RequireTurn(caster);
        if (rounds < 1 || rounds > 600 || !State.Combatants.Any(x => x.Id == target))
            throw new RuleViolation("Invalid concentrating spell duration or target.");
        var effect = new ActiveSpellEffect(caster,target,spellId,
            checked(State.TurnNumber + (long)rounds * State.Order.Length));
        State = State with { ActiveSpells = [..ActiveSpells.Where(x => x.CasterCombatantId != caster),effect] };
        return effect;
    }
    public ActiveSpellEffect? EndConcentration(Guid caster)
    {
        var effect = ActiveSpells.SingleOrDefault(x => x.CasterCombatantId == caster);
        if (effect is not null) State = State with { ActiveSpells=ActiveSpells.Where(x => x != effect).ToArray() };
        return effect;
    }
    public bool HasSpellEffect(Guid target,string spellId) =>
        ActiveSpells.Any(x => x.TargetCombatantId == target && x.SpellId == spellId);

    public void ApplyNextAttackPenalty(Guid target,string spellId)
    {
        RequireActive(); Combatant(target);
        var index = Array.IndexOf(State.Order,target);
        var offset = (index-State.TurnIndex+State.Order.Length)%State.Order.Length;
        if (offset == 0) offset = State.Order.Length;
        var expires = checked(State.TurnNumber+offset+1);
        State = State with { AttackPenalties = [..State.AttackPenalties ?? [],new(target,spellId,expires)] };
    }

    public bool ConsumeNextAttackPenalty(Guid attacker)
    {
        var hasPenalty = (State.AttackPenalties ?? []).Any(x => x.TargetCombatantId == attacker);
        if (hasPenalty)
            State = State with { AttackPenalties = (State.AttackPenalties ?? []).Where(x => x.TargetCombatantId != attacker).ToArray() };
        return hasPenalty;
    }
}
