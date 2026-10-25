using Sim.Hardware;
using Sim.Robots;

namespace Sim.Runner;

/// <summary>
/// Connects a robot's simulated hub hardware (<see cref="RobotHardware"/>) to the OpMode runner: the device list for
/// CONFIGURE, the motor inputs and IMU reading for TICK, and the runner's raw commands applied to the hub firmware
/// models. When no OpMode is running the SDK runs its "Stop Robot" default OpMode (SDK 12.0.0
/// OpModeManagerImpl$DefaultOpMode, javap): motors with power ≠ 0 get setPower(0) at once (startSafe), then every
/// SAFE_WAIT_NANOS = 100 ms every hub gets a failsafe command (staySafe). The failsafe turns motor and servo outputs
/// off (firmware behaviour ASSUMED, docs/open_questions.md Q-008); before the first OpMode it happens at once.
/// </summary>
public sealed class RunnerBridge
{
    public const long SafeWaitNanos = 100_000_000;

    private readonly RobotHardware _hardware;
    private readonly HubControllerSpec _hub;
    private readonly HubDef _imuHub;
    private readonly Quat _imuMount;
    private readonly Dictionary<string, int> _resetCounts = [];
    private bool _stopRobotActive;
    private bool _opModeEverRan;
    private long _nextFailsafeNanos;

    public RunnerBridge(RobotHardware hardware, HubControllerSpec hubSpec)
    {
        _hardware = hardware;
        _hub = hubSpec;
        var imu = hardware.Robot.Devices.FirstOrDefault(d => d.Type == "imu");
        _imuHub = hardware.Robot.Hubs[imu is null ? 0 : hardware.Robot.HubIndex(imu.Hub)];
        _imuMount = HubMount(_imuHub.ImuLogo, _imuHub.ImuUsb);
    }

    /// <summary>The robot's devices in CONFIGURE form (exact hardwareMap names from the robot YAML).</summary>
    public IReadOnlyList<RunnerDevice> Devices()
    {
        var devices = new List<RunnerDevice>();
        foreach (var m in _hardware.Motors)
        {
            var t = m.SdkType;
            devices.Add(new RunnerDevice(m.Device.Name, "motor", m.Hub, m.Port, t.DisplayName, t.TicksPerRev, t.Gearing, t.MaxRpm,
                t.AchievableMaxRpmFraction, t.Orientation));
        }
        foreach (var s in _hardware.Servos)
        {
            devices.Add(new RunnerDevice(s.Device.Name, s.Device.Type, s.Hub, s.Port));
        }
        foreach (var d in _hardware.Robot.Devices.Where(d => d.Type == "imu"))
        {
            devices.Add(new RunnerDevice(d.Name, "imu", _hardware.Robot.HubIndex(d.Hub), 0));
        }
        return devices;
    }

    /// <summary>A new runner session (fresh JVM, counters start over). Hub state such as encoder counts persists, as on a real robot.</summary>
    public void BeginSession() => _resetCounts.Clear();

    /// <summary>
    /// Motor inputs as a bulk read reports them: int32 encoder counts, velocity as int16 counts/s (SDK 12.0.0
    /// LynxGetBulkInputDataResponse: int[] encoders, short[] velocities; saturation ASSUMED), current magnitude.
    /// </summary>
    public IReadOnlyList<MotorInput> Inputs() => _hardware.Motors.Select(m => new MotorInput(
        m.Key,
        unchecked((int)m.Encoder.Counts),
        Math.Clamp(Math.Round(m.Encoder.VelocityCountsPerS), short.MinValue, short.MaxValue),
        Math.Abs(m.CurrentA),
        m.Controller.IsBusy)).ToList();

    /// <summary>
    /// IMU chip orientation and angular velocity from the robot's motion. <paramref name="robot"/> is the robot's
    /// orientation in a Z-up world in the FTC robot frame (+X right, +Y forward, +Z up); <paramref name="angularVelocity"/>
    /// is in that robot frame. The chip is mounted as the robot YAML's hub says, which may differ from what the team's
    /// code declares (exactly the mistake the real IMU punishes).
    /// </summary>
    public (Quat Orientation, Vec3d AngularVelocity) Imu(Quat robot, Vec3d angularVelocity) =>
        (robot * _imuMount, _imuMount.Conjugate.Rotate(angularVelocity));

    /// <summary>Applies one STATE: the OpMode's commands while it runs, the SDK's Stop Robot behaviour otherwise.</summary>
    public void Apply(TickResult result, long nowNanos)
    {
        if (result.State is RunnerState.Init or RunnerState.Running or RunnerState.Stopping)
        {
            _stopRobotActive = false;
            _opModeEverRan = true;
            foreach (var c in result.Motors)
            {
                ApplyMotor(c);
            }
            foreach (var c in result.Servos)
            {
                ApplyServo(c);
            }
        }
        else
        {
            StopRobot(nowNanos);
        }
    }

    /// <summary>The SDK's default OpMode, also used while no runner is attached.</summary>
    public void StopRobot(long nowNanos)
    {
        if (!_stopRobotActive)
        {
            _stopRobotActive = true;
            foreach (var m in _hardware.Motors.Where(m => m.Controller.Power != 0))
            {
                m.Controller.SetPower(0);
            }
            _nextFailsafeNanos = _opModeEverRan ? nowNanos + SafeWaitNanos : nowNanos;
        }
        if (nowNanos >= _nextFailsafeNanos)
        {
            foreach (var m in _hardware.Motors)
            {
                m.Controller.SetPower(0);
                m.Controller.Enabled = false;
            }
            foreach (var s in _hardware.Servos)
            {
                s.Model.Enabled = false;
            }
            _nextFailsafeNanos += SafeWaitNanos;
        }
    }

    private void ApplyMotor(MotorCommand c)
    {
        var port = _hardware.MotorAt(c.Key);
        if (port is null)
        {
            return;
        }
        var controller = port.Controller;
        if (c.ResetCount != _resetCounts.GetValueOrDefault(c.Key))
        {
            controller.SetMode(RunMode.StopAndResetEncoder, port.OutputAngle);
            _resetCounts[c.Key] = c.ResetCount;
        }
        var mode = c.Mode switch
        {
            "RUN_WITHOUT_ENCODER" => RunMode.RunWithoutEncoder,
            "RUN_USING_ENCODER" => RunMode.RunUsingEncoder,
            "RUN_TO_POSITION" => RunMode.RunToPosition,
            "STOP_AND_RESET_ENCODER" => RunMode.StopAndResetEncoder,
            _ => throw new RunnerException($"unknown run mode '{c.Mode}' for motor {c.Key}"),
        };
        if (mode != controller.Mode)
        {
            controller.SetMode(mode, port.OutputAngle);
        }
        controller.ZeroPower = c.ZeroPower == "BRAKE" ? ZeroPowerBehavior.Brake : ZeroPowerBehavior.Float;
        controller.TargetPosition = c.TargetPosition;
        controller.TargetPositionTolerance = c.Tolerance;
        controller.Enabled = c.Enabled;
        controller.VelocityPidf = c.VelocityPidf is { } g ? new Pidf(g[0], g[1], g[2], g[3]) : _hub.DefaultVelocityPidf;
        controller.PositionPidf = double.IsNaN(c.PositionP) ? _hub.DefaultPositionPidf : _hub.DefaultPositionPidf with { P = c.PositionP };
        if (c.VelocitySet)
        {
            controller.SetVelocity(c.Velocity);
        }
        else
        {
            controller.SetPower(c.Power);
        }
    }

    private void ApplyServo(ServoCommand c)
    {
        var port = _hardware.ServoAt(c.Key);
        if (port is null)
        {
            return;
        }
        if (double.IsNaN(c.PulseUs))
        {
            port.Model.Enabled = false;   // never commanded: the hub sends no pulses
            return;
        }
        port.Model.SetPulse(c.PulseUs);
        port.Model.Enabled = c.PwmEnabled;
    }

    /// <summary>
    /// Rotation from the hub/IMU chip frame to the FTC robot frame for a RevHubOrientationOnRobot mounting: the chip's
    /// +Z points where the logo faces and +Y where the USB ports face (logo UP + USB FORWARD = identity), as in the
    /// SDK and the shim's SimImu.
    /// </summary>
    public static Quat HubMount(string logo, string usb)
    {
        var z = Direction(logo);
        var y = Direction(usb);
        if (Math.Abs(z.X * y.X + z.Y * y.Y + z.Z * y.Z) > 1e-9)
        {
            throw new RunnerException($"impossible hub mounting: logo {logo}, USB {usb}");
        }
        var x = new Vec3d(y.Y * z.Z - y.Z * z.Y, y.Z * z.X - y.X * z.Z, y.X * z.Y - y.Y * z.X);
        return Quat.FromAxes(x, y, z);
    }

    private static Vec3d Direction(string name) => name.ToUpperInvariant() switch
    {
        "UP" => new Vec3d(0, 0, 1),
        "DOWN" => new Vec3d(0, 0, -1),
        "FORWARD" => new Vec3d(0, 1, 0),
        "BACKWARD" => new Vec3d(0, -1, 0),
        "LEFT" => new Vec3d(-1, 0, 0),
        "RIGHT" => new Vec3d(1, 0, 0),
        _ => throw new RunnerException($"unknown hub direction '{name}'"),
    };
}
