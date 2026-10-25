using Sim.Core.Config;

namespace DriverStationTests;

/// <summary>Driver Station states, autonomous timer, preselectTeleOp, OpMode lists, gamepad mapping and assignment.</summary>
public class DriverStationTests
{
    private const long Second = 1_000_000_000;
    private static readonly OpModeInfo Teleop = new("t.Teleop", "Mec BioBuzz StarterBot Teleop", "StarterBot", false, "");
    private static readonly OpModeInfo Auto = new("t.Auto", "Mec BioBuzz StarterBot Auto", "StarterBot", true, "Mec BioBuzz StarterBot Teleop");
    private static readonly OpModeInfo Plain = new("t.Plain", "Basic Drive", "", false, "");

    private sealed class FakeRobot : IRobotLink
    {
        public List<string> Calls { get; } = [];

        public void InitOpMode(OpModeInfo opMode) => Calls.Add("init " + opMode.Name);

        public void StartOpMode() => Calls.Add("start");

        public void StopOpMode() => Calls.Add("stop");
    }

    private static (DriverStationApp Ds, FakeRobot Robot) NewDs()
    {
        var robot = new FakeRobot();
        var ds = DriverStationApp.WithRules(robot, RulesConfig.Load(RepoPaths.RulesFile()));
        ds.SetOpModes([Teleop, Auto, Plain]);
        return (ds, robot);
    }

    [Fact]
    public void Lists_split_teleop_and_autonomous_with_ungrouped_opmodes_first()
    {
        var (ds, _) = NewDs();
        Assert.Equal([Plain, Teleop], ds.TeleOps);
        Assert.Equal([Auto], ds.Autonomous);
        Assert.Equal(DsPhase.NoOpMode, ds.Phase);
        Assert.False(ds.CanInit);
    }

    [Fact]
    public void Buttons_follow_init_play_stop()
    {
        var (ds, robot) = NewDs();
        ds.Select(Teleop);
        Assert.True(ds.CanInit && !ds.CanPlay && !ds.CanStop);
        ds.Init();
        Assert.True(!ds.CanInit && ds.CanPlay && ds.CanStop && !ds.CanSelect);
        Assert.Throws<InvalidOperationException>(() => ds.Select(Plain));
        ds.Play(0);
        Assert.True(!ds.CanPlay && ds.CanStop);
        Assert.Null(ds.TimerRemainingSeconds(0));   // TeleOps have no countdown
        ds.Stop();
        Assert.Equal(DsPhase.Stopping, ds.Phase);
        Assert.False(ds.CanInit);
        ds.Update(RunnerState.Stopping, null, Second);
        Assert.Equal(DsPhase.Stopping, ds.Phase);
        ds.Update(RunnerState.Stopped, null, 2 * Second);
        Assert.Equal(DsPhase.Ready, ds.Phase);
        Assert.Equal(["init Mec BioBuzz StarterBot Teleop", "start", "stop"], robot.Calls);
    }

    [Fact]
    public void Autonomous_timer_stops_after_the_autonomous_period_and_preselects_the_teleop()
    {
        var (ds, robot) = NewDs();
        ds.Select(Auto);
        Assert.True(ds.AutonomousTimerEnabled);
        ds.Init();
        ds.Play(5 * Second);
        Assert.Equal(30, ds.TimerRemainingSeconds(5 * Second));   // rules_biobuzz.yaml timing.auto (§10.4)
        ds.Update(RunnerState.Running, null, 34 * Second);
        Assert.Equal(DsPhase.Running, ds.Phase);
        Assert.Equal(1, ds.TimerRemainingSeconds(34 * Second)!.Value, 9);
        ds.Update(RunnerState.Running, null, 35 * Second);
        Assert.Equal(DsPhase.Stopping, ds.Phase);                // STOP pressed by the timer
        Assert.Equal("stop", robot.Calls[^1]);
        ds.Update(RunnerState.Stopped, null, 35 * Second + 4_000_000);
        Assert.Equal(Teleop, ds.Selected);                        // preselectTeleOp
        Assert.Equal(DsPhase.Ready, ds.Phase);
        Assert.False(ds.AutonomousTimerEnabled);
    }

    [Fact]
    public void Timer_switched_off_lets_an_autonomous_run_on()
    {
        var (ds, robot) = NewDs();
        ds.Select(Auto);
        ds.AutonomousTimerEnabled = false;
        ds.Init();
        ds.Play(0);
        ds.Update(RunnerState.Running, null, 60 * Second);
        Assert.Equal(DsPhase.Running, ds.Phase);
        Assert.DoesNotContain("stop", robot.Calls);
    }

    [Fact]
    public void Opmode_that_ends_or_crashes_returns_to_ready_with_the_error()
    {
        var (ds, _) = NewDs();
        ds.Select(Teleop);
        ds.Init();
        ds.Play(0);
        ds.Update(RunnerState.Crashed, "User code threw an uncaught exception: NullPointerException", Second);
        Assert.Equal(DsPhase.Ready, ds.Phase);
        Assert.StartsWith("User code threw", ds.Error);
        ds.Init();
        Assert.Null(ds.Error);   // the banner clears with the next INIT
    }

    [Fact]
    public void Autonomous_that_finishes_by_itself_also_preselects_the_teleop()
    {
        var (ds, _) = NewDs();
        ds.Select(Auto);
        ds.Init();
        ds.Play(0);
        ds.Update(RunnerState.Stopped, null, 9 * Second);
        Assert.Equal(Teleop, ds.Selected);
    }

    [Fact]
    public void Sticks_get_a_radial_deadzone_and_keep_the_sdk_sign()
    {
        var settings = new GamepadSettings(0.05);
        var rest = GamepadMapper.ToGamepad(new RawController(0.03f, -0.02f, 0, 0, 0, 0, GamepadButtons.None), 0, settings);
        Assert.Equal(0f, rest.LeftStickX);
        Assert.Equal(0f, rest.LeftStickY);
        var up = GamepadMapper.ToGamepad(new RawController(0, -1f, 0, 0, 0, 1.2f, GamepadButtons.A), 3, settings);
        Assert.Equal(-1f, up.LeftStickY);   // stick up = −1, like the SDK
        Assert.Equal(1f, up.RightTrigger);  // clamped
        Assert.Equal(GamepadButtons.A, up.Buttons);
        Assert.Equal(3, up.Id);
        var half = GamepadMapper.ToGamepad(new RawController(0.525f, 0, 0, 0, 0, 0, GamepadButtons.None), 0, settings);
        Assert.Equal(0.5f, half.LeftStickX, 4);   // rescaled so full deflection still reaches 1
    }

    [Fact]
    public void Gamepad_settings_load_from_config()
    {
        Assert.Equal(0.05, GamepadSettings.Load().StickDeadzone);
    }

    [Fact]
    public void Start_plus_a_assigns_gamepad1_and_start_plus_b_gamepad2()
    {
        var a = new GamepadAssignment();
        Assert.False(a.Update(7, GamepadButtons.A));                       // A alone does nothing
        Assert.False(a.Update(7, GamepadButtons.A | GamepadButtons.Start)); // A already held: not a new press
        a.Update(7, GamepadButtons.None);
        Assert.True(a.Update(7, GamepadButtons.Start | GamepadButtons.A));
        Assert.Equal(7, a.Gamepad1);
        Assert.True(a.Update(9, GamepadButtons.Start | GamepadButtons.B));
        Assert.Equal(9, a.Gamepad2);
        a.Update(9, GamepadButtons.None);
        Assert.True(a.Update(9, GamepadButtons.Start | GamepadButtons.A));  // controller 9 moves to gamepad1
        Assert.Equal((9, (int?)null), (a.Gamepad1!.Value, a.Gamepad2));
        a.Disconnected(9);
        Assert.Null(a.Gamepad1);
    }
}
