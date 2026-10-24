using Sim.Game.Field;

namespace Sim.Game.Scoring;

/// <summary>
/// HIVE TIP detection (§10.5.1 p87): the HIVE moves from one stable state to the other AND the damper
/// that was not touching the frame starts touching it. With the hinge angle from physics: the angle
/// comes within <see cref="FieldGeometry.DamperEngageDeg"/> of the opposite stable state
/// (config/field_mechanisms.yaml tip_detection; the damper contact itself is ASSUMED at that angle).
/// </summary>
public sealed class HiveTracker
{
    private readonly HingeGeometry _hinge;
    private readonly double _engageDeg;

    public HiveTracker(HingeGeometry hinge, double engageDeg, CellSide initialUp)
    {
        _hinge = hinge;
        _engageDeg = engageDeg;
        StableUp = initialUp;
    }

    public Alliance Alliance => _hinge.Alliance;

    /// <summary>CELL facing up in the last stable state reached.</summary>
    public CellSide StableUp { get; private set; }

    public int Tips { get; private set; }

    /// <summary>Feeds one hinge angle; returns true on the tick a TIP completes.</summary>
    public bool Observe(double angleDeg)
    {
        var target = StableUp.Other();
        if (Math.Abs(angleDeg - _hinge.StableAngle(target)) > _engageDeg)
        {
            return false;
        }
        StableUp = target;
        Tips++;
        return true;
    }
}
