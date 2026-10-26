using Sim.Hardware;

namespace Sim.Robots;

/// <summary>Planar vector in the robot or world ground plane: X right, Z back (forward = −Z).</summary>
public readonly record struct Vec2d(double X, double Z)
{
    public static Vec2d operator +(Vec2d a, Vec2d b) => new(a.X + b.X, a.Z + b.Z);
    public static Vec2d operator -(Vec2d a, Vec2d b) => new(a.X - b.X, a.Z - b.Z);
    public static Vec2d operator *(Vec2d a, double s) => new(a.X * s, a.Z * s);
    public double Dot(Vec2d o) => X * o.X + Z * o.Z;
    public double Length => Math.Sqrt(X * X + Z * Z);

    /// <summary>Rotate by yaw (right-handed about +Y, i.e. counter-clockwise seen from above).</summary>
    public Vec2d Rotated(double yaw)
    {
        var (c, s) = (Math.Cos(yaw), Math.Sin(yaw));
        return new Vec2d(c * X + s * Z, -s * X + c * Z);
    }
}

/// <summary>Contact parameters of the drive wheels on the field tiles (config/physics.yaml contacts + drivetrain).</summary>
public sealed record DriveContact(
    double Friction,
    double FullSlipSpeed,
    double RollerRollingFriction,
    double BearingFrictionTorque,
    double GearEfficiency)
{
    /// <summary>Reads config/physics.yaml: dynamic friction of <paramref name="wheelMaterial"/> on foam_tile
    /// (contacts table), drivetrain.full_slip_speed, roller_rolling_friction, wheel_bearing_friction_torque
    /// and miter_gear_efficiency.</summary>
    public static DriveContact Load(string physicsYaml, string wheelMaterial = "mecanum_roller")
    {
        var root = new YamlDotNet.Serialization.DeserializerBuilder().Build()
            .Deserialize<Dictionary<object, object>>(File.ReadAllText(physicsYaml));
        double Num(object o) => double.Parse(o.ToString()!, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture);
        var drive = (Dictionary<object, object>)root["drivetrain"];
        double Q(string key) => Num(((Dictionary<object, object>)drive[key])["value"]);
        var pairs = (List<object>)((Dictionary<object, object>)root["contacts"])["pairs"];
        var pair = pairs.Cast<Dictionary<object, object>>().FirstOrDefault(p =>
            (p["a"].ToString(), p["b"].ToString()) is ("foam_tile", var b) && b == wheelMaterial
            || (p["b"].ToString(), p["a"].ToString()) is ("foam_tile", var a) && a == wheelMaterial)
            ?? throw new RobotConfigException($"physics.yaml: no contact pair foam_tile / {wheelMaterial}");
        return new DriveContact(Num(pair["dynamic_friction"]), Q("full_slip_speed"), Q("roller_rolling_friction"),
            Q("wheel_bearing_friction_torque"), Q("miter_gear_efficiency"));
    }

    /// <summary>config/physics.yaml next to the robot configs.</summary>
    public static DriveContact LoadDefault(string wheelMaterial = "mecanum_roller") =>
        Load(Path.Combine(Path.GetDirectoryName(Path.GetDirectoryName(RobotDefinition.FindConfig("gobilda_starter")))!, "physics.yaml"), wheelMaterial);
}

/// <summary>
/// Mecanum drivetrain force model (spec 04 Drivetrains, level 1 "force model"). Each wheel has an angular
/// speed state. Its rollers turn freely, so the ground can only push along the roller axis u (45° to the axle):
///   slip  s = v_contact − ω·R·f        (f = wheel forward direction, v_contact from the chassis motion)
///   F_u = −μN · sat(s·u / s₀)          (linear up to the full-slip speed s₀, then sliding friction μN)
///   F_p = −c_rr·N · sat(s·p / s₀)       (roller rolling loss along the free direction p ⟂ u)
///   J·dω/dt = τ_motor·η − R·(F·f) − τ_bearing
/// Wheels are integrated in substeps (chassis velocity held for one physics tick) for stability.
/// Roller axes follow the example code's mixing (CODE:255-260, forward + strafe right = LF+, RF−, LB−, RB+):
/// "left" slant pushes forward-right when the wheel turns forward, "right" slant forward-left.
/// </summary>
public sealed class MecanumDrivetrain
{
    public const int Substeps = 8;
    private static readonly double InvSqrt2 = 1 / Math.Sqrt(2);

    private readonly WheelState[] _wheels;

    public MecanumDrivetrain(RobotDefinition robot, HardwareConfig hardware, DriveContact contact)
    {
        Robot = robot;
        Contact = contact;
        _wheels = robot.Wheels.Select(w =>
        {
            var device = robot.Device(w.Motor);
            var motor = new DcMotor(hardware.Motor(device.Model!));
            var wheelInertia = 0.5 * robot.WheelMassKg * robot.WheelRadius * robot.WheelRadius;
            var reflected = motor.ReflectedInertia / (robot.DriveGearing * robot.DriveGearing);
            var axis = w.Slant switch
            {
                "left" => new Vec2d(InvSqrt2, -InvSqrt2),
                "right" => new Vec2d(-InvSqrt2, -InvSqrt2),
                _ => throw new RobotConfigException($"wheel {w.Id}: unknown slant '{w.Slant}'"),
            };
            return new WheelState(w, motor, wheelInertia + reflected, axis);
        }).ToArray();
    }

    public RobotDefinition Robot { get; }
    public DriveContact Contact { get; }
    public IReadOnlyList<WheelState> Wheels => _wheels;

    /// <summary>Per-wheel input for one tick: the electrical drive of its motor and the normal force.</summary>
    public readonly record struct WheelInput(double AppliedVolts, bool Driven, bool Brake, double NormalForce);

    /// <summary>Average ground force on the robot at each wheel's contact point over the tick (robot frame).</summary>
    public readonly record struct WheelForce(Vec2d Force, Vec2d Point);

    /// <summary>Motor output shaft speed (rad/s, motor's own positive sense) of wheel <paramref name="i"/>.</summary>
    public double MotorOmega(int i) => _wheels[i].Omega * _wheels[i].Def.DriveSign * Robot.DriveGearing;

    /// <summary>Wheel rotation angle (rad, forward positive), e.g. for the visual mesh and encoder.</summary>
    public double WheelAngle(int i) => _wheels[i].Angle;

    /// <summary>Wheel spin (rad/s, forward positive), e.g. for game elements touching the wheel.</summary>
    public double WheelOmega(int i) => _wheels[i].Omega;

    /// <param name="chassisVelocity">Planar velocity of the robot origin in the robot frame.</param>
    /// <param name="yawRate">Yaw rate (rad/s, counter-clockwise seen from above).</param>
    public WheelForce[] Step(double dt, Vec2d chassisVelocity, double yawRate, IReadOnlyList<WheelInput> inputs)
    {
        var h = dt / Substeps;
        var forward = new Vec2d(0, -1);
        var result = new WheelForce[_wheels.Length];
        for (var i = 0; i < _wheels.Length; i++)
        {
            var w = _wheels[i];
            var input = inputs[i];
            var point = new Vec2d(w.Def.Position.X, w.Def.Position.Z);
            // v = v_origin + ω × r  (ω about +Y): (x, z) → ω·(z, −x)
            var vContact = chassisVelocity + new Vec2d(yawRate * point.Z, -yawRate * point.X);
            var free = new Vec2d(-w.Axis.Z, w.Axis.X);
            var sum = new Vec2d(0, 0);
            for (var k = 0; k < Substeps; k++)
            {
                var slip = vContact - forward * (w.Omega * Robot.WheelRadius);
                var fu = -Contact.Friction * input.NormalForce * Sat(slip.Dot(w.Axis) / Contact.FullSlipSpeed);
                var fp = -Contact.RollerRollingFriction * input.NormalForce * Sat(slip.Dot(free) / Contact.FullSlipSpeed);
                var force = w.Axis * fu + free * fp;
                var motorOmega = w.Omega * w.Def.DriveSign * Robot.DriveGearing;
                var motorTorque = MotorTorque(w.Motor, input, motorOmega);
                var wheelTorque = motorTorque * w.Def.DriveSign / Robot.DriveGearing * Contact.GearEfficiency;
                var ground = -Robot.WheelRadius * force.Dot(forward);
                var bearing = Math.Abs(w.Omega) > 1e-6 ? -Contact.BearingFrictionTorque * Math.Sign(w.Omega) : 0;
                w.Omega += (wheelTorque + ground + bearing) / w.Inertia * h;
                w.Angle += w.Omega * h;
                sum += force;
            }
            result[i] = new WheelForce(sum * (1.0 / Substeps), point);
        }
        return result;
    }

    private static double MotorTorque(DcMotor motor, WheelInput input, double omega)
    {
        if (input.Driven)
        {
            return motor.Torque(motor.Current(input.AppliedVolts, omega), omega);
        }
        return input.Brake ? motor.Torque(motor.Current(0, omega), omega) : motor.Torque(0, omega);
    }

    private static double Sat(double x) => Math.Clamp(x, -1, 1);

    /// <summary>Mutable state of one wheel.</summary>
    public sealed class WheelState(WheelDef def, DcMotor motor, double inertia, Vec2d axis)
    {
        public WheelDef Def { get; } = def;
        public DcMotor Motor { get; } = motor;
        public double Inertia { get; } = inertia;

        /// <summary>Roller axis in the ground plane (robot frame).</summary>
        public Vec2d Axis { get; } = axis;

        public double Omega { get; set; }
        public double Angle { get; set; }
    }
}
