using Sim.Core.Config;
using Sim.Runner;

namespace Sim.DriverStation;

/// <summary>What the Driver Station asks of its robot. The app implements it with the runner; tests use fakes.</summary>
public interface IRobotLink
{
    void InitOpMode(OpModeInfo opMode);

    void StartOpMode();

    void StopOpMode();
}

/// <summary>Driver Station button states (spec 08 "INIT → PLAY → STOP").</summary>
public enum DsPhase
{
    /// <summary>No OpMode selected: only the selectors work.</summary>
    NoOpMode,

    /// <summary>An OpMode is selected and stopped: INIT is enabled.</summary>
    Ready,

    /// <summary>INIT pressed, init code running: PLAY and STOP are enabled.</summary>
    Initialized,

    /// <summary>PLAY pressed: STOP is enabled; an Autonomous may count down 30 s.</summary>
    Running,

    /// <summary>STOP pressed, waiting for the robot to report that the OpMode ended: nothing is enabled.</summary>
    Stopping,
}

/// <summary>
/// The Driver Station app's control logic (spec 08): TeleOp and Autonomous lists, INIT/PLAY/STOP availability, the
/// autonomous timer (counts down the autonomous period from PLAY and presses STOP at 0; on by default for Autonomous
/// OpModes, can be switched off before PLAY), preselectTeleOp after an Autonomous ends, and the robot's error banner.
/// Time is simulation time in nanoseconds, so a run is deterministic.
/// </summary>
public sealed class DriverStationApp
{
    private readonly IRobotLink _robot;
    private readonly long _autonomousNanos;
    private List<OpModeInfo> _teleOps = [];
    private List<OpModeInfo> _autonomous = [];
    private long? _timerEndNanos;

    /// <param name="autonomousSeconds">Length of the autonomous timer (rules_biobuzz.yaml timing.auto, §10.4).</param>
    public DriverStationApp(IRobotLink robot, double autonomousSeconds)
    {
        _robot = robot;
        _autonomousNanos = (long)Math.Round(autonomousSeconds * 1e9);
    }

    /// <summary>Driver Station with the autonomous period from the rules config.</summary>
    public static DriverStationApp WithRules(IRobotLink robot, RulesConfig rules) => new(robot, rules.Get("timing.auto").AsDouble());

    public IReadOnlyList<OpModeInfo> TeleOps => _teleOps;
    public IReadOnlyList<OpModeInfo> Autonomous => _autonomous;
    public OpModeInfo? Selected { get; private set; }
    public DsPhase Phase { get; private set; } = DsPhase.NoOpMode;

    /// <summary>The 30 s timer toggle; set to on whenever an Autonomous OpMode is selected.</summary>
    public bool AutonomousTimerEnabled { get; set; }

    /// <summary>Error text of the last OpMode run (red banner), cleared by the next INIT.</summary>
    public string? Error { get; private set; }

    public bool CanSelect => Phase is DsPhase.NoOpMode or DsPhase.Ready;
    public bool CanInit => Phase == DsPhase.Ready;
    public bool CanPlay => Phase == DsPhase.Initialized;
    public bool CanStop => Phase is DsPhase.Initialized or DsPhase.Running;

    /// <summary>The OpModes the robot registered. The selection is kept if the OpMode still exists.</summary>
    public void SetOpModes(IEnumerable<OpModeInfo> opModes)
    {
        var all = opModes.ToList();
        _teleOps = Sorted(all.Where(o => !o.Autonomous));
        _autonomous = Sorted(all.Where(o => o.Autonomous));
        if (Selected is not null && !all.Contains(Selected) && CanSelect)
        {
            Selected = null;
            Phase = DsPhase.NoOpMode;
        }
    }

    public void Select(OpModeInfo opMode)
    {
        if (!CanSelect)
        {
            throw new InvalidOperationException("an OpMode is active; press STOP first");
        }
        if (!_teleOps.Contains(opMode) && !_autonomous.Contains(opMode))
        {
            throw new ArgumentException($"OpMode '{opMode.Name}' is not registered on the robot");
        }
        Selected = opMode;
        AutonomousTimerEnabled = opMode.Autonomous;
        Phase = DsPhase.Ready;
    }

    public void Init()
    {
        Require(CanInit, "INIT");
        Error = null;
        _robot.InitOpMode(Selected!);
        Phase = DsPhase.Initialized;
    }

    public void Play(long nowNanos)
    {
        Require(CanPlay, "PLAY");
        _robot.StartOpMode();
        Phase = DsPhase.Running;
        _timerEndNanos = Selected!.Autonomous && AutonomousTimerEnabled ? nowNanos + _autonomousNanos : null;
    }

    public void Stop()
    {
        Require(CanStop, "STOP");
        _robot.StopOpMode();
        _timerEndNanos = null;
        Phase = DsPhase.Stopping;
    }

    /// <summary>Seconds left on the autonomous timer, or null when it is not running.</summary>
    public double? TimerRemainingSeconds(long nowNanos) =>
        _timerEndNanos is { } end ? Math.Max(0, end - nowNanos) / 1e9 : null;

    /// <summary>Called every simulation step with the robot's report: OpModes that end or crash, the timer reaching 0.</summary>
    public void Update(RunnerState robotState, string? robotError, long nowNanos)
    {
        if (Phase is DsPhase.Initialized or DsPhase.Running or DsPhase.Stopping
            && robotState is RunnerState.Stopped or RunnerState.Crashed or RunnerState.Idle)
        {
            Error = robotError;
            EndRun();
            return;
        }
        if (Phase == DsPhase.Running && _timerEndNanos is { } end && nowNanos >= end)
        {
            Stop();
        }
    }

    /// <summary>The robot restarted (Restart Robot, new code): any run is over and the error is cleared.</summary>
    public void RobotRestarted()
    {
        _timerEndNanos = null;
        Error = null;
        Phase = Selected is null ? DsPhase.NoOpMode : DsPhase.Ready;
    }

    private void EndRun()
    {
        _timerEndNanos = null;
        var ended = Selected!;
        Phase = DsPhase.Ready;
        if (ended.Autonomous && ended.PreselectTeleOp.Length > 0
            && _teleOps.FirstOrDefault(t => t.Name == ended.PreselectTeleOp) is { } teleOp)
        {
            Select(teleOp);
        }
    }

    private void Require(bool allowed, string button)
    {
        if (!allowed)
        {
            throw new InvalidOperationException($"{button} is not available while the Driver Station is {Phase}");
        }
    }

    /// <summary>
    /// Display order: OpModes without a group first, then groups alphabetically, names alphabetically within a group
    /// (case-insensitive). ASSUMED to match the DS app, whose sorting code is not part of the SDK.
    /// </summary>
    private static List<OpModeInfo> Sorted(IEnumerable<OpModeInfo> opModes) => opModes
        .OrderBy(o => IsDefaultGroup(o.Group) ? 0 : 1)
        .ThenBy(o => o.Group, StringComparer.OrdinalIgnoreCase)
        .ThenBy(o => o.Name, StringComparer.OrdinalIgnoreCase)
        .ThenBy(o => o.ClassName, StringComparer.Ordinal)
        .ToList();

    /// <summary>The SDK's default group name is "$$$$$$$" (OpModeMeta.DefaultGroup); empty is treated the same.</summary>
    private static bool IsDefaultGroup(string group) => group.Length == 0 || group == "$$$$$$$";
}
