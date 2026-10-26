using Sim.Game.Fouls;
using Sim.Game.Scoring;
using Sim.Runner;

namespace MatchTests;

/// <summary>Match setups, records, history and replay files survive a round trip; CSV follows the score sheet.</summary>
public class RecordAndReplayTests
{
    private static string TempDir()
    {
        var dir = Path.Combine(Path.GetTempPath(), "ftcsim-match-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private static MatchSetup Setup() => new(
    [
        new StationSetup(Station.Red1, StationKind.Code, "robodojo_23205", "23205", "/tmp/teamcode.jar", "Auto", "RoboDojo TeleOp"),
        new StationSetup(Station.Red2, StationKind.Placeholder),
        new StationSetup(Station.Blue1, StationKind.Gamepad, "gobilda_starter", "Blue, Inc."),
        new StationSetup(Station.Blue2, StationKind.Empty),
    ], FoulMode.Assist, AutoHumanPlayer: false, Seed: 42, Label: "Scrim 1");

    [Fact]
    public void Setup_round_trips_and_reports_problems()
    {
        var setup = Setup();
        var back = MatchSetup.FromJson(setup.ToJson());
        Assert.Equal(setup.Stations, back.Stations);
        Assert.Equal(FoulMode.Assist, back.FoulMode);
        Assert.Equal([Station.Red1, Station.Blue1, Station.Red2], back.Robots.Select(s => s.Station));   // §10.3.4 order
        Assert.Empty(setup.Problems(_ => true));
        var bad = setup with { Stations = [.. setup.Stations, new StationSetup(Station.Red1, StationKind.Code)] };
        Assert.Equal(2, bad.Problems(_ => true).Count);   // duplicate station + code without a jar
    }

    [Fact]
    public void Records_round_trip_through_the_history_and_export_csv()
    {
        var dir = TempDir();
        try
        {
            var rules = GameRules.Load(Sim.Core.Config.RepoPaths.RulesFile());
            var scoring = new List<ScoringEvent>
            {
                new(100, Alliance.Red, ScoreCategory.Leave, MatchPeriod.Auto, rules.LeaveAuto, null, 1, "", null, "§10.5.4"),
                new(200, Alliance.Blue, ScoreCategory.GardenElement, MatchPeriod.Teleop, 1, 7, null, "garden_blue", 2, "§10.5.3"),
            };
            var fouls = new List<FoulRecord> { new(1, 150, "G421", Alliance.Blue, 2, Penalty.Major, 1, Card.None, false, "pin 3.2 s", "§11.4", FoulStatus.Applied) };
            var score = ScoreSheet.Compute(scoring, fouls, rules);
            var history = new MatchHistory(dir);
            var id = MatchHistory.NewId(new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc));
            var record = new MatchRecord(id, "Scrim 1", new DateTime(2026, 10, 26, 12, 0, 0, DateTimeKind.Utc), Setup(), "completed", score,
                fouls, scoring, [new RobotStats(Station.Red1, "23205", "robodojo_23205", 12.5, 6, 5, 3, 13.1, 11.9, 12.4, 22.5, 0)],
                240, 120, 38000, id + ".ftcreplay", 0xDEADBEEF, "test");
            history.Save(record);
            File.WriteAllText(history.ReplayPathFor(id), "x");
            var list = history.List();
            Assert.Single(list);
            Assert.Equal($"Red wins {score.Red.Total}-{score.Blue.Total}", list[0].Summary);
            Assert.NotNull(list[0].ReplayPath);
            var back = history.Load(id);
            Assert.Equal(record.Score, back.Score);
            Assert.Equal(record.Fouls, back.Fouls);
            Assert.Equal(record.Robots, back.Robots);
            var csv = File.ReadAllLines(history.ExportCsv(back));
            Assert.StartsWith("match,alliance,total,", csv[0]);
            Assert.Contains(csv, l => l.StartsWith($"{id},red,{score.Red.Total},", StringComparison.Ordinal));
            Assert.Contains(csv, l => l.StartsWith($"{id},red_1,23205,robodojo_23205,12.5,6,5,3,0.6,", StringComparison.Ordinal));
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Replay_files_keep_every_input_pose_and_checkpoint()
    {
        var dir = TempDir();
        try
        {
            var path = Path.Combine(dir, "m.ftcreplay");
            var header = new ReplayHeader(ReplayFormat.Version, "test", 240, "field", Setup(), 10, [Station.Red1, Station.Blue1], ["pollen", "nectar_red"]);
            MotorCommand Motor(double power) => new("1:0", "RUN_USING_ENCODER", power, true, 1200, 0, "BRAKE", 10, true, 1, [30, 0, 0, 13], double.NaN);
            TickResult Result(long tick, string telemetry) => new(tick * 1000, RunnerState.Running, null, [Motor(tick / 10.0)],
                [new ServoCommand("0:1", 1500 + tick, true)], tick, [telemetry], [], [new SensorCommand("pinpoint", "reset", [1, 2])]);
            var pad = new GamepadState(0.5f, -1f, 0, 0, 0.25f, 1f, GamepadButtons.A | GamepadButtons.DpadUp, 3);
            using (var w = new ReplayWriter(path, header))
            {
                for (long t = 10; t < 13; t++)
                {
                    var events = t == 10 ? new List<ReplayEvent> { new(t, ReplayEventKind.MatchStart), new(t, ReplayEventKind.RunnerSessionBegin, Station.Red1) } : [];
                    w.Tick(t, [new StationTick(Station.Red1, DriveKind.Runner, Result(t, t < 12 ? "same" : "changed")),
                               new StationTick(Station.Blue1, DriveKind.Gamepads, null, pad, GamepadState.Neutral),
                               new StationTick(Station.Red2, DriveKind.None)], events);
                    w.Pose(new PoseFrame(t, [new BodyPose(1, 2, 3, 0, 0, 0, 1)], [new BodyPose(t, 0, 0, 0, 0, 0, 1)], [new BodyPose(0, 1, 0, 0.5f, 0, 0, 0.866f)], 3, 0, "2:30"));
                }
                w.Checkpoint(new Checkpoint(12, 99));
                w.Close(new ReplayFooter(13, 1234, "completed"));
            }
            using var r = new ReplayReader(path);
            Assert.Equal(header.Setup.Stations, r.Header.Setup.Stations);
            var frames = new List<PoseFrame>();
            var ticks = r.Ticks(frames.Add).ToList();
            Assert.Equal(3, ticks.Count);
            Assert.Equal([ReplayEventKind.MatchStart, ReplayEventKind.RunnerSessionBegin], ticks[0].Events.Select(e => e.Kind));
            Assert.Equal(Station.Red1, ticks[0].Events[1].Station);
            var red = ticks[1].Stations[0].Result!;
            Assert.Equal(1.1, red.Motors[0].Power, 12);
            Assert.Equal([30.0, 0, 0, 13], red.Motors[0].VelocityPidf!);
            Assert.True(double.IsNaN(red.Motors[0].PositionP));
            Assert.Equal(1511, red.Servos[0].PulseUs);
            Assert.Equal(["same"], red.Telemetry);                          // unchanged telemetry is carried over
            Assert.Equal(["changed"], ticks[2].Stations[0].Result!.Telemetry);
            Assert.Equal([1.0, 2.0], red.SensorCommands[0].Args);
            Assert.Equal(pad, ticks[1].Stations[1].Gamepad1);
            Assert.Equal(DriveKind.None, ticks[2].Stations[2].Kind);
            Assert.Equal(3, frames.Count);
            Assert.Equal(12f, frames[2].Elements[0].X);
            Assert.Equal(0.866f, frames[0].FieldParts[0].Qw);
            Assert.Equal([new Checkpoint(12, 99)], r.Checkpoints);
            Assert.Equal(new ReplayFooter(13, 1234, "completed"), r.Footer);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }
    }

    [Fact]
    public void Stats_track_distance_battery_and_scoring()
    {
        var t = new RobotStatsTracker(new StationSetup(Station.Blue2, StationKind.Gamepad, Team: "B2"), "gobilda_starter");
        t.Update(0, 0, 13.2, 1);
        t.Update(3, 4, 12.1, 30);
        t.Update(3, 4, 12.6, 2);
        t.Intake();
        t.Launch();
        t.Launch();
        var stats = t.Finish([new ScoringEvent(1, Alliance.Blue, ScoreCategory.GardenElement, MatchPeriod.Teleop, 1, 3, null, "", 4, "")], []);
        Assert.Equal(5, stats.DistanceM, 6);
        Assert.Equal((13.2, 12.1, 12.6, 30.0), (stats.BatteryStartV, stats.BatteryMinV, stats.BatteryEndV, stats.PeakCurrentA));
        Assert.Equal((1, 2, 1), (stats.Intakes, stats.Launches, stats.Scored));
        Assert.Equal(0.5, stats.ShotAccuracy);
    }
}
