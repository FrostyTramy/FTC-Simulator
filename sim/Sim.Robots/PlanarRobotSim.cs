using Sim.Hardware;

namespace Sim.Robots;

/// <summary>
/// Flat-floor robot simulation for benchmarks and tests (spec 05 "physics benchmarks", spec 12 layer 7):
/// rigid chassis in the ground plane (x, z, yaw) driven by <see cref="MecanumDrivetrain"/>, motors on one
/// battery bus through REV hub controllers, normal forces from the static load split plus weight transfer.
/// The Godot robot uses the same drivetrain model with normal forces from its wheel raycasts.
/// </summary>
public sealed class PlanarRobotSim
{
    private const double Gravity = 9.80665;
    private readonly HubMotorController[] _controllers;
    private readonly Encoder[] _encoders;
    private Vec2d _lastAccel;   // robot frame, for weight transfer

    public PlanarRobotSim(RobotDefinition robot, HardwareConfig hardware, DriveContact contact, int tickRate = 240)
    {
        Robot = robot;
        TickRate = tickRate;
        Drive = new MecanumDrivetrain(robot, hardware, contact);
        Battery = new Battery(hardware.Batteries[robot.BatteryModel]);
        _encoders = Drive.Wheels.Select(w => new Encoder(w.Motor.Spec.CountsPerOutputRev, hardware.HubController.VelocityWindowS, tickRate)).ToArray();
        _controllers = Drive.Wheels.Select((w, i) =>
        {
            var device = robot.Device(w.Def.Motor);
            var c = new HubMotorController(hardware.HubController, hardware.SdkTypeOf(w.Motor.Spec), _encoders[i])
            {
                ZeroPower = device.ZeroPower == "BRAKE" ? ZeroPowerBehavior.Brake : ZeroPowerBehavior.Float,
            };
            return c;
        }).ToArray();
    }

    public RobotDefinition Robot { get; }
    public int TickRate { get; }
    public MecanumDrivetrain Drive { get; }
    public Battery Battery { get; }

    /// <summary>World pose: X right of the audience view, Z towards the audience, yaw CCW seen from above.</summary>
    public Vec2d Position { get; private set; }
    public double Yaw { get; private set; }

    /// <summary>World-frame planar velocity and yaw rate.</summary>
    public Vec2d Velocity { get; private set; }
    public double YawRate { get; private set; }
    public double Time { get; private set; }

    /// <summary>Velocity in the robot frame (X right, Z back).</summary>
    public Vec2d RobotVelocity => Velocity.Rotated(-Yaw);

    public HubMotorController Controller(string motorName) =>
        _controllers[Array.FindIndex(Drive.Wheels.ToArray(), w => w.Def.Motor == motorName)];

    /// <summary>
    /// What the SDK sends to the hub for DcMotor.setPower(power) with the configured direction: DcMotorImpl
    /// reverses REVERSE motors of CCW motor types (goBILDA 5202/3/4 series orientation CCW, config/motors.yaml).
    /// </summary>
    public void SetPowerFromSdk(string motorName, double power)
    {
        var device = Robot.Device(motorName);
        var raw = device.Direction == "REVERSE" ? -power : power;
        Controller(motorName).SetPower(raw);
    }

    /// <summary>The goBILDA example's mecanum mixing (CODE:255-262): forward, strafe right, rotate right.</summary>
    public void MecanumDrive(double forward, double strafe, double rotate)
    {
        var lf = forward + strafe + rotate;
        var rf = forward - strafe - rotate;
        var lb = forward - strafe + rotate;
        var rb = forward + strafe - rotate;
        var max = Math.Max(1.0, Math.Max(Math.Max(Math.Abs(lf), Math.Abs(rf)), Math.Max(Math.Abs(lb), Math.Abs(rb))));
        SetPowerFromSdk("left_front_drive", lf / max);
        SetPowerFromSdk("right_front_drive", rf / max);
        SetPowerFromSdk("left_back_drive", lb / max);
        SetPowerFromSdk("right_back_drive", rb / max);
    }

    public void Step()
    {
        var dt = 1.0 / TickRate;
        var wheels = Drive.Wheels;
        var duties = new double[wheels.Count];
        var loads = new List<PowerBus.Load>(wheels.Count);
        for (var i = 0; i < wheels.Count; i++)
        {
            _encoders[i].Sample(Drive.WheelAngle(i) * wheels[i].Def.DriveSign * Robot.DriveGearing);   // motor output angle
            duties[i] = Battery.BrownedOut ? 0 : _controllers[i].Update(dt);
            loads.Add(new PowerBus.Load(wheels[i].Motor, duties[i], Drive.MotorOmega(i), _controllers[i].BrakeWhenIdle));
        }
        var (volts, current, electrical) = PowerBus.Solve(Battery, loads);
        Battery.Update(volts, current, dt);

        var normals = NormalForces();
        var inputs = wheels.Select((w, i) => new MecanumDrivetrain.WheelInput(electrical[i].AppliedVolts, duties[i] != 0,
            _controllers[i].BrakeWhenIdle, normals[i])).ToList();
        var forces = Drive.Step(dt, RobotVelocity, YawRate, inputs);

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
