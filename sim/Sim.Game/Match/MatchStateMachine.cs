using Sim.Core.Time;

namespace Sim.Game.Match;

/// <summary>
/// MATCH flow (spec 03 "Game state machine", §10.1, §10.4 p85, Table 9-1 p79):
/// PreMatch → Auto (30 s) → Transition (8 s) → Teleop (2:00) → MatchEnd → PostMatchSettle → Results,
/// or Aborted. Tick-exact: every boundary is an integer tick computed from the config durations.
/// </summary>
public sealed class MatchStateMachine
{
    private readonly GameRules _rules;
    private readonly FieldControlMode _mode;
    private bool _flowerUnlocked;
    private bool _nectarAllUnlocked;
    private bool _final20Sent;

    public MatchStateMachine(GameRules rules, int tickRate = SimClock.DefaultTickRate, FieldControlMode mode = FieldControlMode.Assisted)
    {
        _rules = rules;
        _mode = mode;
        TickRate = tickRate;
        AutoTicks = SimClock.TicksFor(rules.AutoSeconds, tickRate);
        TransitionTicks = SimClock.TicksFor(rules.TransitionSeconds, tickRate);
        TeleopTicks = SimClock.TicksFor(rules.TeleopSeconds, tickRate);
        FlowerUnlockRemainingTicks = SimClock.TicksFor(rules.FlowerUnlockRemainingSeconds, tickRate);
        NectarAllRemainingTicks = SimClock.TicksFor(rules.NectarAllEntryRemainingSeconds, tickRate);
        Final20RemainingTicks = SimClock.TicksFor(rules.FinalWarningRemainingSeconds, tickRate);
    }

    public int TickRate { get; }
    public FieldControlMode Mode => _mode;
    public long AutoTicks { get; }
    public long TransitionTicks { get; }
    public long TeleopTicks { get; }
    public long FlowerUnlockRemainingTicks { get; }
    public long NectarAllRemainingTicks { get; }
    public long Final20RemainingTicks { get; }

    public MatchPhase Phase { get; private set; } = MatchPhase.PreMatch;
    public long CurrentTick { get; private set; }
    public long StartTick { get; private set; } = -1;
    public bool OutputsEnabled { get; private set; }

    public long AutoEndTick => StartTick + AutoTicks;
    public long TeleopStartTick => AutoEndTick + TransitionTicks;
    public long MatchEndTick => TeleopStartTick + TeleopTicks;

    /// <summary>Auto, Transition or Teleop: the MATCH clock is running.</summary>
    public bool IsRunning => Phase is MatchPhase.Auto or MatchPhase.Transition or MatchPhase.Teleop;

    /// <summary>The field timer has reached 0:00 (MatchEnd and later, not Aborted).</summary>
    public bool IsAfterEnd => Phase is MatchPhase.MatchEnd or MatchPhase.PostMatchSettle or MatchPhase.Results;

    /// <summary>Achievements up to this tick belong to AUTO (§10.5 B: complete before TELEOP starts).</summary>
    public MatchPeriod PeriodAt(long tick) => StartTick >= 0 && tick < TeleopStartTick ? MatchPeriod.Auto : MatchPeriod.Teleop;

    public IReadOnlyList<MatchEvent> Start(long tick)
    {
        if (Phase != MatchPhase.PreMatch)
        {
            throw new InvalidOperationException($"cannot start a match in phase {Phase}");
        }
        StartTick = tick;
        CurrentTick = tick;
        var events = new List<MatchEvent>();
        SetPhase(events, tick, MatchPhase.Auto);
        events.Add(Cue(tick, AudioCueKind.MatchStart, "match_start", "2:30"));
        SetOutputs(events, tick, true);
        return events;
    }

    /// <summary>Moves the clock to <paramref name="tick"/>, emitting every boundary event in time order with its exact tick.</summary>
    public IReadOnlyList<MatchEvent> Advance(long tick)
    {
        if (tick < CurrentTick)
        {
            throw new ArgumentException($"time cannot go back ({tick} < {CurrentTick})");
        }
        var events = new List<MatchEvent>();
        while (NextBoundary() is { } next && next <= tick)
        {
            ProcessBoundary(events, next);
        }
        CurrentTick = tick;
        return events;
    }

    /// <summary>Everything has come to rest after the MATCH: final results can be computed.</summary>
    public IReadOnlyList<MatchEvent> FinishSettle(long tick)
    {
        if (Phase != MatchPhase.PostMatchSettle)
        {
            throw new InvalidOperationException($"cannot finish settling in phase {Phase}");
        }
        var events = new List<MatchEvent>();
        SetPhase(events, tick, MatchPhase.Results);
        return events;
    }

    /// <summary>MATCH stopped by field staff (Table 9-1 "Foghorn"); robots are disabled.</summary>
    public IReadOnlyList<MatchEvent> Abort(long tick)
    {
        var events = new List<MatchEvent>();
        if (Phase is MatchPhase.Results or MatchPhase.Aborted)
        {
            return events;
        }
        SetPhase(events, tick, MatchPhase.Aborted);
        events.Add(Cue(tick, AudioCueKind.MatchStopped, "match_stopped", "N/A"));
        SetOutputs(events, tick, false);
        return events;
    }

    /// <summary>
    /// Ticks shown by the main field timer: 2:30→2:00 in AUTO, frozen at 2:00 in the transition, 2:00→0:00 in TELEOP.
    /// This is the "time remaining in the MATCH" used by G410 and G426.
    /// </summary>
    public long TimerRemainingTicks(long tick)
    {
        if (StartTick < 0 || Phase == MatchPhase.PreMatch)
        {
            return AutoTicks + TeleopTicks;
        }
        if (tick < AutoEndTick)
        {
            return AutoEndTick - tick + TeleopTicks;
        }
        if (tick < TeleopStartTick)
        {
            return TeleopTicks;
        }
        return Math.Max(0, MatchEndTick - tick);
    }

    /// <summary>Main field timer text, m:ss, counting down with whole seconds rounded up.</summary>
    public string TimerDisplay(long tick) => Format(TimerRemainingTicks(tick));

    /// <summary>Separate transition countdown (0:08 … 0:01), or null outside the transition.</summary>
    public string? TransitionCountdownDisplay(long tick) =>
        StartTick >= 0 && tick >= AutoEndTick && tick < TeleopStartTick ? Format(TeleopStartTick - tick) : null;

    /// <summary>FLOWER scoring allowed (G410: last 60 s of the MATCH).</summary>
    public bool FlowerScoringOpen(long tick) =>
        StartTick >= 0 && tick >= TeleopStartTick && TimerRemainingTicks(tick) <= FlowerUnlockRemainingTicks;

    /// <summary>Humans may enter all remaining NECTAR (G426.B: 60 s or less remain).</summary>
    public bool NectarAllEntryOpen(long tick) =>
        StartTick >= 0 && tick >= TeleopStartTick && TimerRemainingTicks(tick) <= NectarAllRemainingTicks;

    private long? NextBoundary() => Phase switch
    {
        MatchPhase.Auto => AutoEndTick,
        MatchPhase.Transition => TeleopStartTick,
        MatchPhase.Teleop => new[]
            {
                _flowerUnlocked ? long.MaxValue : MatchEndTick - FlowerUnlockRemainingTicks,
                _nectarAllUnlocked ? long.MaxValue : MatchEndTick - NectarAllRemainingTicks,
                _final20Sent ? long.MaxValue : MatchEndTick - Final20RemainingTicks,
                MatchEndTick,
            }.Min(),
        MatchPhase.MatchEnd => MatchEndTick + 1,
        _ => null,
    };

    private void ProcessBoundary(List<MatchEvent> events, long t)
    {
        switch (Phase)
        {
            case MatchPhase.Auto:
                events.Add(Cue(t, AudioCueKind.AutoEnd, "auto_end", "2:00"));
                events.Add(new StopAutoOpMode(t));
                SetPhase(events, t, MatchPhase.Transition);
                if (_mode == FieldControlMode.Assisted)
                {
                    SetOutputs(events, t, false);
                }
                events.Add(Cue(t, AudioCueKind.TransitionCountdown, "transition_countdown", "0:08-0:01"));
                break;
            case MatchPhase.Transition:
                SetPhase(events, t, MatchPhase.Teleop);
                events.Add(Cue(t, AudioCueKind.TeleopStart, "teleop_start", "2:00"));
                SetOutputs(events, t, true);
                break;
            case MatchPhase.Teleop:
                if (!_flowerUnlocked && t == MatchEndTick - FlowerUnlockRemainingTicks)
                {
                    _flowerUnlocked = true;
                    events.Add(new FlowerScoringUnlocked(t));
                    events.Add(Cue(t, AudioCueKind.FlowerUnlock, "flower_unlock", "1:00"));
                }
                if (!_nectarAllUnlocked && t == MatchEndTick - NectarAllRemainingTicks)
                {
                    _nectarAllUnlocked = true;
                    events.Add(new NectarAllEntryUnlocked(t));
                }
                if (!_final20Sent && t == MatchEndTick - Final20RemainingTicks)
                {
                    _final20Sent = true;
                    events.Add(Cue(t, AudioCueKind.Final20, "final_20", "0:20"));
                }
                if (t == MatchEndTick)
                {
                    events.Add(Cue(t, AudioCueKind.MatchEnd, "match_end", "0:00"));
                    SetPhase(events, t, MatchPhase.MatchEnd);
                    if (_mode == FieldControlMode.Assisted)
                    {
                        SetOutputs(events, t, false);
                    }
                }
                break;
            case MatchPhase.MatchEnd:
                SetPhase(events, t, MatchPhase.PostMatchSettle);
                break;
        }
    }

    private void SetPhase(List<MatchEvent> events, long tick, MatchPhase to)
    {
        events.Add(new PhaseChanged(tick, Phase, to));
        Phase = to;
    }

    private void SetOutputs(List<MatchEvent> events, long tick, bool enabled)
    {
        if (OutputsEnabled == enabled)
        {
            return;
        }
        OutputsEnabled = enabled;
        events.Add(new RobotOutputsChanged(tick, enabled));
    }

    private AudioCue Cue(long tick, AudioCueKind kind, string eventKey, string timer) =>
        new(tick, kind, _rules.AudioCueText(eventKey), timer);

    private string Format(long ticks)
    {
        var seconds = (ticks + TickRate - 1) / TickRate; // whole seconds, rounded up
        return $"{seconds / 60}:{seconds % 60:00}";
    }
}
