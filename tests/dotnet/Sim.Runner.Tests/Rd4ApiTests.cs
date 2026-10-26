using System.Globalization;
using System.Numerics;
using Sim.Core.Config;

namespace RunnerTests;

/// <summary>
/// The APIs the RoboDojo #23205 RD4 team code needs (D-039; tests/compat/rd4_apis): Panels, FTC Dashboard @Config,
/// Limelight 3A, AnalogInput, TypeConversion, a team-owned register-level Pinpoint driver on the simulated Pinpoint,
/// RobotLog.ee with an exception, Android TextUtils/Environment and OpModeManagerImpl listeners.
/// </summary>
[Collection("runner")]
public class Rd4ApiTests
{
    private static readonly Lazy<string> Jar = new(() =>
    {
        var source = RepoPaths.Find(Path.Combine("tests", "compat", "rd4_apis", "TeamCode", "src", "main", "java"));
        var build = Path.Combine(Path.GetTempPath(), "ftcsim-tests", "rd4_apis");
        var result = TeamCodeBuilder.Build(source, build);
        Assert.True(result.Success, string.Join("\n", result.Errors.Select(e => $"{e.File}:{e.Line} {e.Message}")) + result.Output);
        return result.JarPath!;
    });

    private static string Value(TickResult r, string caption) =>
        r.Telemetry.Last(l => l.StartsWith(caption + ": ", StringComparison.Ordinal))[(caption.Length + 2)..];

    [Fact]
    public void Team_code_with_panels_limelight_and_its_own_pinpoint_driver_runs()
    {
        using var robot = SensorRobot.Start(Jar.Value);
        robot.Init("RD4 API check");
        var init = robot.Run(0.5);
        Assert.True(init.State is RunnerState.Init or RunnerState.Running, init.Error);
        Assert.Contains(init.Telemetry, l => l.StartsWith("id : "));
        robot.Session.Start();
        robot.Run(0.4);
        robot.Move(new Vector3(0, 0, -0.5f), 0, 1.5);   // pushed 0.5 m forward
        var r = robot.Run(0.3);
        Assert.True(r.State == RunnerState.Running, r.Error);
        Assert.Equal(500, double.Parse(Value(r, "x mm"), CultureInfo.InvariantCulture), 10);   // read from raw Pinpoint registers
        Assert.Equal("false", Value(r, "limelight valid"));
        Assert.Equal("0.0", Value(r, "analog V"));
        Assert.Contains(r.Telemetry, l => l == "active RD4 API check");

        robot.Session.Stop();
        robot.Run(0.2);
        var log = Path.Combine(robot.Session.WorkDir, "sdcard", "FTCLogs", "api_check.csv");
        Assert.True(File.Exists(log), log);
        var lines = File.ReadAllLines(log);
        Assert.Equal("t,x", lines[0]);
        Assert.Contains("-1.0,0.0", lines);          // START heard by the listener
        Assert.Equal("stopped", lines[^1]);          // closed by the post-stop listener
    }
}
