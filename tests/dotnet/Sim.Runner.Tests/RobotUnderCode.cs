using Sim.Core.Config;
using Sim.Core.Events;

namespace RunnerTests;

/// <summary>
/// The goBILDA StarterBot on a flat floor (<see cref="PlanarRobotSim"/>) driven by OpMode code in the JVM runner, stepped
/// in lockstep as docs/ipc_protocol.md describes: TICK k carries the inputs sampled after step k−1, the physics step
/// uses the commands applied before, then the STATE reply is applied.
/// </summary>
internal sealed class RobotUnderCode : IDisposable
{
    public const int TickRate = 240;
    public static readonly HardwareConfig Hardware = HardwareConfig.Load();
    public static readonly RobotDefinition Robot = RobotDefinition.Load(RobotDefinition.FindConfig("gobilda_starter"));

    private RobotUnderCode(PlanarRobotSim sim, RunnerSession session, RunnerBridge bridge)
    {
        Sim = sim;
        Session = session;
        Bridge = bridge;
    }

    public PlanarRobotSim Sim { get; }
    public RunnerSession Session { get; }
    public RunnerBridge Bridge { get; }
    public long Tick { get; private set; }
    public TickResult? Last { get; private set; }

    /// <summary>Running hash of every STATE reply and the robot pose after every step (determinism checks).</summary>
    public StableHasher Trace { get; } = new();

    /// <summary>The example TeamCode compiled by java/build-adapter, unchanged.</summary>
    public static string TeamCodeJar => RepoPaths.Find(Path.Combine("java", "build-adapter", "build", "libs", "teamcode.jar"));

    /// <summary>Starts robot <paramref name="robot"/> (config/robots) running <paramref name="jar"/> (default: the example).</summary>
    public static RobotUnderCode Start(RobotDefinition? robot = null, string? jar = null)
    {
        var sim = new PlanarRobotSim(robot ?? Robot, Hardware, DriveContact.LoadDefault(), TickRate);
        var bridge = new RunnerBridge(sim.Hardware, Hardware.HubController);
        var session = RunnerSession.Start(RunnerLaunch.ForTeamCode(jar ?? TeamCodeJar));
        return new RobotUnderCode(sim, session, bridge);
    }

    public static long EndNanos(long tick) => (tick + 1) * 1_000_000_000L / TickRate;

    public double Seconds => Tick / (double)TickRate;

    /// <summary>Driver Station INIT for the OpMode with this Driver Station name.</summary>
    public void Init(string opModeName)
    {
        var opMode = Session.ListOpModes().Single(o => o.Name == opModeName);
        Bridge.BeginSession();
        Session.Configure(HardwareCosts.Load(), Sim.Hardware.HubCount, Bridge.Devices());
        Session.Init(opMode.ClassName);
    }

    public TickResult Step(GamepadState gamepad1)
    {
        var (orientation, rate) = Bridge.Imu(Quat.FromYaw(Sim.Yaw), new Vec3d(0, 0, Sim.YawRate));
        var input = new TickInput(Tick, EndNanos(Tick), gamepad1, GamepadState.Neutral, Sim.Battery.BusVolts, orientation, rate,
            Bridge.Inputs());
        var result = Session.Tick(input);
        Sim.Step();
        Bridge.Apply(result, EndNanos(Tick));
        Record(result);
        Tick++;
        Last = result;
        return result;
    }

    public TickResult Run(double seconds, GamepadState gamepad1)
    {
        TickResult? last = null;
        for (var i = 0; i < (int)Math.Round(seconds * TickRate); i++)
        {
            last = Step(gamepad1);
        }
        return last ?? throw new ArgumentOutOfRangeException(nameof(seconds));
    }

    public MotorCommand Motor(TickResult result, string name)
    {
        var key = Sim.Hardware.Motor(name).Key;
        return result.Motors.Single(m => m.Key == key);
    }

    public ServoCommand Servo(TickResult result, string name)
    {
        var key = Sim.Hardware.Servos.Single(s => s.Device.Name == name).Key;
        return result.Servos.Single(s => s.Key == key);
    }

    public void Dispose() => Session.Dispose();

    private void Record(TickResult r)
    {
        Trace.Add(r.NowNanos).Add((byte)r.State).Add(r.Error).Add(r.TelemetryTransmissions);
        foreach (var m in r.Motors)
        {
            Trace.Add(m.Key).Add(m.Mode).Add(m.Power).Add(m.VelocitySet).Add(m.Velocity).Add(m.TargetPosition).Add(m.ZeroPower)
                .Add(m.Tolerance).Add(m.Enabled).Add(m.ResetCount).Add(m.PositionP);
            foreach (var g in m.VelocityPidf ?? [])
            {
                Trace.Add(g);
            }
        }
        foreach (var s in r.Servos)
        {
            Trace.Add(s.Key).Add(s.PulseUs).Add(s.PwmEnabled);
        }
        foreach (var line in r.Telemetry)
        {
            Trace.Add(line);
        }
        Trace.Add(Sim.Position.X).Add(Sim.Position.Z).Add(Sim.Yaw).Add(Sim.Battery.BusVolts);
    }
}
