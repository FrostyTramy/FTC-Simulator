using Sim.Core.Events;

namespace Sim.Game.Match;

public enum MatchPhase
{
    PreMatch,
    Auto,
    Transition,
    Teleop,

    /// <summary>The tick at which the field timer reaches 0:00 (TELEOP PARK assessed here).</summary>
    MatchEnd,

    /// <summary>Waiting until all elements and robots are at rest (§10.5 A, C, D, E).</summary>
    PostMatchSettle,
    Results,

    /// <summary>MATCH stopped (Table 9-1 "Foghorn").</summary>
    Aborted,
}

/// <summary>
/// How the sim treats robot outputs between periods. On a real FTC field nothing disables robots: the DS
/// 30-second timer stops the AUTO OpMode and G403/G404 forbid powered movement. <see cref="Assisted"/>
/// (default, spec 03) also disables outputs in the transition and after 0:00; <see cref="Realistic"/> only
/// stops the AUTO OpMode and leaves G403/G404 to the foul detectors.
/// </summary>
public enum FieldControlMode
{
    Assisted,
    Realistic,
}

public enum AudioCueKind
{
    MatchStart,
    AutoEnd,
    TransitionCountdown,
    TeleopStart,
    FlowerUnlock,
    Final20,
    MatchEnd,
    MatchStopped,
}

public abstract record MatchEvent(long Tick) : ISimEvent
{
    public abstract string Type { get; }

    public virtual void AddTo(StableHasher hasher)
    {
    }
}

public sealed record PhaseChanged(long Tick, MatchPhase From, MatchPhase To) : MatchEvent(Tick)
{
    public override string Type => "PhaseChanged";

    public override void AddTo(StableHasher hasher) => hasher.Add((int)From).Add((int)To);
}

/// <summary>Audio cue of Table 9-1 p79. <see cref="Timer"/> is the field timer value printed in the table.</summary>
public sealed record AudioCue(long Tick, AudioCueKind Kind, string Text, string Timer) : MatchEvent(Tick)
{
    public override string Type => "AudioCue";

    public override void AddTo(StableHasher hasher) => hasher.Add((int)Kind).Add(Text).Add(Timer);
}

/// <summary>The AUTO OpMode is stopped, as the Driver Station's 30-second timer does (§10.3.3 A, G305).</summary>
public sealed record StopAutoOpMode(long Tick) : MatchEvent(Tick)
{
    public override string Type => "StopAutoOpMode";
}

public sealed record RobotOutputsChanged(long Tick, bool Enabled) : MatchEvent(Tick)
{
    public override string Type => "RobotOutputsChanged";

    public override void AddTo(StableHasher hasher) => hasher.Add(Enabled);
}

/// <summary>One minute remains: FLOWER scoring and NECTAR entry into FLOWERS are allowed (G410, Table 9-1 "Chime").</summary>
public sealed record FlowerScoringUnlocked(long Tick) : MatchEvent(Tick)
{
    public override string Type => "FlowerScoringUnlocked";
}

/// <summary>60 s or less remain: humans may enter all remaining NECTAR (G426.B).</summary>
public sealed record NectarAllEntryUnlocked(long Tick) : MatchEvent(Tick)
{
    public override string Type => "NectarAllEntryUnlocked";
}
