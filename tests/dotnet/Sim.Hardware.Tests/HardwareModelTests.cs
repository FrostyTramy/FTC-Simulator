namespace HardwareTests;

/// <summary>Motor, battery, hub controller, encoder and servo models against their datasheets and the SDK.</summary>
public class HardwareModelTests
{
    private const int TickRate = 240;
    private const double Dt = 1.0 / TickRate;
    private static readonly HardwareConfig Config = HardwareConfig.Load();

    private static Battery FreshBattery() => new(Config.Batteries["gobilda_3100_0012_0020"]);

    /// <summary>One motor on its own shaft, powered from a battery: encoder → controller → bus → torque → shaft.</summary>
    private sealed class Rig
    {
        public Rig(string motorKey, double extraInertia, Battery? battery = null)
        {
            Motor = new DcMotor(Config.Motor(motorKey));
            Shaft = new Shaft(Motor.ReflectedInertia + extraInertia);
            Encoder = new Encoder(Motor.Spec.CountsPerOutputRev, Config.HubController.VelocityWindowS, TickRate);
            Controller = new HubMotorController(Config.HubController, Config.SdkTypeOf(Motor.Spec), Encoder);
            Battery = battery ?? FreshBattery();
        }

        public DcMotor Motor { get; }
        public Shaft Shaft { get; }
        public Encoder Encoder { get; }
        public HubMotorController Controller { get; }
        public Battery Battery { get; }
        public double LoadTorque { get; set; }

        public void Run(double seconds)
        {
            for (var i = 0; i < (int)Math.Round(seconds * TickRate); i++)
            {
                Encoder.Sample(Shaft.Angle);
                var duty = Controller.Update(Dt);
                var (v, current, motors) = PowerBus.Solve(Battery, [new PowerBus.Load(Motor, duty, Shaft.Omega, Controller.BrakeWhenIdle)]);
                Battery.Update(v, current, Dt);
                Shaft.Step(motors[0].TorqueNm - LoadTorque, Dt);
            }
        }
    }

    [Fact]
    public void Config_converts_datasheet_units()
    {
        var m = Config.Motor("gobilda_5203_19_2");
        Assert.Equal(537.7, m.CountsPerOutputRev);
        Assert.Equal(312 * 2 * Math.PI / 60, m.FreeSpeedRadPerS, 9);
        Assert.Equal(24.3 * 0.0980665, m.StallTorqueNm, 9);
        Assert.Equal(2368, Config.SdkTypeOf(m).AchievableMaxTicksPerSecond);   // 2786 × 60 × 0.85 / 60 (SDK quirk: not per ratio)
    }

    [Theory]
    [InlineData("gobilda_5203_1_1")]
    [InlineData("gobilda_5203_19_2")]
    [InlineData("gobilda_5203_50_9")]
    public void Free_speed_at_nominal_voltage_matches_the_datasheet(string key)
    {
        var motor = new DcMotor(Config.Motor(key));
        var shaft = new Shaft(motor.ReflectedInertia + 1e-4);
        for (var i = 0; i < 10 * TickRate; i++)
        {
            shaft.Step(motor.Torque(motor.Current(motor.Spec.NominalVoltage, shaft.Omega), shaft.Omega), Dt);
        }
        Assert.InRange(shaft.Omega / motor.Spec.FreeSpeedRadPerS, 0.97, 1.03);
    }

    [Fact]
    public void Stall_torque_and_current_match_the_datasheet()
    {
        var motor = new DcMotor(Config.Motor("gobilda_5203_19_2"));
        var current = motor.Current(12, 0);
        Assert.Equal(9.2, current, 6);
        Assert.InRange(motor.Torque(current, 0) / motor.Spec.StallTorqueNm, 0.97, 1.0);   // minus gearbox friction
    }

    [Fact]
    public void Four_stalled_drive_motors_sag_the_battery()
    {
        var battery = FreshBattery();
        var motor = new DcMotor(Config.Motor("gobilda_5203_19_2"));
        var loads = Enumerable.Range(0, 4).Select(_ => new PowerBus.Load(motor, 1.0, 0, true)).ToList();
        var (volts, current, motors) = PowerBus.Solve(battery, loads);
        Assert.InRange(volts, 9.0, 11.5);
        Assert.Equal(battery.OpenCircuitVolts - battery.SeriesResistanceOhm * current, volts, 9);
        Assert.Equal(4 * volts / motor.ResistanceOhm + Config.Batteries["gobilda_3100_0012_0020"].QuiescentCurrentA, current, 6);
        Assert.All(motors, m => Assert.Equal(volts / motor.ResistanceOhm, m.CurrentA, 9));
    }

    [Fact]
    public void Battery_drains_and_its_voltage_drops()
    {
        var battery = FreshBattery();
        var start = battery.OpenCircuitVolts;
        battery.Update(12, 30, 60);   // 30 A for a minute = 0.5 Ah of 3 Ah
        Assert.Equal(1 - 0.5 / 3, battery.StateOfCharge, 9);
        Assert.True(battery.OpenCircuitVolts < start);
    }

    [Fact]
    public void Brownout_disables_outputs_until_the_voltage_recovers()
    {
        var battery = FreshBattery();
        battery.Update(7.5, 60, Dt);
        Assert.True(battery.BrownedOut);
        for (var i = 0; i < TickRate; i++)
        {
            battery.Update(12, 1, Dt);
        }
        Assert.False(battery.BrownedOut);
    }

    [Fact]
    public void Launcher_reaches_the_example_code_velocity_with_its_pidf()
    {
        // goBILDA example: 1:1 Yellow Jacket, RUN_USING_ENCODER, PIDF(40, 0, 0, 12.5), setVelocity(1250);
        // it feeds when getVelocity() > 1200 (CODE:76-77, 145-147). Hogback + Hyper Hub ≈ 1.65e-4 kg·m².
        var rig = new Rig("gobilda_5203_1_1", 1.65e-4);
        rig.Controller.SetMode(RunMode.RunUsingEncoder, rig.Shaft.Angle);
        rig.Controller.VelocityPidf = new Pidf(40, 0, 0, 12.5);
        rig.Controller.SetVelocity(1250);
        rig.Run(3.0);
        Assert.InRange(rig.Encoder.VelocityCountsPerS, 1200, 1400);
    }

    [Fact]
    public void Run_to_position_converges_and_clears_is_busy()
    {
        var rig = new Rig("gobilda_5203_19_2", 2e-3);
        rig.Controller.SetMode(RunMode.StopAndResetEncoder, rig.Shaft.Angle);
        rig.Controller.TargetPosition = 1000;
        rig.Controller.SetMode(RunMode.RunToPosition, rig.Shaft.Angle);
        rig.Controller.SetPower(0.5);
        rig.Run(0.05);
        Assert.True(rig.Controller.IsBusy);
        rig.Run(4.0);
        Assert.InRange(rig.Encoder.Counts, 1000 - 5, 1000 + 5);
        Assert.False(rig.Controller.IsBusy);
    }

    [Fact]
    public void Brake_stops_a_spinning_shaft_faster_than_float()
    {
        double SpinDownTime(ZeroPowerBehavior behaviour)
        {
            var rig = new Rig("gobilda_5203_19_2", 5e-3);
            rig.Controller.ZeroPower = behaviour;
            rig.Controller.SetPower(1);
            rig.Run(1.0);
            rig.Controller.SetPower(0);
            var start = rig.Shaft.Omega;
            for (var t = 0; t < 10 * TickRate; t++)
            {
                rig.Run(Dt);
                if (rig.Shaft.Omega < 0.1 * start)
                {
                    return t * Dt;
                }
            }
            return double.PositiveInfinity;
        }
        var brake = SpinDownTime(ZeroPowerBehavior.Brake);
        var floating = SpinDownTime(ZeroPowerBehavior.Float);
        Assert.True(brake < floating / 2, $"brake {brake:F3} s, float {floating:F3} s");
    }

    [Fact]
    public void Encoder_counts_one_output_revolution()
    {
        var encoder = new Encoder(537.7, 0.05, TickRate);
        encoder.Sample(2 * Math.PI);
        Assert.Equal(537, encoder.Counts);
        encoder.Reset(2 * Math.PI);
        encoder.Sample(2 * Math.PI - 0.01);
        Assert.Equal(-1, encoder.Counts);
    }

    [Fact]
    public void Run_using_encoder_power_maps_to_the_sdk_velocity_target()
    {
        var rig = new Rig("gobilda_5203_19_2", 2e-3);
        rig.Controller.SetMode(RunMode.RunUsingEncoder, 0);
        rig.Controller.SetPower(0.25);   // target = 0.25 × 2368 = 592 ticks/s
        rig.Run(3.0);
        Assert.InRange(rig.Encoder.VelocityCountsPerS, 592 * 0.9, 592 * 1.1);
    }

    [Fact]
    public void Positional_servo_moves_at_its_rated_speed()
    {
        var servo = new ServoModel(Config.Servo("gobilda_2000_0025_0003"), Config.ServoPorts, continuous: false);
        servo.SetPosition(1.0);
        Assert.Equal(2400, servo.PulseUs);
        Assert.Equal(135, servo.TargetAngleDeg, 6);   // (2400 − 1500) µs × 0.15 °/µs
        var speed = servo.NoLoadSpeedDegPerS(5.0);
        for (var i = 0; i < (int)(0.1 * TickRate); i++)
        {
            servo.Step(Dt);
        }
        Assert.InRange(servo.AngleDeg, 0.9 * speed * 0.1, speed * 0.1 + 1e-9);
        for (var i = 0; i < TickRate; i++)
        {
            servo.Step(Dt);
        }
        Assert.Equal(135, servo.AngleDeg, 6);
    }

    [Fact]
    public void Continuous_servo_follows_the_sdk_power_mapping_and_deadband()
    {
        var spec = Config.Servo("gobilda_2000_0025_0003");
        var servo = new ServoModel(spec, Config.ServoPorts, continuous: true);
        var full = servo.NoLoadSpeedDegPerS(5.0);
        servo.SetPower(0);
        servo.Step(Dt);
        Assert.Equal(0, servo.SpeedDegPerS);
        servo.SetPower(1);   // 2400 µs is beyond the 2000 µs continuous limit → full speed
        servo.Step(Dt);
        Assert.Equal(full, servo.SpeedDegPerS, 6);
        servo.SetPower(-0.5);   // 1050 µs → 90 % of full speed backwards (SDK range 600–2400)
        servo.Step(Dt);
        Assert.Equal(-0.9 * full, servo.SpeedDegPerS, 6);
    }

    [Fact]
    public void Servo_slows_down_under_load_and_stalls_at_stall_torque()
    {
        var servo = new ServoModel(Config.Servo("gobilda_2000_0025_0002"), Config.ServoPorts, continuous: true);
        servo.SetPower(1);
        servo.Step(Dt, loadTorqueNm: 0);
        var free = servo.SpeedDegPerS;
        servo.Step(Dt, loadTorqueNm: servo.StallTorqueNm(5.0) / 2);
        Assert.Equal(free / 2, servo.SpeedDegPerS, 6);
        servo.Step(Dt, loadTorqueNm: servo.StallTorqueNm(5.0) * 1.1);
        Assert.Equal(0, servo.SpeedDegPerS);
    }

    [Fact]
    public void Same_inputs_give_bit_identical_results()
    {
        static (double, long, double) Simulate()
        {
            var rig = new Rig("gobilda_5203_1_1", 1.65e-4);
            rig.Controller.SetMode(RunMode.RunUsingEncoder, 0);
            rig.Controller.VelocityPidf = new Pidf(40, 0, 0, 12.5);
            rig.Controller.SetVelocity(1250);
            rig.Run(2.0);
            return (rig.Shaft.Omega, rig.Encoder.Counts, rig.Battery.StateOfCharge);
        }
        Assert.Equal(Simulate(), Simulate());
    }
}
