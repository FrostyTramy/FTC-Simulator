using System.Globalization;
using System.Text.Json;

namespace RobotTests;

/// <summary>goBILDA StarterBot drivetrain on a flat floor with the example code's mecanum mixing.</summary>
public class DrivetrainTests
{
    private static readonly HardwareConfig Hardware = HardwareConfig.Load();
    private static readonly RobotDefinition Robot = RobotDefinition.Load(RobotDefinition.FindConfig("gobilda_starter"));

    private static PlanarRobotSim NewSim() => new(Robot, Hardware, DriveContact.LoadDefault());

    private static PlanarRobotSim Drive(double forward, double strafe, double rotate, double seconds)
    {
        var sim = NewSim();
        sim.MecanumDrive(forward, strafe, rotate);
        for (var i = 0; i < (int)(seconds * sim.TickRate); i++)
        {
            sim.Step();
        }
        return sim;
    }

    /// <summary>Theoretical free speed: 312 rpm motor, 1:1 miter gears, Ø104 mm wheel.</summary>
    private static double TheoreticalTopSpeed =>
        Hardware.Motor("gobilda_5203_19_2").FreeSpeedRadPerS / Robot.DriveGearing * Robot.WheelRadius;

    [Fact]
    public void Robot_definition_loads_the_starter_bot()
    {
        Assert.Equal(4, Robot.Wheels.Count);
        Assert.Equal("mecanum", Robot.DrivetrainType);
        Assert.Equal(8.38, Robot.MassKg, 6);
        Assert.Equal("REVERSE", Robot.Device("left_front_drive").Direction);
        Assert.Equal(1.70, TheoreticalTopSpeed, 2);
    }

    [Fact]
    public void Left_stick_forward_drives_the_robot_forward_and_straight()
    {
        var sim = Drive(1, 0, 0, 2.0);
        var v = sim.RobotVelocity;
        Assert.True(v.Z < -1.0, $"forward speed {-v.Z:F3} m/s");     // forward = −Z
        Assert.InRange(Math.Abs(v.X), 0, 0.02);
        Assert.InRange(Math.Abs(sim.YawRate), 0, 0.02);
        // Free speed scales with the bus voltage (datasheet at 12 V; a fresh pack is ~13.6 V under this load).
        var atBusVoltage = TheoreticalTopSpeed * sim.Battery.BusVolts / 12.0;
        Assert.InRange(-v.Z / atBusVoltage, 0.85, 1.0);
    }

    [Fact]
    public void Strafe_right_moves_the_robot_right_and_slower_than_forward()
    {
        var forward = -Drive(1, 0, 0, 2.0).RobotVelocity.Z;
        var sim = Drive(0, 1, 0, 2.0);
        Assert.True(sim.RobotVelocity.X > 0.5, $"strafe speed {sim.RobotVelocity.X:F3} m/s");
        Assert.InRange(Math.Abs(sim.RobotVelocity.Z), 0, 0.03);
        Assert.InRange(sim.RobotVelocity.X / forward, 0.80, 0.90);   // physics.yaml mecanum_lateral_efficiency 0.85
    }

    [Fact]
    public void Right_stick_right_turns_the_robot_clockwise_seen_from_above()
    {
        var sim = Drive(0, 0, 1, 1.0);
        Assert.True(sim.YawRate < -1.0, $"yaw rate {sim.YawRate:F3} rad/s");   // CCW positive about +Y
        Assert.InRange(sim.RobotVelocity.Length, 0, 0.05);
    }

    [Fact]
    public void Full_power_launch_reaches_90_percent_speed_within_a_second()
    {
        var sim = NewSim();
        sim.MecanumDrive(1, 0, 0);
        var top = -Drive(1, 0, 0, 3.0).RobotVelocity.Z;
        var t90 = double.NaN;
        for (var i = 0; i < 3 * sim.TickRate && double.IsNaN(t90); i++)
        {
            sim.Step();
            if (-sim.RobotVelocity.Z >= 0.9 * top)
            {
                t90 = sim.Time;
            }
        }
        Assert.InRange(t90, 0.1, 1.0);
    }

    [Fact]
    public void Driving_sags_the_battery_and_drains_it()
    {
        var sim = NewSim();
        var rest = sim.Battery.OpenCircuitVolts;
        sim.MecanumDrive(1, 0, 0);
        sim.Step();
        Assert.True(sim.Battery.BusVolts < rest - 0.5, $"bus {sim.Battery.BusVolts:F2} V vs {rest:F2} V");
        for (var i = 0; i < 240; i++)
        {
            sim.Step();
        }
        Assert.True(sim.Battery.StateOfCharge < 1.0);
    }

    [Fact]
    public void Same_commands_give_identical_trajectories()
    {
        static (double, double, double) Run()
        {
            var s = Drive(0.7, 0.4, -0.3, 2.0);
            return (s.Position.X, s.Position.Z, s.Yaw);
        }
        Assert.Equal(Run(), Run());
    }

    [Fact]
    public void Benchmark_numbers_are_recorded()
    {
        var forward = Drive(1, 0, 0, 3.0);
        var strafe = Drive(0, 1, 0, 3.0);
        var turn = Drive(0, 0, 1, 3.0);
        var sim = NewSim();
        sim.MecanumDrive(1, 0, 0);
        var accel = 0.0;
        for (var i = 0; i < 24; i++)   // first 0.1 s
        {
            sim.Step();
        }
        accel = -sim.RobotVelocity.Z / sim.Time;
        var result = new SortedDictionary<string, double>(StringComparer.Ordinal)
        {
            ["forward_top_speed_mps"] = Math.Round(-forward.RobotVelocity.Z, 3),
            ["strafe_top_speed_mps"] = Math.Round(strafe.RobotVelocity.X, 3),
            ["strafe_ratio"] = Math.Round(strafe.RobotVelocity.X / -forward.RobotVelocity.Z, 3),
            ["turn_rate_radps"] = Math.Round(-turn.YawRate, 3),
            ["initial_acceleration_mps2"] = Math.Round(accel, 2),
            ["theoretical_free_speed_mps"] = Math.Round(TheoreticalTopSpeed, 3),
            ["bus_volts_full_forward"] = Math.Round(forward.Battery.BusVolts, 2),
        };
        var dir = Path.Combine(Path.GetDirectoryName(RobotDefinition.FindConfig("gobilda_starter"))!, "..", "..", "reports", "benchmarks");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "drivetrain_gobilda_starter.json"),
            JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
        Assert.All(result.Values, v => Assert.False(double.IsNaN(v), string.Join(", ", result.Select(kv => $"{kv.Key}={kv.Value.ToString(CultureInfo.InvariantCulture)}"))));
    }
}
