using System.Numerics;
using Sim.Robots;

namespace SensorTests;

/// <summary>A robot pose scripted by the test on an empty floor (optional walls/targets as planes), stepped at 240 Hz.</summary>
internal sealed class SensorTestWorld : ISensorWorld
{
    public const int TickRate = 240;
    public const double Dt = 1.0 / TickRate;
    public static readonly SensorModels Models = SensorModels.Load();
    public static readonly RobotDefinition Robot = RobotDefinition.Load(RobotDefinition.FindConfig("gobilda_starter_sensors"));

    public Vector3 RobotPosition { get; set; }
    public Quaternion RobotRotation { get; set; } = Quaternion.Identity;
    public double Time { get; private set; }

    /// <summary>Surfaces as (point on plane, normal, colour); rays hit a plane only from its normal side.</summary>
    public List<(Vector3 Point, Vector3 Normal, Vector3 Color)> Planes { get; } = [];

    /// <summary>Chassis tilt applied in the robot frame before the heading (e.g. driving up a ramp).</summary>
    public Quaternion Tilt { get; set; } = Quaternion.Identity;

    /// <summary>Heading counter-clockwise seen from above (radians), forward = −Z at 0.</summary>
    public double Heading
    {
        set => RobotRotation = Quaternion.CreateFromAxisAngle(Vector3.UnitY, (float)value) * Tilt;
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

    /// <summary>Steps the devices once at the current pose.</summary>
    public void Step(params SensorDevice[] devices)
    {
        Time += Dt;
        foreach (var d in devices)
        {
            d.Step(this, Time, Dt);
        }
    }

    /// <summary>Moves the robot origin by <paramref name="worldDelta"/> and turns by <paramref name="turn"/> over <paramref name="seconds"/>.</summary>
    public void Move(Vector3 worldDelta, double turn, double seconds, params SensorDevice[] devices)
    {
        var steps = (int)Math.Round(seconds * TickRate);
        var start = RobotPosition;
        var startHeading = HeadingOf(RobotRotation);
        for (var i = 1; i <= steps; i++)
        {
            RobotPosition = start + worldDelta * (i / (float)steps);
            Heading = startHeading + turn * i / steps;
            Step(devices);
        }
    }

    /// <summary>Lets time pass without motion.</summary>
    public void Wait(double seconds, params SensorDevice[] devices) => Move(Vector3.Zero, 0, seconds, devices);

    public static double HeadingOf(Quaternion q) => Poses.Heading(q);

    /// <summary>World direction of robot-frame forward at heading 0 is −Z; this gives the world vector for a robot-frame motion.</summary>
    public Vector3 RobotToWorld(Vector3 robotDelta) => Vector3.Transform(robotDelta, RobotRotation);

    public static SensorDef Sensor(string name) => Robot.Sensors.Single(s => s.Name == name);
}
