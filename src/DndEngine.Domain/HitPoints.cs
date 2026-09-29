namespace DndEngine.Domain;

public sealed record HealthState(int Maximum, int Current, int Temporary, bool Dead, bool Stable,
    int DeathSuccesses, int DeathFailures, bool Prone)
{
    public bool Unconscious => !Dead && Current == 0;
}

public sealed record HealthChange(string Operation, int Requested, HealthState Before, HealthState After,
    int HitPointsLost, int HitPointsRegained, int TemporaryAbsorbed, int ExcessDamage, int? DeathRoll = null, bool Critical = false);

/// <summary>PC health rules, SRD 5.2.1 pp. 16–18. Damage amounts are already resolved.</summary>
public sealed class HitPoints
{
    public HealthState State { get; private set; }
    public HitPoints(int maximum) : this(new HealthState(maximum, maximum, 0, false, false, 0, 0, false)) { }
    public HitPoints(HealthState state)
    {
        Guard.Range(state.Maximum, 1, 1000000, "Maximum HP");
        Guard.Range(state.Current, 0, state.Maximum, "Current HP");
        Guard.Range(state.Temporary, 0, 1000000, "Temporary HP");
        Guard.Range(state.DeathSuccesses, 0, 2, "Death successes");
        Guard.Range(state.DeathFailures, 0, 3, "Death failures");
        if ((state.Dead || state.Stable) && state.Current != 0 || state.Dead && state.Stable ||
            state.Current > 0 && (state.DeathFailures != 0 || state.DeathSuccesses != 0) ||
            state.Stable && (state.DeathFailures != 0 || state.DeathSuccesses != 0) ||
            state.DeathFailures == 3 && !state.Dead || state.Current == 0 && !state.Prone)
            throw new RuleViolation("Inconsistent health state.");
        State = state;
    }
    public HealthChange Damage(int amount, bool critical = false)
    {
        RequireAlive(); Guard.Range(amount, 0, 1000000, "Damage");
        var before = State;
        var absorbed = Math.Min(amount, before.Temporary);
        var remaining = amount - absorbed;
        var lost = Math.Min(remaining, before.Current);
        var excess = remaining - lost;
        // Temporary HP buffers HP loss, but taking damage at zero still triggers a failure.
        var failures = before.DeathFailures + (before.Current == 0 && amount > 0 ? critical ? 2 : 1 : 0);
        var dead = failures >= 3 || (before.Current == 0 ? amount >= before.Maximum : excess >= before.Maximum);
        State = before with { Current = before.Current - lost, Temporary = before.Temporary - absorbed,
            Dead = dead, Stable = amount == 0 && before.Stable, DeathFailures = Math.Min(3, failures),
            Prone = before.Prone || before.Current - lost == 0 };
        return new("Damage", amount, before, State, lost, 0, absorbed, excess, Critical: critical);
    }
    public HealthChange Heal(int amount)
    {
        RequireAlive(); Guard.Range(amount, 0, 1000000, "Healing");
        var before = State;
        var regained = Math.Min(amount, before.Maximum - before.Current);
        State = before with { Current = before.Current + regained,
            Stable = regained > 0 ? false : before.Stable,
            DeathFailures = regained > 0 ? 0 : before.DeathFailures,
            DeathSuccesses = regained > 0 ? 0 : before.DeathSuccesses };
        return new("Healing", amount, before, State, 0, regained, 0, 0);
    }
    public HealthChange GrantTemporary(int amount, bool replaceExisting)
    {
        RequireAlive(); Guard.Range(amount, 0, 1000000, "Temporary HP");
        var before = State;
        if (replaceExisting) State = before with { Temporary = amount };
        return new("TemporaryHitPoints", amount, before, State, 0, 0, 0, 0);
    }
    public HealthChange DeathSave(IDiceRoller dice)
    {
        RequireAlive();
        if (State.Current != 0 || State.Stable) throw new RuleViolation("Death save requires a dying character at zero HP.");
        var before = State;
        var roll = dice.Roll(new(1, 20)).Rolls[0];
        if (roll == 20) Heal(1);
        else
        {
            var successes = before.DeathSuccesses + (roll >= 10 ? 1 : 0);
            var failures = before.DeathFailures + (roll == 1 ? 2 : roll < 10 ? 1 : 0);
            var stable = successes >= 3;
            State = before with { Stable = stable, Dead = failures >= 3,
                DeathSuccesses = stable ? 0 : successes, DeathFailures = stable ? 0 : Math.Min(3, failures) };
        }
        return new("DeathSavingThrow", 0, before, State, 0, State.Current - before.Current, 0, 0, roll);
    }
    public void RequireAlive()
    {
        if (State.Dead) throw new RuleViolation("Character is dead; this operation cannot revive them.");
    }
}
