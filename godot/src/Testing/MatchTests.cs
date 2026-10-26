using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using Godot;
using Sim.App;
using Sim.Field;
using Sim.Game;
using Sim.Game.Match;
using Sim.Game.Scoring;
using Sim.Match;
using Sim.Robot;
using Sim.Runner;

namespace Sim.Testing;

/// <summary>
/// Phase 10 acceptance, part 1 (<c>--match-test [OUT_DIR]</c>, headless): a full 2v2 MATCH with four robots — Red 1 runs
/// the goBILDA example AUTO and TeleOp in the runner JVM, Blue 1 the RoboDojo TeleOp, Red 2 is a scripted gamepad
/// driver, Blue 2 a placeholder — with the automatic human players and Strict fouls. Checks the MATCH flow, the score
/// breakdown and the robots' statistics, then saves the record and the replay to OUT_DIR (default reports/match) for
/// part 2 (<see cref="VerifyReplay"/>). Exit code 0 when every check passes.
/// </summary>
public partial class ScriptedMatch : Node
{
    private const double PreMatchSeconds = 1.0;
    private const double TimeoutSeconds = 240;
    private readonly string _layout;
    private readonly string _outDir;
    private readonly ScriptedGamepads _red2 = new();
    private MatchSession _session = null!;
    private long _tick;
    private ulong _startUsec;

    public ScriptedMatch(string layout, string? outDir)
    {
        _layout = layout;
        _outDir = Path.GetFullPath(outDir ?? RepoPaths.Reports("match"));
    }

    public override void _Ready()
    {
        var physics = PhysicsConfig.Load(RepoPaths.Config("physics.yaml"));
        var layoutPath = RepoPaths.Layout(_layout);
        var field = Main.LoadField(this, layoutPath, physics);
        var starter = TeamCodeJars.DefaultFor("gobilda_starter") ?? throw new InvalidOperationException("starter TeamCode jar missing (scripts/test_java.ps1)");
        var robodojo = TeamCodeJars.DefaultFor("robodojo_23205") ?? throw new InvalidOperationException("RoboDojo TeamCode did not build");
        var setup = new MatchSetup(
        [
            new StationSetup(Station.Red1, StationKind.Code, "gobilda_starter", "SIM 1", starter, "Mec BioBuzz StarterBot Auto"),
            new StationSetup(Station.Blue1, StationKind.Code, "robodojo_23205", "23205", robodojo, null, "RoboDojo TeleOp"),
            new StationSetup(Station.Red2, StationKind.Gamepad, "gobilda_starter", "SIM 2"),
            new StationSetup(Station.Blue2, StationKind.Placeholder, "gobilda_starter", "SIM 4"),
        ], Label: "Phase 10 acceptance match");
        Directory.CreateDirectory(_outDir);
        _session = new MatchSession(field, setup, physics, null, _outDir);
        _session.GamepadSources[Station.Red2] = _red2;
        AddChild(_session);
        _startUsec = Time.GetTicksUsec();
        GD.Print($"match test: layout {layoutPath}{(RepoPaths.IsStandIn(layoutPath) ? " (stand-in)" : "")}");
    }

    public override void _PhysicsProcess(double delta)
    {
        var rate = Engine.PhysicsTicksPerSecond;
        if (_tick == (long)(PreMatchSeconds * rate))
        {
            GD.Print($"setup problems before the start: {_session.SetupProblems.Count}");
            _session.StartMatch();
        }
        _red2.Drive(_session.Director.Match, _session.Controller.Tick);
        if (_session.LastRecord is { } record)
        {
            Report(record);
            return;
        }
        if (++_tick > TimeoutSeconds * rate)
        {
            GD.PrintErr($"match test: no result after {TimeoutSeconds} s (phase {_session.Director.Match.Phase})");
            GetTree().Quit(1);
        }
    }

    private void Report(MatchRecord record)
    {
        SetPhysicsProcess(false);
        var checks = new List<(bool Ok, string What)>();
        void Check(bool ok, string what) => checks.Add((ok, what));
        var log = _session.Director.Log.Events;
        var phases = log.OfType<PhaseChanged>().Select(p => p.To).ToList();
        Check(record.Outcome == "completed", $"outcome {record.Outcome}");
        Check(phases.SequenceEqual([MatchPhase.Auto, MatchPhase.Transition, MatchPhase.Teleop, MatchPhase.MatchEnd, MatchPhase.PostMatchSettle, MatchPhase.Results]),
            "phases " + string.Join(" → ", phases));
        Check(_session.SetupProblems.Count == 0 && record.DurationTicks > 0, "G304 setup legal, MATCH started" +
            string.Concat(_session.SetupProblems.Select(p => $" [{p.RobotId} {p.Rule}: {p.Message}]")));
        foreach (var a in new[] { record.Score.Red, record.Score.Blue })
        {
            Check(a.Total == a.AutoPoints + a.TeleopPoints + a.PenaltyPoints, $"{a.Alliance} total {a.Total} = auto {a.AutoPoints} + teleop {a.TeleopPoints} + penalties {a.PenaltyPoints}");
        }
        var leave = record.Scoring.Where(e => e.Category == ScoreCategory.Leave).ToList();
        Check(leave.Any(e => e.RobotId == Station.Red1.RobotId()) && record.Score.Red.LeavePoints >= _session.Controller.Rules.LeaveAuto,
            $"Red 1 (example AUTO) scored LEAVE ({record.Score.Red.LeavePoints} points)");
        Check(leave.All(e => e.RobotId == Station.Red1.RobotId()), "only Red 1 moved in AUTO (the others had no AUTO or were placeholders)");
        var stats = record.Robots.ToDictionary(r => r.Station);
        Check(stats[Station.Red1].Launches >= 1, $"Red 1 launched its pre-loads ({stats[Station.Red1].Launches})");
        Check(stats[Station.Red2].DistanceM > 0.5, $"Red 2 (scripted gamepad) drove {stats[Station.Red2].DistanceM:0.00} m in TELEOP");
        Check(stats[Station.Blue2].DistanceM < 0.05, $"Blue 2 (placeholder) stayed ({stats[Station.Blue2].DistanceM:0.000} m)");
        Check(stats.Values.All(s => s.BatteryMinV < s.BatteryStartV || s.Station == Station.Blue2), "batteries sag under load");
        var hp = _session.Director.HumanPlayer;
        Check(hp.Reserve(Alliance.Red) < _session.Controller.Rules.NectarInAllianceArea, $"automatic human players entered NECTAR (red reserve left {hp.Reserve(Alliance.Red)})");
        Check(record.Fouls.Count == 0, $"no fouls in a clean MATCH ({string.Join("; ", record.Fouls.Select(f => $"{f.Rule} {f.Evidence}"))})");
        Check(record.ReplayFile != null && File.Exists(Path.Combine(_outDir, record.ReplayFile)), $"replay saved ({record.ReplayFile})");
        var wall = (Time.GetTicksUsec() - _startUsec) / 1e6;
        foreach (var (ok, what) in checks)
        {
            GD.Print($"  {(ok ? "ok  " : "FAIL")} {what}");
        }
        GD.Print($"match test: {record.Summary}, {checks.Count(c => c.Ok)}/{checks.Count} checks, {record.DurationTicks / (double)record.TickRate:0} s MATCH in {wall:0} s wall");
        File.WriteAllText(Path.Combine(_outDir, "last_match.json"), JsonSerializer.Serialize(new
        {
            id = record.Id,
            summary = record.Summary,
            replay = record.ReplayFile is { } f ? Path.Combine(_outDir, f) : null,
            hash = record.StateHash.ToString("x16"),
            checks = checks.Select(c => new { ok = c.Ok, what = c.What }),
        }, new JsonSerializerOptions { WriteIndented = true }));
        GetTree().Quit(checks.All(c => c.Ok) ? 0 : 1);
    }

    /// <summary>The Red 2 driver: in TELEOP drives forward for 1.5 s, turns for 1 s, then waits.</summary>
    private sealed class ScriptedGamepads : IGamepadSource
    {
        public GamepadState Gamepad1 { get; private set; } = GamepadState.Neutral with { Id = 1 };
        public GamepadState Gamepad2 => GamepadState.Neutral;

        public void Drive(MatchStateMachine match, long tick)
        {
            var t = match.Phase == MatchPhase.Teleop ? (tick - match.TeleopStartTick) / (double)match.TickRate : -1;
            Gamepad1 = GamepadState.Neutral with
            {
                Id = 1,
                LeftStickY = t is >= 0 and < 1.5 ? -0.6f : 0f,
                RightStickX = t is >= 1.5 and < 2.5 ? 0.5f : 0f,
            };
        }
    }
}

/// <summary>
/// Phase 10 acceptance, part 2 (<c>--verify-replay FILE</c>, headless, a fresh process): rebuilds the recorded MATCH
/// from its setup and re-simulates it from the recorded inputs (runner STATEs, gamepads, human player and referee
/// actions). Every per-second checkpoint and the final hash (event log, score, every body) must match bit for bit.
/// </summary>
public partial class VerifyReplay : Node
{
    private readonly string _path;
    private readonly string _layout;
    private MatchSession _session = null!;
    private ulong _startUsec;

    public VerifyReplay(string path, string layout)
    {
        _path = Path.GetFullPath(path);
        _layout = layout;
    }

    public override void _Ready()
    {
        var physics = PhysicsConfig.Load(RepoPaths.Config("physics.yaml"));
        var field = Main.LoadField(this, RepoPaths.Layout(_layout), physics);
        _session = new MatchSession(field, new MatchSetup([]), physics, null, Path.GetTempPath(), SessionMode.Verify, _path);
        AddChild(_session);
        _startUsec = Time.GetTicksUsec();
    }

    public override void _PhysicsProcess(double delta)
    {
        if (!_session.ReplayFinished)
        {
            return;
        }
        SetPhysicsProcess(false);
        var footer = _session.RecordedFooter;
        var record = _session.LastRecord;
        var ok = footer != null && record != null && _session.Mismatches.Count == 0 && _session.RecordedCheckpoints > 0 && record.StateHash == footer.StateHash;
        GD.Print($"verify replay {Path.GetFileName(_path)}: {_session.RecordedCheckpoints} checkpoints, {_session.Mismatches.Count} mismatches" +
                 (_session.Mismatches.Count > 0 ? $" (first at tick {_session.Mismatches.Min()})" : "") +
                 $", final hash {record?.StateHash:x16} vs recorded {footer?.StateHash:x16}, {record?.Summary ?? "no result"}," +
                 $" {(Time.GetTicksUsec() - _startUsec) / 1e6:0} s wall");
        GD.Print(ok ? "REPLAY REPRODUCES THE MATCH" : "REPLAY DIFFERS FROM THE MATCH");
        GetTree().Quit(ok ? 0 : 1);
    }
}
