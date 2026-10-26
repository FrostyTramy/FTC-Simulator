using Sim.Core.Config;

namespace RunnerTests;

/// <summary>
/// The TeleOp written for the RoboDojo #23205 turret robot (tests/compat/robodojo_23205), compiled by the build adapter
/// and run in the JVM runner against that robot: the turret follows dpad-left to about +90° with the counter-rotating
/// Through Bore Encoder (its own P loop, RUN_TO_POSITION would run away), and the right bumper spins the shooter up
/// before the gate opens.
/// </summary>
[Collection("runner")]
public class TeamRobotCodeTests
{
    private static readonly Lazy<string> Jar = new(() =>
    {
        var source = RepoPaths.Find(Path.Combine("tests", "compat", "robodojo_23205", "TeamCode", "src", "main", "java"));
        var build = Path.Combine(Path.GetTempPath(), "ftcsim-tests", "robodojo_23205");
        var result = TeamCodeBuilder.Build(source, build);
        Assert.True(result.Success, result.Output);
        return result.JarPath!;
    });

    private static RobotUnderCode Start()
    {
        var robot = RobotDefinition.Load(RobotDefinition.FindConfig("robodojo_23205"));
        var r = RobotUnderCode.Start(robot, Jar.Value);
        r.Init("RoboDojo TeleOp");
        r.Run(0.2, GamepadState.Neutral);
        r.Session.Start();
        return r;
    }

    private static double TurretDeg(RobotUnderCode r)
    {
        var port = r.Sim.Hardware.Motor("turret");
        var def = r.Sim.Robot.Mechanism("turret");
        return port.OutputAngle * port.Device.SpeedRatio * def.DriveSign * 180 / Math.PI;
    }

    [Fact]
    public void Turret_follows_the_dpad_and_holds_the_target()
    {
        using var r = Start();
        var left = new GamepadState(Buttons: GamepadButtons.DpadLeft);
        var result = r.Run(0.5, left);                       // target moves 180°/s → +90°
        Assert.True(result.State == RunnerState.Running, result.Error);
        r.Run(1.5, GamepadState.Neutral);
        Assert.InRange(TurretDeg(r), 85, 95);
        Assert.Contains(r.Last!.Telemetry, l => l.StartsWith("Turret") && l.Contains("target 90"));
        r.Run(0.5, new GamepadState(Buttons: GamepadButtons.Y));   // back to the front
        r.Run(1.5, GamepadState.Neutral);
        Assert.InRange(TurretDeg(r), -5, 5);
    }

    [Fact]
    public void Right_bumper_spins_the_shooter_up_before_the_gate_opens()
    {
        using var r = Start();
        var bumper = new GamepadState(Buttons: GamepadButtons.RightBumper);
        var gate = r.Sim.Hardware.Servos.Single(s => s.Device.Name == "gate");
        r.Run(0.05, bumper);
        Assert.True(gate.Model.TargetAngleDeg < 40, "gate opened before the shooter was at speed");
        r.Run(2.0, bumper);
        Assert.True(r.Sim.Powertrain.VelocityFromSdk("shooter") > 0.95 * 1500);
        Assert.True(gate.Model.TargetAngleDeg > 40, $"gate target {gate.Model.TargetAngleDeg:F0}°");
        r.Run(0.3, GamepadState.Neutral);
        Assert.True(gate.Model.TargetAngleDeg < 40);
    }
}
