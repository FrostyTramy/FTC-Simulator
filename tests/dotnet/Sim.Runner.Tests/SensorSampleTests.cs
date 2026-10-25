using System.Globalization;
using System.Numerics;
using Sim.Core.Config;
using Sim.Sensors;

namespace RunnerTests;

/// <summary>
/// Phase 9 acceptance (specs/13-phases.md "Pinpoint sample runs"): FTC SDK 12.0 sensor samples, copied into TeamCode the
/// way a team does (tools/compat/import_sdk_samples.py: package + @Disabled only), run in the runner against the
/// gobilda_starter_sensors robot. The robot is moved by hand (a scripted pose, like "Push your robot around to see it
/// track"); the sensor models step from that ground truth.
/// </summary>
[Collection("runner")]
public class SensorSampleTests
{
    private static readonly Lazy<string> SamplesJar = new(() =>
    {
        var src = RepoPaths.Find(Path.Combine("tests", "compat", "sdk_samples", "TeamCode", "src", "main", "java"));
        var build = Path.Combine(Path.GetDirectoryName(RepoPaths.Find("config"))!, "generated", "build", "sdk_samples");
        var result = TeamCodeBuilder.Build(src, build, TimeSpan.FromMinutes(10));
        Assert.True(result.Success, string.Join("\n", result.Errors.Select(e => $"{e.File}:{e.Line} {e.Message}")) + result.Output);
        return result.JarPath!;
    });

    /// <summary>Caption → value of every telemetry item (addLine().addData() items share a line, joined by " | ").</summary>
    private static Dictionary<string, string> Telemetry(TickResult r) => r.Telemetry
        .SelectMany(l => l.Split(" | "))
        .Select(l => l.Split(" : ", 2))
        .Where(p => p.Length == 2)
        .GroupBy(p => p[0].Trim())
        .ToDictionary(g => g.Key, g => g.Last()[1].Trim());

    private static double Number(TickResult r, string caption) =>
        double.Parse(Telemetry(r)[caption], NumberStyles.Float, CultureInfo.InvariantCulture);

    [Fact]
    public void Pinpoint_sample_tracks_a_robot_pushed_around_by_hand()
    {
        using var robot = SensorRobot.Start(SamplesJar.Value);
        robot.Init("Sensor: GoBilda Pinpoint");
        robot.Run(0.4);                           // init(): configure, setPosition(0,0,0); device calibrated after 0.25 s
        robot.Session.Start();
        robot.Run(0.5);
        robot.Move(new Vector3(0, 0, -1), 0, 2.0);      // push 1 m forward
        var r = robot.Run(0.3);
        Assert.Equal(39.37, Number(r, "X coordinate (IN)"), 0.2);
        Assert.Equal(0, Number(r, "Y coordinate (IN)"), 0.2);
        Assert.Equal(0, Number(r, "Heading angle (DEGREES)"), 0.5);
        robot.Move(Vector3.Zero, Math.PI / 2, 1.5);     // turn 90° counter-clockwise in place
        robot.Move(new Vector3(0, 0, -0.5f), 0, 1.0);    // and push 0.5 m forward (now along +Y)
        r = robot.Run(0.3);
        Assert.Equal(90, Number(r, "Heading angle (DEGREES)"), 1.0);
        Assert.Equal(39.37, Number(r, "X coordinate (IN)"), 0.5);
        Assert.Equal(19.69, Number(r, "Y coordinate (IN)"), 0.5);
    }

    [Fact]
    public void Otos_sample_tracks_position_in_inches_and_degrees()
    {
        using var robot = SensorRobot.Start(SamplesJar.Value);
        robot.Init("Sensor: SparkFun OTOS");
        robot.Run(1.0);                          // configureOtos(): calibrateImu() waits ~0.6 s, resetTracking
        robot.Session.Start();
        robot.Run(0.3);
        robot.Move(new Vector3(0, 0, -0.5f), 0, 1.0);
        robot.Move(new Vector3(0.25f, 0, 0), 0, 1.0);   // strafe right
        var r = robot.Run(0.3);
        Assert.Equal(19.69, Number(r, "Y coordinate"), 0.3);   // OTOS: +Y forward
        Assert.Equal(9.84, Number(r, "X coordinate"), 0.3);    // +X right
        Assert.Equal(0, Number(r, "Heading angle"), 0.5);
    }

    [Fact]
    public void Distance_touch_and_digital_samples_read_the_scene()
    {
        using var robot = SensorRobot.Start(SamplesJar.Value);
        robot.World.Planes.Add((new Vector3(0, 0, -0.726f), Vector3.UnitZ, Vector3.One));     // wall 0.5 m ahead of the sensor
        robot.Init("Sensor: REV2mDistance");
        robot.Run(0.2);
        robot.Session.Start();
        var r = robot.Run(0.3);
        Assert.Contains(r.Telemetry, l => l.StartsWith("deviceName : REV 2M ToF Distance Sensor"));
        Assert.Contains(r.Telemetry, l => l.StartsWith("ID : ee"));
        var mm = r.Telemetry.First(l => l.StartsWith("range") && l.EndsWith(" mm"));
        Assert.InRange(double.Parse(mm.Split(':')[1].Trim().Split(' ')[0], CultureInfo.InvariantCulture), 450, 550);
    }

    [Fact]
    public void Color_sample_runs_with_the_robot_controller_screen_stand_in()
    {
        using var robot = SensorRobot.Start(SamplesJar.Value);
        robot.World.Planes.Add((new Vector3(0, 0.06f, 0), Vector3.UnitY, new Vector3(0.9f, 0.8f, 0.05f)));   // yellow below
        robot.Init("Sensor: Color");
        robot.Run(0.2);
        robot.Session.Start();
        var r = robot.Run(0.5);
        Assert.True(r.State == RunnerState.Running, r.Error);
        Assert.InRange(Number(r, "Distance (cm)"), 1.5, 2.5);
        Assert.InRange(Number(r, "Hue"), 40, 70);          // yellow
    }
}

/// <summary>The sensor robot in the runner, its pose scripted by the test (no drivetrain physics).</summary>
internal sealed class SensorRobot : IDisposable
{
    private SensorRobot(RunnerSession session, RunnerBridge bridge, SensorSuite sensors, ScriptedWorld world, RobotHardware hardware)
    {
        Session = session;
        Bridge = bridge;
        Sensors = sensors;
        World = world;
        Hardware = hardware;
    }

    public RunnerSession Session { get; }
    public RunnerBridge Bridge { get; }
    public SensorSuite Sensors { get; }
    public ScriptedWorld World { get; }
    public RobotHardware Hardware { get; }
    public long Tick { get; private set; }

    public static SensorRobot Start(string teamCodeJar)
    {
        var definition = RobotDefinition.Load(RobotDefinition.FindConfig("gobilda_starter_sensors"));
        var sim = new PlanarRobotSim(definition, RobotUnderCode.Hardware, DriveContact.LoadDefault(), RobotUnderCode.TickRate);
        var sensors = new SensorSuite(definition, SensorModels.Load(), 2026);
        var bridge = new RunnerBridge(sim.Hardware, RobotUnderCode.Hardware.HubController, sensors);
        var session = RunnerSession.Start(RunnerLaunch.ForTeamCode(teamCodeJar));
        return new SensorRobot(session, bridge, sensors, new ScriptedWorld(), sim.Hardware);
    }

    public void Init(string opModeName)
    {
        var opMode = Session.ListOpModes().Single(o => o.Name == opModeName);
        Bridge.BeginSession();
        Session.Configure(HardwareCosts.Load(), Hardware.HubCount, Bridge.Devices());
        Session.Init(opMode.ClassName);
    }

    public TickResult Step()
    {
        var endNanos = RobotUnderCode.EndNanos(Tick);
        var input = new TickInput(Tick, endNanos, GamepadState.Neutral, GamepadState.Neutral, 12.5, Quat.Identity, new Vec3d(0, 0, 0),
            Bridge.Inputs(), Bridge.SensorInputs());
        var result = Session.Tick(input);
        Sensors.Step(World, endNanos / 1e9, 1.0 / RobotUnderCode.TickRate);
        Bridge.Apply(result, endNanos);
        Tick++;
        return result;
    }

    public TickResult Run(double seconds)
    {
        TickResult? last = null;
        for (var i = 0; i < (int)Math.Round(seconds * RobotUnderCode.TickRate); i++)
        {
            last = Step();
        }
        return last!;
    }

    /// <summary>Moves the robot by a robot-frame displacement (at the start heading) and turns, over <paramref name="seconds"/>.</summary>
    public TickResult Move(Vector3 robotDelta, double turn, double seconds)
    {
        var steps = (int)Math.Round(seconds * RobotUnderCode.TickRate);
        var start = World.RobotPosition;
        var startHeading = World.Heading;
        var worldDelta = Vector3.Transform(robotDelta, World.RobotRotation);
        TickResult? last = null;
        for (var i = 1; i <= steps; i++)
        {
            World.RobotPosition = start + worldDelta * (i / (float)steps);
            World.Heading = startHeading + turn * i / steps;
            last = Step();
        }
        return last!;
    }

    public void Dispose() => Session.Dispose();
}

/// <summary>A scripted robot pose on an empty floor with optional planes (walls, targets).</summary>
internal sealed class ScriptedWorld : ISensorWorld
{
    private double _heading;

    public Vector3 RobotPosition { get; set; }
    public Quaternion RobotRotation { get; private set; } = Quaternion.Identity;
    public List<(Vector3 Point, Vector3 Normal, Vector3 Color)> Planes { get; } = [];

    public double Heading
    {
        get => _heading;
        set
        {
            _heading = value;
            RobotRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)value);
        }
    }

    public SurfaceHit? Raycast(Vector3 origin, Vector3 direction, float maxDistance)
    {
        SurfaceHit? best = null;
        var dir = Vector3.Normalize(direction);
        foreach (var (point, normal, color) in Planes)
        {
            var denom = Vector3.Dot(dir, normal);
            if (denom >= -1e-6f)
            {
                continue;
            }
            var t = Vector3.Dot(point - origin, normal) / denom;
            if (t >= 0 && t <= maxDistance && (best is null || t < best.Value.Distance))
            {
                best = new SurfaceHit(t, normal, color);
            }
        }
        return best;
    }
}
