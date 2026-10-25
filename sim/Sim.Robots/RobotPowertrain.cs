using Sim.Hardware;

namespace Sim.Robots;

/// <summary>
/// Everything that turns hub commands into wheel forces for one tick, shared by <see cref="PlanarRobotSim"/> and the
/// Godot robot: hub firmware for every port (<see cref="RobotHardware"/>), the battery bus, the mecanum wheels, the
/// mechanism shafts and the servos. The caller supplies the chassis motion and the wheel normal forces (flat-floor
/// estimate or Godot raycasts) and applies the returned forces to its chassis.
/// </summary>
public sealed class RobotPowertrain
{
    private readonly MotorPort[] _drivePorts;

    public RobotPowertrain(RobotDefinition robot, HardwareConfig hardware, DriveContact contact, int tickRate)
    {
        Robot = robot;
        TickRate = tickRate;
        Drive = new MecanumDrivetrain(robot, hardware, contact);
        Battery = new Battery(hardware.Batteries[robot.BatteryModel]);
        Hardware = new RobotHardware(robot, hardware, tickRate);
        _drivePorts = Drive.Wheels.Select(w => Hardware.Motor(w.Def.Motor)).ToArray();
        foreach (var port in Hardware.Motors)
        {
            // Direct drive (Phase 5) stands in for the robot's code: apply the zero-power behaviour its init sets
            // (robot YAML devices.zero_power). Code running in the runner overrides it with its own commands.
            port.Controller.ZeroPower = port.Device.ZeroPower == "BRAKE" ? ZeroPowerBehavior.Brake : ZeroPowerBehavior.Float;
        }
    }

    public RobotDefinition Robot { get; }
    public int TickRate { get; }
    public MecanumDrivetrain Drive { get; }
    public Battery Battery { get; }
    public RobotHardware Hardware { get; }

    public HubMotorController Controller(string motorName) => Hardware.Motor(motorName).Controller;

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

    /// <summary>CRServo.setPower(power) with the configured direction (REVERSE negates); turns the PWM output on.</summary>
    public void SetCrServoFromSdk(string servoName, double power)
    {
        var port = Hardware.Servos.FirstOrDefault(s => s.Device.Name == servoName)
            ?? throw new RobotConfigException($"{Robot.Name}: no servo '{servoName}'");
        port.Model.SetPower(port.Device.Direction == "REVERSE" ? -power : power);
        port.Model.Enabled = true;
    }

    /// <summary>
    /// DcMotorEx in RUN_USING_ENCODER with velocity PIDF <paramref name="pidf"/> and setVelocity(ticksPerSecond), the
    /// configured direction applied (as the example's launcher, CODE:145-147, 278).
    /// </summary>
    public void SetVelocityFromSdk(string motorName, double ticksPerSecond, Pidf pidf)
    {
        var port = Hardware.Motor(motorName);
        if (port.Controller.Mode != RunMode.RunUsingEncoder)
        {
            port.Controller.SetMode(RunMode.RunUsingEncoder, port.OutputAngle);
        }
        port.Controller.VelocityPidf = pidf;
        port.Controller.SetVelocity(port.Device.Direction == "REVERSE" ? -ticksPerSecond : ticksPerSecond);
    }

    /// <summary>DcMotorEx.getVelocity() in the motor's configured direction, ticks/s (hub reading).</summary>
    public double VelocityFromSdk(string motorName)
    {
        var port = Hardware.Motor(motorName);
        var raw = Math.Round(port.Encoder.VelocityCountsPerS);
        return port.Device.Direction == "REVERSE" ? -raw : raw;
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

    /// <summary>
    /// One tick: encoders, firmware, battery bus, wheels, mechanisms, servos.
    /// </summary>
    /// <param name="chassisVelocity">Planar velocity of the robot origin in the robot frame (X right, Z back).</param>
    /// <param name="yawRate">Rad/s, counter-clockwise seen from above.</param>
    /// <param name="normalForces">Ground normal force on each drive wheel (robot YAML wheel order), N.</param>
    /// <returns>Ground force on the robot at each wheel's contact point, robot frame.</returns>
    public MecanumDrivetrain.WheelForce[] Step(Vec2d chassisVelocity, double yawRate, IReadOnlyList<double> normalForces)
    {
        var dt = 1.0 / TickRate;
        var wheels = Drive.Wheels;
        for (var i = 0; i < wheels.Count; i++)
        {
            _drivePorts[i].OutputAngle = Drive.WheelAngle(i) * wheels[i].Def.DriveSign * Robot.DriveGearing;   // motor output angle
            _drivePorts[i].OutputOmega = Drive.MotorOmega(i);
        }
        Hardware.SampleEncoders();
        Hardware.UpdateControllers(dt, Battery.BrownedOut);
        var electrical = Hardware.SolveBus(Battery, dt);
        var inputs = wheels.Select((w, i) =>
        {
            var port = _drivePorts[i];
            return new MecanumDrivetrain.WheelInput(electrical[port.Index].AppliedVolts, port.Duty != 0, port.Controller.BrakeWhenIdle,
                normalForces[i]);
        }).ToList();
        var forces = Drive.Step(dt, chassisVelocity, yawRate, inputs);
        Hardware.StepMechanisms(electrical, dt);
        Hardware.StepServos(dt);
        return forces;
    }
}
