using System.Numerics;

namespace Sim.Robots;

/// <summary>Guided element path of a robot (robot YAML ball_path, D-027). Positions are in the robot frame.</summary>
public sealed record BallPathDef(
    Vector3 CaptureMin,
    Vector3 CaptureMax,
    double CaptureMinSurfaceSpeed,
    IReadOnlyList<Vector3> StorageSlots,
    int Capacity,
    double FeedAngleDeg,
    int FeedPowerSign,
    Vector3 LaunchPoint,
    Vector3 LaunchDirection,
    double ExitSpeedRatio,
    double MinLaunchSurfaceSpeed)
{
    /// <summary>Mechanism whose rotation in the feeding direction moves elements into the launcher (windmill paddles,
    /// a transfer wheel).</summary>
    public string Feeder { get; init; } = "windmill";

    /// <summary>Launcher mechanism (a flywheel); null = the robot's only flywheel.</summary>
    public string? Launcher { get; init; }

    /// <summary>Turret carrying the launcher: <see cref="LaunchPoint"/> and the launch direction turn with it.</summary>
    public string? Turret { get; init; }

    /// <summary>Servo gate in front of the launcher: feeding waits while it is closed.</summary>
    public GateDef? Gate { get; init; }

    /// <summary>Adjustable hood: launch elevation from the hood servo angle.</summary>
    public HoodDef? Hood { get; init; }

    /// <summary>Spring-loaded nip between the launcher wheels and the hood: grip (and so exit speed) per element size.</summary>
    public SpringNipDef? SpringNip { get; init; }
}

/// <summary>Gate arm driven by a servo; open while the servo angle (degrees from centre) is inside OpenServoAnglesDeg.</summary>
public sealed record GateDef(string Mechanism, (double Min, double Max) OpenServoAnglesDeg)
{
    public bool IsOpen(double servoAngleDeg) =>
        servoAngleDeg >= Math.Min(OpenServoAnglesDeg.Min, OpenServoAnglesDeg.Max)
        && servoAngleDeg <= Math.Max(OpenServoAnglesDeg.Min, OpenServoAnglesDeg.Max);
}

/// <summary>
/// Hood: launch elevation (degrees above horizontal, towards the launch heading) varies linearly with the hood servo
/// angle between two calibration points (clamped outside them).
/// </summary>
public sealed record HoodDef(string Mechanism, (double A, double B) ServoAnglesDeg, (double A, double B) ElevationDeg)
{
    public double Elevation(double servoAngleDeg)
    {
        var span = ServoAnglesDeg.B - ServoAnglesDeg.A;
        var t = span == 0 ? 0 : Math.Clamp((servoAngleDeg - ServoAnglesDeg.A) / span, 0, 1);
        return ElevationDeg.A + t * (ElevationDeg.B - ElevationDeg.A);
    }
}

/// <summary>
/// Spring-loaded nip: the launcher wheel rim and a sprung back plate are <see cref="Gap"/> apart. An element wider than
/// the gap pushes the plate back (spring: <see cref="Preload"/> + <see cref="Rate"/>·push, up to <see cref="Travel"/>);
/// beyond the travel the element itself is squeezed (its own contact stiffness). That normal force N lets the wheel
/// give at most μ·N·t of impulse while the element rolls along <see cref="ContactLength"/> (t = ContactLength / v),
/// so a lightly pressed element slips and leaves slower than rolling (half the rim speed) would give.
/// </summary>
public sealed record SpringNipDef(double Gap, double Travel, double Rate, double Preload, double ContactLength)
{
    /// <summary>Normal force on an element of <paramref name="diameter"/> with contact stiffness <paramref name="elementStiffness"/>, N.</summary>
    public double NormalForce(double diameter, double elementStiffness)
    {
        var over = diameter - Gap;
        if (over <= 0)
        {
            return 0;
        }
        var push = Math.Min(over, Travel);
        return Preload + Rate * push + elementStiffness * Math.Max(0, over - Travel);
    }

    /// <summary>Exit speed when rolling without slip would give <paramref name="idealSpeed"/>; 0 if the element is not gripped.</summary>
    public double ExitSpeed(double diameter, double elementStiffness, double massKg, double idealSpeed, double friction)
    {
        var n = NormalForce(diameter, elementStiffness);
        if (idealSpeed <= 0 || n <= 0)
        {
            return 0;
        }
        // Impulse needed m·v against the impulse available μ·N·L/v: v² ≤ μ·N·L/m.
        return Math.Min(idealSpeed, Math.Sqrt(friction * n * ContactLength / massKg));
    }
}


/// <summary>
/// The StarterBot's intake → hopper → feeder → flywheel path as rules instead of contacts (D-027): an element inside the
/// capture zone while the intake roller turns inwards fast enough is stored (up to the hopper capacity; a full hopper
/// takes nothing, the element stays outside); every <see cref="BallPathDef.FeedAngleDeg"/> of feeder rotation in the
/// feeding direction moves the oldest stored element into the flywheel, which launches it if its rim is fast enough.
/// Deterministic; ids are the caller's element ids.
/// </summary>
public sealed class GuidedBallPath
{
    private readonly List<int> _stored = new();
    private double _feedAccumulatedDeg;

    public GuidedBallPath(BallPathDef def)
    {
        Def = def;
    }

    public BallPathDef Def { get; }

    /// <summary>Stored element ids, oldest first (slot i holds element i).</summary>
    public IReadOnlyList<int> Stored => _stored;

    public bool Full => _stored.Count >= Def.Capacity;

    public bool Contains(int id) => _stored.Contains(id);

    /// <summary>Takes the element if it is in the capture zone and the roller pulls in fast enough.</summary>
    /// <param name="inwardSurfaceSpeed">Intake rim speed towards the inside of the robot, m/s (negative = spitting out).</param>
    public bool TryCapture(int id, Vector3 local, double inwardSurfaceSpeed)
    {
        if (Full || _stored.Contains(id) || inwardSurfaceSpeed < Def.CaptureMinSurfaceSpeed)
        {
            return false;
        }
        var inside = local.X >= Def.CaptureMin.X && local.X <= Def.CaptureMax.X
                     && local.Y >= Def.CaptureMin.Y && local.Y <= Def.CaptureMax.Y
                     && local.Z >= Def.CaptureMin.Z && local.Z <= Def.CaptureMax.Z;
        if (inside)
        {
            _stored.Add(id);
        }
        return inside;
    }

    /// <summary>Puts an element straight into the hopper (pre-loads at setup).</summary>
    public bool Preload(int id)
    {
        if (Full || _stored.Contains(id))
        {
            return false;
        }
        _stored.Add(id);
        return true;
    }

    public Vector3 SlotOf(int id) => Def.StorageSlots[Math.Min(_stored.IndexOf(id), Def.StorageSlots.Count - 1)];

    /// <summary>
    /// Feeder rotation for one tick. Returns the element launched now (null if none): the oldest stored one, once a full
    /// feed angle has turned in the feeding direction and the flywheel rim is fast enough. Turning backwards unwinds.
    /// </summary>
    /// <param name="feedRotationDeg">Feeder rotation this tick, positive in the feeding direction.</param>
    /// <param name="gateOpen">False while a closed gate holds the next element back.</param>
    public int? Feed(double feedRotationDeg, double flywheelSurfaceSpeed, bool gateOpen = true)
    {
        _feedAccumulatedDeg = Math.Max(0, _feedAccumulatedDeg + feedRotationDeg);
        if (_stored.Count == 0 || _feedAccumulatedDeg < Def.FeedAngleDeg || flywheelSurfaceSpeed < Def.MinLaunchSurfaceSpeed
            || !gateOpen)
        {
            _feedAccumulatedDeg = Math.Min(_feedAccumulatedDeg, Def.FeedAngleDeg);   // a waiting element blocks the feeder
            return null;
        }
        _feedAccumulatedDeg -= Def.FeedAngleDeg;
        var id = _stored[0];
        _stored.RemoveAt(0);
        return id;
    }

    /// <summary>Exit speed of a launched element: the ratio of the rim speed (rolling between rim and hood).</summary>
    public double ExitSpeed(double flywheelSurfaceSpeed) => Def.ExitSpeedRatio * flywheelSurfaceSpeed;

    /// <summary>
    /// Angular impulse the flywheel loses for one shot, N·m·s: the rim pushes the element up to <paramref name="exitSpeed"/>,
    /// so the flywheel gives up m·v·R of angular momentum (RPM drop per shot, spec 04 Flywheel launchers).
    /// </summary>
    public static double FlywheelImpulse(double elementMassKg, double exitSpeed, double rimRadius) => elementMassKg * exitSpeed * rimRadius;

    public void Clear()
    {
        _stored.Clear();
        _feedAccumulatedDeg = 0;
    }
}
