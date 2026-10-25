namespace Sim.Hardware;

/// <summary>
/// Hobby servo on a hub servo port (config/servos.yaml). The SDK turns position 0..1 (Servo) or power −1..1
/// (CRServo) into a pulse width with PwmRange 600–2400 µs by default; the servo maps the pulse to a target angle
/// (degrees_per_us around the 1500 µs centre, limited to its travel) or, in continuous mode, to a speed within
/// its continuous PWM range. Speed and torque scale with the supply voltage (hub servo ports: 5 V).
/// Positive angle/speed = right-handed rotation about the servo axis in the robot YAML (ASSUMED convention).
/// </summary>
public sealed class ServoModel
{
    private const double CentreUs = 1500;

    public ServoModel(ServoSpec spec, ServoPortSpec port, bool continuous)
    {
        Spec = spec;
        Port = port;
        Continuous = continuous;
        TargetAngleDeg = 0;
    }

    public ServoSpec Spec { get; }
    public ServoPortSpec Port { get; }
    public bool Continuous { get; }

    /// <summary>PWM output enabled (Servo.getController().pwmEnable/disable, field disable).</summary>
    public bool Enabled { get; set; } = true;

    public double PulseUs { get; private set; } = CentreUs;
    public double AngleDeg { get; private set; }
    public double TargetAngleDeg { get; private set; }

    /// <summary>Signed output speed after the last <see cref="Step"/>.</summary>
    public double SpeedDegPerS { get; private set; }

    public double NoLoadSpeedDegPerS(double volts) => Spec.At(Spec.NoLoadSpeedRpm, volts) * 6.0;   // rpm → °/s

    public double StallTorqueNm(double volts) => Spec.At(Spec.StallTorqueNm, volts);

    /// <summary>Servo.setPosition(p): p is clipped to 0..1 and scaled into the SDK PWM range.</summary>
    public void SetPosition(double position)
    {
        var p = Math.Clamp(position, 0, 1);
        SetPulse(Port.PulseLowerUs + p * (Port.PulseUpperUs - Port.PulseLowerUs));
    }

    /// <summary>Raw pulse width from the hub port (the SDK already applied direction, scaleRange and PwmRange).</summary>
    public void SetPulse(double pulseUs)
    {
        PulseUs = pulseUs;
        var half = Spec.TravelDeg / 2;
        TargetAngleDeg = Math.Clamp((PulseUs - CentreUs) * Spec.DegreesPerUs, -half, half);
    }

    /// <summary>CRServo.setPower(p): −1..1 mapped like a position of (p + 1) / 2.</summary>
    public void SetPower(double power) => SetPosition((Math.Clamp(power, -1, 1) + 1) / 2);

    /// <summary>Moves the output for one step against a load torque (N·m, opposing positive rotation when positive).</summary>
    public void Step(double dt, double loadTorqueNm = 0, double? supplyVolts = null)
    {
        if (!Enabled)
        {
            SpeedDegPerS = 0;
            return;
        }
        var volts = supplyVolts ?? Port.SupplyVoltage;
        var maxSpeed = NoLoadSpeedDegPerS(volts);
        double command;   // −1..1 fraction of full drive
        if (Continuous)
        {
            var range = Spec.ContinuousPwmRangeUs;
            var halfSpan = (range[1] - range[0]) / 2;
            var offset = PulseUs - CentreUs;
            command = Math.Abs(offset) <= Spec.DeadbandUs / 2 ? 0 : Math.Clamp(offset / halfSpan, -1, 1);
        }
        else
        {
            var error = TargetAngleDeg - AngleDeg;
            command = Math.Clamp(error / Spec.ProportionalBandDeg, -1, 1);
        }
        // Linear torque–speed line: available speed shrinks as the load approaches the stall torque.
        var stall = StallTorqueNm(volts);
        var resisting = command == 0 ? 0 : loadTorqueNm * Math.Sign(command);
        var headroom = Math.Clamp(1 - resisting / stall, 0, 2);
        var speed = command * maxSpeed * headroom;
        if (!Continuous)
        {
            var error = TargetAngleDeg - AngleDeg;
            if (Math.Abs(speed * dt) > Math.Abs(error))
            {
                speed = error / dt;   // land exactly on the target instead of overshooting it
            }
        }
        SpeedDegPerS = speed;
        AngleDeg += speed * dt;
        if (!Continuous)
        {
            AngleDeg = Math.Clamp(AngleDeg, -Spec.TravelDeg / 2, Spec.TravelDeg / 2);
        }
    }
}
