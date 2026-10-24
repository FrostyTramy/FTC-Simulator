namespace Sim.Hardware;

/// <summary>DcMotor.RunMode of the FTC SDK.</summary>
public enum RunMode
{
    RunWithoutEncoder,
    RunUsingEncoder,
    RunToPosition,
    StopAndResetEncoder,
}

/// <summary>DcMotor.ZeroPowerBehavior of the FTC SDK.</summary>
public enum ZeroPowerBehavior
{
    Brake,
    Float,
}

/// <summary>
/// Quadrature encoder on the motor (config/motors.yaml counts_per_output_rev): integer counts from the output
/// shaft angle; velocity as the REV hub reports it, counts over a sliding window (velocity_window, ASSUMED).
/// </summary>
public sealed class Encoder
{
    private readonly Queue<long> _history = new();
    private readonly int _windowTicks;
    private readonly double _countsPerRad;
    private double _zeroAngle;

    public Encoder(double countsPerOutputRev, double windowSeconds, int tickRate)
    {
        _countsPerRad = countsPerOutputRev / (2 * Math.PI);
        _windowTicks = Math.Max(1, (int)Math.Round(windowSeconds * tickRate));
        TickRate = tickRate;
    }

    public int TickRate { get; }
    public long Counts { get; private set; }
    public double VelocityCountsPerS { get; private set; }

    /// <summary>Samples the shaft angle once per sim tick.</summary>
    public void Sample(double shaftAngleRad)
    {
        Counts = (long)Math.Floor((shaftAngleRad - _zeroAngle) * _countsPerRad);
        _history.Enqueue(Counts);
        if (_history.Count > _windowTicks + 1)
        {
            _history.Dequeue();
        }
        var span = _history.Count - 1;
        VelocityCountsPerS = span == 0 ? 0 : (Counts - _history.Peek()) * (double)TickRate / span;
    }

    /// <summary>STOP_AND_RESET_ENCODER: the current angle becomes count 0.</summary>
    public void Reset(double shaftAngleRad)
    {
        _zeroAngle = shaftAngleRad;
        _history.Clear();
        Counts = 0;
        VelocityCountsPerS = 0;
    }
}

/// <summary>
/// One motor port of a REV hub as the SDK sees it (spec 06 "Motors"; config/motors.yaml hub_motor_controller).
/// Inputs are the raw values the SDK sends after DcMotorImpl applied the direction; output is the PWM duty
/// (−1..1) and whether a zero duty brakes. Behaviour per SDK 12.0.0 LynxDcMotorController:
/// <list type="bullet">
/// <item>RUN_WITHOUT_ENCODER: duty = power.</item>
/// <item>RUN_USING_ENCODER: target velocity = power × achievable max ticks/s (or setVelocity), firmware velocity
///   PIDF: u = P·e + I·∫e dt/unit + D·de/dt + F·v_target, duty = u / 32767.</item>
/// <item>RUN_TO_POSITION: position P loop gives a velocity target limited to |power| × max, then the velocity
///   PIDF; isBusy while |target − position| &gt; tolerance.</item>
/// <item>STOP_AND_RESET_ENCODER: encoder reset, motor stopped (zero-power behaviour).</item>
/// </list>
/// </summary>
public sealed class HubMotorController
{
    private readonly HubControllerSpec _spec;
    private readonly double _maxTicksPerSecond;
    private double _integral;
    private double _lastError;
    private bool _haveLastError;
    private double _targetVelocity;
    private bool _velocityCommanded;

    public HubMotorController(HubControllerSpec spec, SdkMotorType sdkType, Encoder encoder)
    {
        _spec = spec;
        _maxTicksPerSecond = sdkType.AchievableMaxTicksPerSecond;
        Encoder = encoder;
        VelocityPidf = spec.DefaultVelocityPidf;
        PositionPidf = spec.DefaultPositionPidf;
        TargetPositionTolerance = spec.TargetPositionTolerance;
    }

    public Encoder Encoder { get; }
    public RunMode Mode { get; private set; } = RunMode.RunWithoutEncoder;
    public ZeroPowerBehavior ZeroPower { get; set; } = ZeroPowerBehavior.Float;
    public double Power { get; private set; }
    public int TargetPosition { get; set; }
    public int TargetPositionTolerance { get; set; }
    public Pidf VelocityPidf { get; set; }
    public Pidf PositionPidf { get; set; }
    public bool Enabled { get; set; } = true;

    /// <summary>Duty cycle computed by the last <see cref="Update"/>.</summary>
    public double Duty { get; private set; }

    /// <summary>DcMotor.isBusy(): RUN_TO_POSITION and not yet within the tolerance.</summary>
    public bool IsBusy => Mode == RunMode.RunToPosition && Math.Abs(TargetPosition - Encoder.Counts) > TargetPositionTolerance;

    /// <summary>Zero duty shorts the windings (BRAKE) rather than leaving them open.</summary>
    public bool BrakeWhenIdle => ZeroPower == ZeroPowerBehavior.Brake;

    public void SetMode(RunMode mode, double shaftAngleRad)
    {
        if (mode == RunMode.StopAndResetEncoder)
        {
            Encoder.Reset(shaftAngleRad);
            Power = 0;
        }
        Mode = mode;
        ResetLoop();
    }

    /// <summary>DcMotor.setPower (−1..1, clipped like the SDK's Range.clip).</summary>
    public void SetPower(double power)
    {
        Power = Math.Clamp(power, -1, 1);
        _velocityCommanded = false;
    }

    /// <summary>DcMotorEx.setVelocity(ticks/s): only meaningful in RUN_USING_ENCODER.</summary>
    public void SetVelocity(double ticksPerSecond)
    {
        _targetVelocity = ticksPerSecond;
        _velocityCommanded = true;
        Power = Math.Clamp(ticksPerSecond / _maxTicksPerSecond, -1, 1);
    }

    /// <summary>Computes the duty for this tick from the mode, commands and encoder (sampled already).</summary>
    public double Update(double dt)
    {
        if (!Enabled)
        {
            return Duty = 0;
        }
        Duty = Mode switch
        {
            RunMode.RunWithoutEncoder => Power,
            RunMode.RunUsingEncoder => VelocityLoop(_velocityCommanded ? _targetVelocity : Power * _maxTicksPerSecond, dt),
            RunMode.RunToPosition => PositionLoop(dt),
            _ => 0,
        };
        return Duty;
    }

    private double PositionLoop(double dt)
    {
        var limit = Math.Abs(Power) * _maxTicksPerSecond;
        var error = TargetPosition - Encoder.Counts;
        var velocity = Math.Clamp(PositionPidf.P * error, -limit, limit);
        return VelocityLoop(velocity, dt);
    }

    private double VelocityLoop(double target, double dt)
    {
        var error = target - Encoder.VelocityCountsPerS;
        var derivative = _haveLastError ? (error - _lastError) / dt : 0;
        _lastError = error;
        _haveLastError = true;
        var g = VelocityPidf;
        var unsaturated = g.P * error + g.I * (_integral + error * dt) / _spec.IntegralTimeUnitS + g.D * derivative + g.F * target;
        if (Math.Abs(unsaturated) < _spec.PowerFullScale)
        {
            _integral += error * dt;   // no integral wind-up while the output is saturated
        }
        var u = g.P * error + g.I * _integral / _spec.IntegralTimeUnitS + g.D * derivative + g.F * target;
        return Math.Clamp(u / _spec.PowerFullScale, -1, 1);
    }

    private void ResetLoop()
    {
        _integral = 0;
        _haveLastError = false;
        _lastError = 0;
    }
}
