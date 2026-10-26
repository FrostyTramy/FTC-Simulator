using System.Numerics;
using Sim.Hardware;

namespace RobotTests;

/// <summary>
/// Servo arms of the RoboDojo #23205 robot (USER 2026-10-26): the flower bar rests on the floor when swung down (floor
/// stop), and the hood's servo position 0 is the starting (CAD) pose with higher positions raising the hood.
/// </summary>
public class ArmAndHoodTests
{
    // A 0.2 m bar on a pivot 0.15 m above the floor, pointing up and back at its CAD pose (robot frame: +Y up, -Z forward).
    private static readonly Vector3 Pivot = new(0, 0.15f, 0);
    private static readonly Vector3[] Bar = [new(-0.1f, 0.15f, 0), new(0.1f, 0.15f, 0), new(-0.1f, 0.30f, 0.13f), new(0.1f, 0.30f, 0.13f)];

    [Fact]
    public void A_bar_swung_down_stops_with_its_lowest_point_just_into_the_floor()
    {
        // Turning about +X carries the back-up bar over the top to the front and down.
        var (min, max) = FloorStop.Limits(Bar, Pivot, Vector3.UnitX, penetration: 0.002);
        Assert.NotNull(max);
        Assert.Equal(-0.002, FloorStop.LowestY(Bar, Pivot, Vector3.UnitX, max!.Value), 4);
        Assert.True(max > Math.PI / 2 && max < 2 * Math.PI);
        // Commanded further, the bar stays at the stop; inside the range it follows the servo.
        Assert.Equal(max.Value, FloorStop.Clamp(max.Value + 0.5, (min, max)), 9);
        Assert.Equal(0.3, FloorStop.Clamp(0.3, (min, max)), 9);
        // Swinging back the other way it also meets the floor (behind the pivot), never below the stop.
        Assert.NotNull(min);
        Assert.True(FloorStop.LowestY(Bar, Pivot, Vector3.UnitX, min!.Value) >= -0.002 - 1e-6);
    }

    [Fact]
    public void An_arm_too_short_to_reach_the_floor_has_no_stop()
    {
        var shortArm = new[] { new Vector3(0, 0.15f, 0), new Vector3(0, 0.25f, 0) };
        Assert.Equal((null, null), FloorStop.Limits(shortArm, new Vector3(0, 0.2f, 0), Vector3.UnitX, 0.002));
    }

    [Fact]
    public void A_cad_pose_already_below_the_floor_is_a_config_error()
    {
        var sunk = new[] { new Vector3(0, -0.01f, 0), new Vector3(0, 0.1f, 0) };
        Assert.Throws<RobotConfigException>(() => FloorStop.Limits(sunk, Vector3.Zero, Vector3.UnitX, 0.002));
    }

    [Fact]
    public void The_flower_bar_has_a_floor_stop()
    {
        var robot = RobotDefinition.Load(RobotDefinition.FindConfig("robodojo_23205"));
        Assert.Equal(0.002, robot.Mechanism("yellow_bar").FloorStopPenetration!.Value, 9);
    }

    [Theory]
    [InlineData(0.0, 90.0)]
    [InlineData(0.5, 78.4)]
    [InlineData(1.0, 66.8)]
    public void Hood_position_0_is_the_starting_pose_and_higher_positions_raise_the_hood(double position, double elevationDeg)
    {
        var robot = RobotDefinition.Load(RobotDefinition.FindConfig("robodojo_23205"));
        var hw = new RobotHardware(robot, HardwareConfig.Load(), 240);
        var port = hw.Servos.Single(s => s.Device.Name == "hood");
        var hood = robot.Mechanism("hood");
        Assert.Equal(hood.CadServoAngleDeg, port.Model.AngleDeg, 6);   // powered off: where the robot was built

        port.Model.Enabled = true;   // PWM on, as Servo.setPosition does through the powertrain
        port.Model.SetPosition(0);
        Assert.Equal(hood.CadServoAngleDeg, port.Model.TargetAngleDeg, 1);   // position 0 = the CAD (starting) pose

        port.Model.SetPosition(position);
        for (var i = 0; i < 240 * 3; i++)
        {
            hw.StepServos(1.0 / 240);
        }
        var servoAngle = port.Model.AngleDeg;
        Assert.Equal(elevationDeg, robot.BallPath!.Hood!.Elevation(servoAngle), 1);

        // The hood sits behind and above the shooter shaft; its mesh turns by (servo − CAD) × ratio × drive_sign
        // about the hood axis (RobotBody.SpinFor). A higher position must lift a point of the hood.
        var angle = (servoAngle - hood.CadServoAngleDeg) * Math.PI / 180 * port.Device.SpeedRatio * hood.DriveSign;
        var behindAndAbove = new Vector3(0, 0.03f, 0.10f);
        var turned = Vector3.Transform(behindAndAbove, Quaternion.CreateFromAxisAngle(Vector3.Normalize(hood.Axis!.Value), (float)angle));
        if (position > 0)
        {
            Assert.True(turned.Y > behindAndAbove.Y + 0.005, $"hood point at y {turned.Y:F4} did not rise");
            Assert.True(turned.Z < behindAndAbove.Z, "the hood should swing forward over the wheel");
        }
        else
        {
            Assert.Equal(behindAndAbove.Y, turned.Y, 4);
        }
    }
}
