using System.Numerics;

namespace Sim.Robots;

/// <summary>
/// A servo arm that swings down to the floor and rests on it (robot YAML mechanism <c>floor_stop</c>): the angle about
/// the arm's axis, from its CAD pose, at which its lowest point has pressed <c>penetration</c> into the floor (y = 0 of
/// the robot frame). Commanded further, the arm stays there, as a real arm stalls against the tiles. The arm keeps
/// pushing elements kinematically and never lifts the robot (it does not collide with the floor).
/// </summary>
public static class FloorStop
{
    private const double SearchStepRad = Math.PI / 720;   // 0.25°

    /// <summary>
    /// Stop angles (radians, negative and positive direction from the CAD pose) for an arm whose geometry is
    /// <paramref name="points"/> at the CAD pose, robot frame; null in a direction where the arm never reaches the
    /// floor within a full turn. Throws if the CAD pose itself is already deeper than the penetration.
    /// </summary>
    public static (double? Min, double? Max) Limits(IReadOnlyList<Vector3> points, Vector3 pivot, Vector3 axis, double penetration)
    {
        if (points.Count == 0)
        {
            return (null, null);
        }
        var floor = -penetration;
        if (LowestY(points, pivot, axis, 0) < floor)
        {
            throw new RobotConfigException($"the arm's CAD pose already reaches {LowestY(points, pivot, axis, 0):F4} m, below the floor stop at {floor} m");
        }
        return (Search(points, pivot, axis, floor, -1), Search(points, pivot, axis, floor, +1));
    }

    /// <summary>Lowest y of the arm's points turned by <paramref name="angle"/> about the axis through the pivot.</summary>
    public static double LowestY(IReadOnlyList<Vector3> points, Vector3 pivot, Vector3 axis, double angle)
    {
        var q = Quaternion.CreateFromAxisAngle(Vector3.Normalize(axis), (float)angle);
        var lowest = double.PositiveInfinity;
        foreach (var p in points)
        {
            lowest = Math.Min(lowest, (pivot + Vector3.Transform(p - pivot, q)).Y);
        }
        return lowest;
    }

    /// <summary>Clamps a commanded arm angle to the stops (either may be null).</summary>
    public static double Clamp(double angle, (double? Min, double? Max) limits) =>
        Math.Min(limits.Max ?? double.PositiveInfinity, Math.Max(limits.Min ?? double.NegativeInfinity, angle));

    private static double? Search(IReadOnlyList<Vector3> points, Vector3 pivot, Vector3 axis, double floor, int direction)
    {
        var steps = (int)Math.Ceiling(2 * Math.PI / SearchStepRad);
        var previous = 0.0;
        for (var i = 1; i <= steps; i++)
        {
            var angle = direction * i * SearchStepRad;
            if (LowestY(points, pivot, axis, angle) <= floor)
            {
                // Bisect between the last angle above the stop and this one (to ~1e-6 rad).
                var (above, below) = (previous, angle);
                for (var k = 0; k < 30; k++)
                {
                    var mid = (above + below) / 2;
                    if (LowestY(points, pivot, axis, mid) <= floor)
                    {
                        below = mid;
                    }
                    else
                    {
                        above = mid;
                    }
                }
                return above;
            }
            previous = angle;
        }
        return null;
    }
}
