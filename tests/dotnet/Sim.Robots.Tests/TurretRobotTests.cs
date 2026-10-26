using System.Numerics;
using Sim.Hardware;

namespace RobotTests;

/// <summary>
/// Turret-robot features of the guided path and the hardware (RoboDojo #23205): servo gate, hood elevation, spring nip
/// exit speeds per element size, turret hard stops and an encoder geared to a mechanism.
/// </summary>
public class TurretRobotTests
{
    private static BallPathDef Path(GateDef? gate = null) =>
        new(Vector3.Zero, Vector3.One, 0.1, [Vector3.Zero, Vector3.Zero], 2, 90, 1, Vector3.Zero, Vector3.UnitY, 0.5, 1.0) { Gate = gate };

    [Fact]
    public void A_closed_gate_holds_the_next_element_until_it_opens()
    {
        var path = new GuidedBallPath(Path());
        path.Preload(7);
        Assert.Null(path.Feed(120, flywheelSurfaceSpeed: 10, gateOpen: false));
        Assert.Null(path.Feed(120, 10, gateOpen: false));       // the feeder slips against the element, no wind-up
        Assert.Equal(7, path.Feed(0, 10, gateOpen: true));       // opening the gate lets the waiting element through
    }

    [Fact]
    public void Gate_open_range_is_a_servo_angle_window()
    {
        var gate = new GateDef("gate_arm", (60, 20));
        Assert.True(gate.IsOpen(40));
        Assert.False(gate.IsOpen(0));
        Assert.False(gate.IsOpen(61));
    }

    [Fact]
    public void Hood_elevation_follows_the_servo_linearly_and_clamps()
    {
        var hood = new HoodDef("hood", (-60, 60), (30, 60));
        Assert.Equal(30, hood.Elevation(-60), 6);
        Assert.Equal(45, hood.Elevation(0), 6);
        Assert.Equal(60, hood.Elevation(90), 6);
        Assert.Equal(30, hood.Elevation(-100), 6);
    }

    [Fact]
    public void Spring_nip_grips_a_ball_that_bottoms_out_the_plate_harder_than_one_the_spring_alone_holds()
    {
        var nip = new SpringNipDef(Gap: 0.052, Travel: 0.020, Rate: 600, Preload: 2, ContactLength: 0.05);
        // POLLEN 71.1 mm: pushes the plate 19.1 mm (inside the travel), only the spring presses it.
        Assert.Equal(2 + 600 * 0.0191, nip.NormalForce(0.0711, 2000), 9);
        // NECTAR 91.4 mm: plate at the end of its travel, the ball itself squeezed by the remaining 19.4 mm.
        Assert.Equal(2 + 600 * 0.020 + 2500 * 0.0194, nip.NormalForce(0.0914, 2500), 9);
        Assert.Equal(0, nip.NormalForce(0.050, 2000));                      // narrower than the gap: not gripped
        // Fast rim (ideal 6 m/s): POLLEN slips, NECTAR leaves at rolling speed.
        var pollen = nip.ExitSpeed(0.0711, 2000, 0.02495, 6, 1.0);
        Assert.Equal(Math.Sqrt(1.0 * (2 + 600 * 0.0191) * 0.05 / 0.02495), pollen, 9);
        Assert.True(pollen < 6);
        Assert.Equal(6, nip.ExitSpeed(0.0914, 2500, 0.04128, 6, 1.0), 9);
        Assert.Equal(1.0, nip.ExitSpeed(0.0711, 2000, 0.02495, 1.0, 1.0), 9);   // slow rim: rolling speed is the limit
    }

    [Fact]
    public void The_team_robot_yaml_loads_its_turret_hood_gate_and_spring()
    {
        var robot = RobotDefinition.Load(RobotDefinition.FindConfig("robodojo_23205"));
        var b = robot.BallPath!;
        Assert.Equal("turret", b.Turret);
        Assert.Equal("shooter", b.Launcher);
        Assert.Equal("transfer_out", b.Feeder);
        Assert.NotNull(b.Gate);
        Assert.NotNull(b.Hood);
        Assert.True(b.SpringNip!.Gap < 0.0711 && b.SpringNip.Travel > 0);
        var turret = robot.Mechanism("turret");
        Assert.Equal(360, turret.LimitsDeg!.Value.Max - turret.LimitsDeg.Value.Min, 6);
        Assert.Equal("turret", robot.Mechanism("shooter").MountedOn);
        Assert.Equal(-8192, robot.Device("turret").EncoderCountsPerOutputRev!.Value, 6);   // counter-rotating pinion
    }

    [Fact]
    public void Turret_motor_stops_at_the_hard_stop_and_its_encoder_counts_minus_8192_per_output_rev()
    {
        var robot = RobotDefinition.Load(RobotDefinition.FindConfig("robodojo_23205"));
        var hardware = HardwareConfig.Load();
        var hw = new RobotHardware(robot, hardware, 240);
        var port = hw.Motor("turret");
        Assert.NotNull(port.OutputLimits);
        var (min, max) = port.OutputLimits!.Value;
        var turret = robot.Mechanism("turret");
        var device = robot.Device("turret");
        Assert.Equal(360, (max - min) * device.SpeedRatio * 180 / Math.PI, 3);

        var battery = new Battery(hardware.Batteries[robot.BatteryModel]);
        port.Controller.SetPower(1);
        for (var i = 0; i < 240 * 6; i++)   // 6 s at full power is far more than half a turn of the turret
        {
            hw.SampleEncoders();
            hw.UpdateControllers(1.0 / 240, battery.BrownedOut);
            hw.StepMechanisms(hw.SolveBus(battery, 1.0 / 240), 1.0 / 240);
        }
        var stop = turret.DriveSign * device.SpeedRatio > 0 ? max : min;
        Assert.Equal(stop, port.OutputAngle, 9);
        Assert.Equal(0, port.OutputOmega);
        hw.SampleEncoders();
        Assert.Equal(Math.Floor(port.OutputAngle / (2 * Math.PI) * -8192), port.Encoder.Counts, 0);
    }
}
