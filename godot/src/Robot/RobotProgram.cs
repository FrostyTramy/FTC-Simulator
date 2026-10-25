using System;
using Godot;
using Sim.App;
using Sim.DriverStation;
using Sim.Runner;

namespace Sim.Robot;

/// <summary>
/// Runs the team's OpModes in the runner JVM in lockstep with the Godot physics (docs/ipc_protocol.md) and serves the
/// Driver Station (<see cref="IRobotLink"/>). Per tick: TICK with the inputs sampled after the previous step, the
/// powertrain step with the commands already applied, then the STATE reply is applied. One JVM per robot session, so
/// static fields survive from Autonomous to TeleOp like on the Robot Controller; Restart Robot starts a new JVM.
/// </summary>
public sealed class RobotProgram : IRobotDriver, IRobotLink, IDisposable
{
    private readonly RobotBody _robot;
    private readonly GamepadInput _input;
    private readonly RunnerBridge _bridge;
    private readonly HardwareCosts _costs;
    private RunnerSession? _session;
    private TickResult? _pending;
    private string? _failure;   // INIT refused or runner lost: reported to the DS as a crash on the next tick

    public RobotProgram(RobotBody robot, GamepadInput input, string teamCodeJar)
    {
        _robot = robot;
        _input = input;
        TeamCodeJar = teamCodeJar;
        _bridge = new RunnerBridge(robot.Powertrain.Hardware, robot.HardwareConfig.HubController, robot.Sensors);
        _costs = HardwareCosts.Load(RepoPaths.Config("hardware_latency.yaml"));
        Ds = DriverStationApp.WithRules(this, Sim.Core.Config.RulesConfig.Load(RepoPaths.Config("rules_biobuzz.yaml")));
    }

    public string TeamCodeJar { get; private set; }
    public DriverStationApp Ds { get; }
    public TickResult? Last { get; private set; }

    /// <summary>"connected …" or the reason the runner is not available.</summary>
    public string Status { get; private set; } = "not started";

    public bool Connected => _session is { HasExited: false };

    public static long EndNanos(long tick) => (tick + 1) * 1_000_000_000L / Engine.PhysicsTicksPerSecond;

    /// <summary>Starts the runner JVM and lists the OpModes (blocks for about a second).</summary>
    public void Connect()
    {
        try
        {
            _session = RunnerSession.Start(RunnerLaunch.ForTeamCode(TeamCodeJar));
            Ds.SetOpModes(_session.ListOpModes());
            Status = $"connected (SDK {_session.SdkVersion}), {Ds.TeleOps.Count} TeleOp, {Ds.Autonomous.Count} Autonomous";
        }
        catch (RunnerException e)
        {
            _session = null;
            Status = e.Message;
            GD.PushWarning("runner: " + e.Message);
        }
    }

    /// <summary>Driver Station "Restart Robot": a fresh JVM (static fields are lost, as on the real Robot Controller).</summary>
    public void RestartRobot()
    {
        _session?.Dispose();
        _session = null;
        _pending = null;
        Connect();
        Ds.RobotRestarted();
    }

    /// <summary>"Run on robot" with new code (spec 07): a fresh JVM with the new jar and a refreshed OpMode list.</summary>
    public void ReloadTeamCode(string jar)
    {
        TeamCodeJar = jar;
        RestartRobot();
    }

    public void InitOpMode(OpModeInfo opMode)
    {
        if (_session == null)
        {
            _failure = "The robot is not connected: " + Status;
            return;
        }
        try
        {
            _bridge.BeginSession();
            _session.Configure(_costs, _robot.Powertrain.Hardware.HubCount, _bridge.Devices());
            _session.Init(opMode.ClassName);
        }
        catch (RunnerException e)
        {
            _failure = e.Message;
        }
    }

    public void StartOpMode() => Guard(s => s.Start());

    public void StopOpMode() => Guard(s => s.Stop());

    public void BeforeStep(RobotBody robot, long tick)
    {
        if (_session == null)
        {
            return;
        }
        var (orientation, rate) = _bridge.Imu(RobotOrientation(robot), RobotAngularVelocity(robot));
        var input = new TickInput(tick, EndNanos(tick), _input.Gamepad1, _input.Gamepad2, robot.Powertrain.Battery.BusVolts,
            orientation, rate, _bridge.Inputs(), _bridge.SensorInputs());
        try
        {
            _pending = _session.Tick(input);
        }
        catch (RunnerException e)
        {
            Lost(e.Message);
        }
    }

    public void AfterStep(RobotBody robot, long tick)
    {
        var now = EndNanos(tick);
        if (_failure != null)
        {
            Ds.Update(RunnerState.Crashed, _failure, now);
            _failure = null;
        }
        if (_pending is { } result)
        {
            _bridge.Apply(result, now);
            Ds.Update(result.State, result.Error, now);
            Last = result;
            _pending = null;
        }
        else
        {
            _bridge.StopRobot(now);
        }
    }

    public void Dispose()
    {
        _session?.Dispose();
        _session = null;
    }

    private void Guard(Action<RunnerSession> action)
    {
        if (_session == null)
        {
            return;
        }
        try
        {
            action(_session);
        }
        catch (RunnerException e)
        {
            Lost(e.Message);
        }
    }

    private void Lost(string message)
    {
        _failure = "The robot controller stopped responding: " + message;
        Status = message;
        _session?.Dispose();
        _session = null;
        _pending = null;
    }

    /// <summary>
    /// Robot orientation in the FTC convention: robot frame +X right, +Y forward, +Z up, in a Z-up world. Godot's robot
    /// frame is +X right, +Y up, −Z forward in a Y-up world; both map with (x, y, z) → (x, −z, y).
    /// </summary>
    private static Quat RobotOrientation(RobotBody robot)
    {
        var b = robot.GlobalBasis;
        static Vec3d C(Vector3 v) => new(v.X, -v.Z, v.Y);
        return Quat.FromAxes(C(b.X.Normalized()), C(-b.Z.Normalized()), C(b.Y.Normalized()));
    }

    /// <summary>Angular velocity about the FTC robot axes (x right, y forward, z up).</summary>
    private static Vec3d RobotAngularVelocity(RobotBody robot)
    {
        var local = robot.GlobalBasis.Inverse() * robot.AngularVelocity;
        return new Vec3d(local.X, -local.Z, local.Y);
    }
}
