using Sim.Hardware;

namespace Sim.Robots;

/// <summary>
/// Flat-floor robot simulation for benchmarks and tests (spec 05 "physics benchmarks", spec 12 layer 7):
/// rigid chassis in the ground plane (x, z, yaw) driven by a <see cref="RobotPowertrain"/>, normal forces from the
/// static load split plus weight transfer. The Godot robot uses the same powertrain with normal forces from its wheel
/// raycasts.
/// </summary>
public sealed class PlanarRobotSim
{
    private const double Gravity = 9.80665;
    private Vec2d _lastAccel;   // robot frame, for weight transfer

    public PlanarRobotSim(RobotDefinition robot, HardwareConfig hardware, DriveContact contact, int tickRate = 240)
    {
        Robot = robot;
        TickRate = tickRate;
        Powertrain = new RobotPowertrain(robot, hardware, contact, tickRate);
    }

    public RobotDefinition Robot { get; }
    public int TickRate { get; }
    public RobotPowertrain Powertrain { get; }
    public MecanumDrivetrain Drive => Powertrain.Drive;
    public Battery Battery => Powertrain.Battery;
    public RobotHardware Hardware => Powertrain.Hardware;

    /// <summary>World pose: X right of the audience view, Z towards the audience, yaw CCW seen from above.</summary>
    public Vec2d Position { get; private set; }
    public double Yaw { get; private set; }

    /// <summary>World-frame planar velocity and yaw rate.</summary>
    public Vec2d Velocity { get; private set; }
    public double YawRate { get; private set; }
    public double Time { get; private set; }

    /// <summary>Velocity in the robot frame (X right, Z back).</summary>
    public Vec2d RobotVelocity => Velocity.Rotated(-Yaw);

    public HubMotorController Controller(string motorName) => Powertrain.Controller(motorName);

    /// <inheritdoc cref="RobotPowertrain.SetPowerFromSdk"/>
    public void SetPowerFromSdk(string motorName, double power) => Powertrain.SetPowerFromSdk(motorName, power);

    /// <inheritdoc cref="RobotPowertrain.MecanumDrive"/>
    public void MecanumDrive(double forward, double strafe, double rotate) => Powertrain.MecanumDrive(forward, strafe, rotate);

    public void Step()
    {
        var dt = 1.0 / TickRate;
        var forces = Powertrain.Step(RobotVelocity, YawRate, NormalForces());

        // Newton–Euler in the plane about the centre of mass (robot frame), then to the world frame.
        var com = new Vec2d(Robot.CenterOfMass.X, Robot.CenterOfMass.Z);
        var total = new Vec2d(0, 0);
        var torque = 0.0;
        foreach (var f in forces)
        {
            total += f.Force;
            var r = f.Point - com;
            torque += r.Z * f.Force.X - r.X * f.Force.Z;   // (r × F)·Y with x right, z back
        }
        var accelRobot = total * (1.0 / Robot.MassKg);
        _lastAccel = accelRobot;
        Velocity += accelRobot.Rotated(Yaw) * dt;
        YawRate += torque / Robot.Inertia.Y * dt;
        Position += Velocity * dt;
        Yaw += YawRate * dt;
        Time += dt;
    }

    /// <summary>Static split of the weight over the four wheels from the centre-of-mass position, plus
    /// longitudinal/lateral load transfer m·a·h / (wheelbase or track) from the previous tick's acceleration.</summary>
    private double[] NormalForces()
    {
        var w = Drive.Wheels;
        var xs = w.Select(x => (double)x.Def.Position.X).ToArray();
        var zs = w.Select(x => (double)x.Def.Position.Z).ToArray();
        var track = xs.Max() - xs.Min();
        var wheelbase = zs.Max() - zs.Min();
        var cx = Robot.CenterOfMass.X - (xs.Max() + xs.Min()) / 2;
        var cz = Robot.CenterOfMass.Z - (zs.Max() + zs.Min()) / 2;
        var h = Robot.CenterOfMass.Y;
        var weight = Robot.MassKg * Gravity;
        var result = new double[w.Count];
        for (var i = 0; i < w.Count; i++)
        {
            var right = w[i].Def.Position.X > 0 ? 1 : -1;
            var back = w[i].Def.Position.Z > 0 ? 1 : -1;
            var share = 0.25 * (1 + 2 * right * cx / track) * (1 + 2 * back * cz / wheelbase);
            // Accelerating towards −Z (forward) loads the back wheels; towards +X (right) loads the left wheels.
            var transfer = Robot.MassKg * h * (-_lastAccel.Z * back / wheelbase - _lastAccel.X * right / track) / 2;
            result[i] = Math.Max(0, weight * share + transfer);
        }
        return result;
    }
}
