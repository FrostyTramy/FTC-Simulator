using Sim.Core.Randomness;

namespace RunnerTests;

/// <summary>
/// Phase 6 acceptance (specs/13-phases.md): the goBILDA example TeleOp and our example Autonomous, compiled unchanged by
/// java/build-adapter, run in the sandboxed JVM runner against the simulated StarterBot. Tests in this class share one
/// collection so only one runner JVM runs at a time.
/// </summary>
public class ExampleCodeTests
{
    private const string TeleOp = "Mec BioBuzz StarterBot Teleop";
    private const string Auto = "Mec BioBuzz StarterBot Auto";
    private static readonly GamepadState Neutral = GamepadState.Neutral;
    private static readonly GamepadState StickForward = new(LeftStickY: -1f);   // SDK: stick up = −1

    [Fact]
    public void Runner_lists_the_example_opmodes_like_the_driver_station()
    {
        using var robot = RobotUnderCode.Start();
        var opModes = robot.Session.ListOpModes();
        Assert.Equal([Auto, TeleOp], opModes.Select(o => o.Name));
        var auto = opModes[0];
        Assert.True(auto.Autonomous);
        Assert.Equal("StarterBot", auto.Group);
        Assert.Equal(TeleOp, auto.PreselectTeleOp);
        Assert.Equal("org.firstinspires.ftc.teamcode.BioBuzzStarterbotTeleopMecanum", opModes[1].ClassName);
        Assert.False(opModes[1].Autonomous);
        Assert.StartsWith("12.0.0", robot.Session.SdkVersion);
    }

    [Fact]
    public void Teleop_left_stick_forward_sends_the_raw_drive_powers()
    {
        using var robot = RobotUnderCode.Start();
        robot.Init(TeleOp);
        var init = robot.Run(0.1, Neutral);
        Assert.Equal(RunnerState.Init, init.State);
        Assert.Contains("Status : Initialized", init.Telemetry);
        robot.Session.Start();
        var r = robot.Run(0.1, StickForward);
        Assert.Equal(RunnerState.Running, r.State);
        // Left motors are REVERSE in the code (CODE:121-124) and the goBILDA motor type is CCW: the SDK negates them.
        Assert.Equal(-1.0, robot.Motor(r, "left_front_drive").Power, 9);
        Assert.Equal(1.0, robot.Motor(r, "right_front_drive").Power, 9);
        Assert.Equal(-1.0, robot.Motor(r, "left_back_drive").Power, 9);
        Assert.Equal(1.0, robot.Motor(r, "right_back_drive").Power, 9);
        Assert.Equal("BRAKE", robot.Motor(r, "left_front_drive").ZeroPower);
        Assert.Equal("RUN_WITHOUT_ENCODER", robot.Motor(r, "left_front_drive").Mode);
    }

    [Fact]
    public void Right_bumper_spins_the_launcher_up_and_the_windmill_feeds_once_at_speed()
    {
        using var robot = RobotUnderCode.Start();
        robot.Init(TeleOp);
        robot.Run(0.05, Neutral);
        robot.Session.Start();
        var bumper = new GamepadState(Buttons: GamepadButtons.RightBumper);
        double fedAt = -1;
        TickResult r = robot.Step(bumper);
        while (robot.Seconds < 3)
        {
            r = robot.Step(bumper);
            if (fedAt < 0 && robot.Servo(r, "windmillServo").PulseUs < 1000)
            {
                fedAt = robot.Seconds;
            }
        }
        var launcher = robot.Motor(r, "launcher");
        Assert.True(launcher.VelocitySet);
        Assert.Equal(1250, launcher.Velocity);                       // CODE:76, FORWARD → raw +1250
        Assert.Equal("RUN_USING_ENCODER", launcher.Mode);
        Assert.Equal([40.0, 0, 0, 12.5], launcher.VelocityPidf!);    // CODE:145-147
        var measured = robot.Sim.Hardware.Motor("launcher").Encoder.VelocityCountsPerS;
        Assert.InRange(measured, 1190, 1310);                       // hub velocity PIDF holds the target
        // The code only feeds when getVelocity() > 1200 (CODE:290); the windmill is REVERSE, so power 1 → 600 µs.
        Assert.InRange(fedAt, 0.1, 2.0);
        Assert.Equal(600, robot.Servo(r, "windmillServo").PulseUs, 6);
    }

    [Fact]
    public void Teleop_drives_the_robot_like_phase5_direct_drive()
    {
        (GamepadState Pad, double Forward, double Strafe, double Rotate)[] phases =
        [
            (StickForward, 1, 0, 0),
            (new GamepadState(LeftStickX: 1f), 0, 1, 0),
            (new GamepadState(RightStickX: 1f), 0, 0, 1),
            (Neutral, 0, 0, 0),
        ];
        const int phaseTicks = RobotUnderCode.TickRate;   // 1 s each

        // Code path: gamepad → runner → TeleOp mixing → raw powers, which act from the step after the STATE reports them.
        using var robot = RobotUnderCode.Start();
        robot.Init(TeleOp);
        robot.Run(0.1, Neutral);
        robot.Session.Start();
        var latency = -1;
        var codePoses = new List<(Vec2d Position, double Yaw)>();
        for (var p = 0; p < phases.Length; p++)
        {
            for (var i = 0; i < phaseTicks; i++)
            {
                var r = robot.Step(phases[p].Pad);
                if (latency < 0 && robot.Motor(r, "left_front_drive").Power != 0)
                {
                    latency = i + 1;
                }
            }
            codePoses.Add((robot.Sim.Position, robot.Sim.Yaw));
        }
        Assert.Equal(RunnerState.Running, robot.Last!.State);
        // ≤ one Driver Station gamepad packet (20 ms = 5 steps; here the packet comes 1 step after the change) + 1 step.
        Assert.Equal(2, latency);

        // Phase 5 direct drive (the example's mixing in C#), commands shifted by that latency: the same motion. Not bit for
        // bit: the code writes the four drive powers as sequential hub commands (2 ms each, hardware_latency.yaml), so the
        // motors start up to ~6 ms apart, as on a real Control Hub.
        var direct = new PlanarRobotSim(RobotUnderCode.Robot, RobotUnderCode.Hardware, DriveContact.LoadDefault(), RobotUnderCode.TickRate);
        var report = new List<object>();
        for (var step = 0; step < phases.Length * phaseTicks; step++)
        {
            var phase = phases[Math.Max(0, step - latency) / phaseTicks];
            if (step < latency)
            {
                direct.MecanumDrive(0, 0, 0);
            }
            else
            {
                direct.MecanumDrive(phase.Forward, phase.Strafe, phase.Rotate);
            }
            direct.Step();
            if ((step + 1) % phaseTicks == 0)
            {
                var (position, yaw) = codePoses[step / phaseTicks];
                var dPos = (direct.Position - position).Length;
                var dYaw = Math.Abs(direct.Yaw - yaw) * 180 / Math.PI;
                report.Add(new { phase = step / phaseTicks, position_difference_m = Math.Round(dPos, 4), yaw_difference_deg = Math.Round(dYaw, 3) });
                Assert.True(dPos < 0.01 && dYaw < 1.0,
                    $"phase {step / phaseTicks}: direct {Pose(direct)}, code ({position.X:F6}, {position.Z:F6}, {yaw * 180 / Math.PI:F4}°)");
            }
        }
        var dir = Path.Combine(Path.GetDirectoryName(RobotDefinition.FindConfig("gobilda_starter"))!, "..", "..", "reports", "benchmarks");
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, "teleop_vs_direct_drive.json"), System.Text.Json.JsonSerializer.Serialize(new
        {
            description = "Example TeleOp in the runner vs Phase 5 direct drive (same mixing in C#), 1 s each: forward, strafe right, rotate right, stop",
            code_latency_steps = latency,
            phases = report,
        }, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
    }

    [Fact]
    public void Example_autonomous_launches_the_preloads_then_leaves_and_strafes()
    {
        using var robot = RobotUnderCode.Start();
        robot.Init(Auto);
        robot.Run(0.5, Neutral);
        Assert.Equal(RunnerState.Init, robot.Last!.State);
        robot.Session.Start();
        double fedAt = -1, startedAt = robot.Seconds;
        var sawLauncherTelemetry = false;
        Assert.Equal(RunnerState.Running, robot.Step(Neutral).State);
        while (robot.Last!.State == RunnerState.Running && robot.Seconds < 20)
        {
            var r = robot.Step(Neutral);
            if (fedAt < 0 && r.Servos.Count > 0 && robot.Servo(r, "windmillServo").PulseUs < 1000)
            {
                fedAt = robot.Seconds - startedAt;
            }
            sawLauncherTelemetry |= r.Telemetry.Any(l => l.StartsWith("Launcher : ", StringComparison.Ordinal));
        }
        var last = robot.Last!;
        Assert.Equal(RunnerState.Stopped, last.State);   // runOpMode returned by itself
        Assert.Null(last.Error);
        Assert.True(sawLauncherTelemetry);
        Assert.InRange(fedAt, 0.2, 2.0);   // launcher reached 1200 ticks/s, feeding started
        robot.Run(0.5, Neutral);           // Stop Robot: everything comes to rest

        // 6 in forward (−Z), then 24 in of wheel travel to the right; mecanum strafes at ≈85 % of that (physics.yaml).
        var sim = robot.Sim;
        Assert.InRange(-sim.Position.Z, 0.12, 0.19);
        Assert.InRange(sim.Position.X, 0.45, 0.62);
        Assert.InRange(sim.Yaw * 180 / Math.PI, -4, 4);
    }

    [Fact]
    public void Stop_runs_the_sdk_stop_robot_opmode_brake_then_failsafe()
    {
        using var robot = RobotUnderCode.Start();
        robot.Init(TeleOp);
        robot.Run(0.05, Neutral);
        robot.Session.Start();
        robot.Run(0.5, StickForward);
        robot.Session.Stop();
        var r = robot.Step(StickForward);
        Assert.Equal(RunnerState.Stopped, r.State);
        var lf = robot.Sim.Hardware.Motor("left_front_drive").Controller;
        Assert.Equal(0, lf.Power);
        Assert.True(lf.Enabled && lf.BrakeWhenIdle);          // setPower(0) with the code's BRAKE
        robot.Run(0.12, StickForward);
        Assert.False(lf.Enabled);                             // hub failsafe after 100 ms
        Assert.All(robot.Sim.Hardware.Servos, s => Assert.False(s.Model.Enabled));
        // Outputs off: the robot coasts against gearbox friction and stops (≈1.8 s from full speed with these values).
        var speeds = new List<double>();
        for (var i = 0; i < 15; i++)
        {
            robot.Run(0.2, StickForward);
            speeds.Add(robot.Sim.Velocity.Length);
        }
        var trace = string.Join(", ", speeds.Select(v => v.ToString("G4")));
        Assert.True(speeds.Zip(speeds.Skip(1)).All(pair => pair.Second <= pair.First + 1e-4), "speeds after stop: " + trace);
        Assert.True(speeds[^1] < 0.005, "speeds after stop: " + trace);
    }

    [Fact]
    public void Same_inputs_give_the_same_run_bit_for_bit()
    {
        var a = ScriptedTeleopRun(seed: 7);
        var b = ScriptedTeleopRun(seed: 7);
        var c = ScriptedTeleopRun(seed: 8);
        Assert.Equal(a, b);
        Assert.NotEqual(a, c);
    }

    /// <summary>Random driver: new stick, trigger and bumper values every 0.25 s for 6 s. Returns the trace hash.</summary>
    private static string ScriptedTeleopRun(ulong seed)
    {
        var rng = new DeterministicRng(seed);
        using var robot = RobotUnderCode.Start();
        robot.Init(TeleOp);
        robot.Run(0.1, Neutral);
        robot.Session.Start();
        for (var block = 0; block < 24; block++)
        {
            var pad = new GamepadState(
                LeftStickX: (float)rng.NextDouble(-1, 1),
                LeftStickY: (float)rng.NextDouble(-1, 1),
                RightStickX: (float)rng.NextDouble(-1, 1),
                LeftTrigger: (float)rng.NextDouble(),
                RightTrigger: (float)rng.NextDouble(),
                Buttons: rng.NextInt(2) == 0 ? GamepadButtons.None : GamepadButtons.RightBumper);
            robot.Run(0.25, pad);
        }
        robot.Session.Stop();
        robot.Run(0.2, Neutral);
        Assert.Equal(RunnerState.Stopped, robot.Last!.State);
        return robot.Trace.ToString();
    }

    private static string Pose(PlanarRobotSim s) => $"({s.Position.X:F3}, {s.Position.Z:F3}, {s.Yaw * 180 / Math.PI:F1}°)";
}
