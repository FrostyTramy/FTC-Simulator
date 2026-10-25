using Sim.Hardware;

namespace Sim.Robots;

/// <summary>
/// One hub motor port, addressed like the SDK shim ("hub:port"): firmware controller, encoder and motor model.
/// Whoever simulates the motor's load (the drivetrain, or <see cref="RobotHardware"/> for mechanism motors) keeps
/// <see cref="OutputAngle"/> and <see cref="OutputOmega"/> current; positive = the direction of positive raw power.
/// </summary>
public sealed class MotorPort
{
    internal MotorPort(int index, int hub, int port, DeviceDef device, DcMotor motor, SdkMotorType sdkType, Encoder encoder,
        HubMotorController controller, double? loadInertia)
    {
        Index = index;
        Hub = hub;
        Port = port;
        Key = $"{hub}:{port}";
        Device = device;
        Motor = motor;
        SdkType = sdkType;
        Encoder = encoder;
        Controller = controller;
        LoadInertia = loadInertia;
    }

    /// <summary>Position in <see cref="RobotHardware.Motors"/> (and in the bus solve results).</summary>
    public int Index { get; }
    public int Hub { get; }
    public int Port { get; }
    public string Key { get; }
    public DeviceDef Device { get; }
    public DcMotor Motor { get; }
    public SdkMotorType SdkType { get; }
    public Encoder Encoder { get; }
    public HubMotorController Controller { get; }

    /// <summary>Inertia at the output shaft of a mechanism motor (mechanism × speed_ratio² + rotor); null for drive motors.</summary>
    public double? LoadInertia { get; }

    public double OutputAngle { get; set; }
    public double OutputOmega { get; set; }

    /// <summary>Duty and winding current of the last step (the current is what DcMotorEx.getCurrent reports).</summary>
    public double Duty { get; internal set; }
    public double CurrentA { get; internal set; }

    /// <summary>External torque on the output shaft for the next step (element contacts), N·m; cleared after use.</summary>
    public double LoadTorque { get; set; }
}

/// <summary>One hub servo port ("hub:port"). The hub's servo outputs start disabled until the SDK sends a pulse.</summary>
public sealed class ServoPort
{
    internal ServoPort(int hub, int port, DeviceDef device, ServoModel model)
    {
        Hub = hub;
        Port = port;
        Key = $"{hub}:{port}";
        Device = device;
        Model = model;
        Model.Enabled = false;
    }

    public int Hub { get; }
    public int Port { get; }
    public string Key { get; }
    public DeviceDef Device { get; }
    public ServoModel Model { get; }

    /// <summary>Load torque against positive servo rotation for the next step (element contacts), N·m; cleared after use.</summary>
    public double LoadTorque { get; set; }
}

/// <summary>
/// Every motor and servo port of a robot (robot YAML devices) with its hub firmware model, and the electrical step
/// that puts all motors on one battery bus (spec 04). Drive motors get their shaft state from the drivetrain;
/// motors that drive a mechanism (devices.drives "mechanism:&lt;id&gt;") spin a rigid shaft whose inertia comes
/// from the robot YAML mechanisms (until mechanisms become physics joints in Godot).
/// </summary>
public sealed class RobotHardware
{
    private const int MechanismSubsteps = 8;
    private readonly List<MotorPort> _motors = [];
    private readonly List<ServoPort> _servos = [];

    public RobotHardware(RobotDefinition robot, HardwareConfig hardware, int tickRate)
    {
        Robot = robot;
        HubCount = robot.Hubs.Count;
        foreach (var device in robot.Devices)
        {
            if (device.Type is not ("motor" or "servo" or "crservo"))
            {
                continue;
            }
            var hub = robot.HubIndex(device.Hub);
            var port = device.Port ?? throw new RobotConfigException($"{robot.Name}: device '{device.Name}' has no port");
            var model = device.Model ?? throw new RobotConfigException($"{robot.Name}: device '{device.Name}' has no model");
            if (device.Type == "motor")
            {
                var spec = hardware.Motor(model);
                var motor = new DcMotor(spec);
                var sdkType = hardware.SdkTypeOf(spec);
                var encoder = new Encoder(spec.CountsPerOutputRev, hardware.HubController.VelocityWindowS, tickRate);
                var controller = new HubMotorController(hardware.HubController, sdkType, encoder);
                _motors.Add(new MotorPort(_motors.Count, hub, port, device, motor, sdkType, encoder, controller,
                    LoadInertia(robot, device, motor)));
            }
            else
            {
                _servos.Add(new ServoPort(hub, port, device, new ServoModel(hardware.Servo(model), hardware.ServoPorts,
                    continuous: device.Type == "crservo")));
            }
        }
        var duplicate = _motors.Select(m => m.Key).Concat(_servos.Select(s => "servo " + s.Key))
            .GroupBy(k => k).FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            throw new RobotConfigException($"{robot.Name}: two devices on port {duplicate.Key}");
        }
    }

    public RobotDefinition Robot { get; }
    public int HubCount { get; }
    public IReadOnlyList<MotorPort> Motors => _motors;
    public IReadOnlyList<ServoPort> Servos => _servos;

    public MotorPort Motor(string name) =>
        _motors.FirstOrDefault(m => m.Device.Name == name) ?? throw new RobotConfigException($"{Robot.Name}: no motor '{name}'");

    public MotorPort? MotorAt(string key) => _motors.FirstOrDefault(m => m.Key == key);

    public ServoPort? ServoAt(string key) => _servos.FirstOrDefault(s => s.Key == key);

    /// <summary>Samples every encoder from its port's output angle (once per tick, before the controllers run).</summary>
    public void SampleEncoders()
    {
        foreach (var m in _motors)
        {
            m.Encoder.Sample(m.OutputAngle);
        }
    }

    /// <summary>Runs the hub firmware of every motor port. A browned-out robot outputs nothing.</summary>
    public void UpdateControllers(double dt, bool brownedOut)
    {
        foreach (var m in _motors)
        {
            m.Duty = brownedOut ? 0 : m.Controller.Update(dt);
        }
    }

    /// <summary>Solves the battery bus with all motors, advances the battery and returns each port's electrical state.</summary>
    public MotorElectrical[] SolveBus(Battery battery, double dt)
    {
        var loads = _motors.Select(m => new PowerBus.Load(m.Motor, m.Duty, m.OutputOmega, m.Controller.BrakeWhenIdle)).ToList();
        var (volts, current, electrical) = PowerBus.Solve(battery, loads);
        battery.Update(volts, current, dt);
        foreach (var m in _motors)
        {
            m.CurrentA = electrical[m.Index].CurrentA;
        }
        return electrical;
    }

    /// <summary>
    /// Spins the mechanism motors' shafts for one tick (substepped: the winding current is recomputed from the back-EMF,
    /// so stiff geared loads stay stable). Friction stops a coasting shaft instead of reversing it.
    /// </summary>
    public void StepMechanisms(MotorElectrical[] electrical, double dt)
    {
        var h = dt / MechanismSubsteps;
        foreach (var m in _motors)
        {
            if (m.LoadInertia is not { } inertia)
            {
                continue;
            }
            var applied = electrical[m.Index].AppliedVolts;
            var driving = m.Duty != 0;
            var brake = m.Controller.BrakeWhenIdle;
            var load = m.LoadTorque;
            m.LoadTorque = 0;
            double omega = m.OutputOmega, angle = m.OutputAngle;
            for (var s = 0; s < MechanismSubsteps; s++)
            {
                var current = driving ? m.Motor.Current(applied, omega) : brake ? m.Motor.Current(0, omega) : 0;
                var drive = m.Motor.Torque(current, omega);
                var next = omega + (drive + load) / inertia * h;
                if (omega != 0 && Math.Sign(next) != Math.Sign(omega) && Math.Abs(m.Motor.TorqueConstant * current + load) <= m.Motor.FrictionTorqueNm)
                {
                    next = 0;
                }
                omega = next;
                angle += omega * h;
            }
            m.OutputOmega = omega;
            m.OutputAngle = angle;
        }
    }

    /// <summary>Moves every servo for one tick against its load torque (element contacts).</summary>
    public void StepServos(double dt)
    {
        foreach (var s in _servos)
        {
            s.Model.Step(dt, s.LoadTorque);
            s.LoadTorque = 0;
        }
    }

    private static double? LoadInertia(RobotDefinition robot, DeviceDef device, DcMotor motor)
    {
        var drives = device.Drives ?? throw new RobotConfigException($"{robot.Name}: motor '{device.Name}' has no 'drives'");
        if (drives.StartsWith("wheel:", StringComparison.Ordinal))
        {
            return null;
        }
        if (!drives.StartsWith("mechanism:", StringComparison.Ordinal))
        {
            throw new RobotConfigException($"{robot.Name}: motor '{device.Name}' drives '{drives}' (expected wheel:* or mechanism:*)");
        }
        var mechanism = robot.Mechanism(drives["mechanism:".Length..]);
        var inertia = mechanism.InertiaKgM2
            ?? throw new RobotConfigException($"{robot.Name}: mechanism '{mechanism.Id}' driven by '{device.Name}' needs an inertia");
        return inertia * device.SpeedRatio * device.SpeedRatio + motor.ReflectedInertia;
    }
}
