using Sim.Core.Numerics;

namespace Sim.Game.Geometry;

/// <summary>
/// Convex polygon on the floor plane (world X/Z), vertices stored counter-clockwise in (X, Z)
/// coordinates. Used for zones (infinitely tall prisms, §9.3) and robot footprints.
/// </summary>
public sealed class Polygon2
{
    private const double Eps = 1e-12;

    public Polygon2(IEnumerable<Vec2> vertices)
    {
        var v = vertices.ToList();
        if (v.Count < 3)
        {
            throw new ArgumentException("a polygon needs at least 3 vertices");
        }
        if (SignedArea(v) < 0)
        {
            v.Reverse();
        }
        for (var i = 0; i < v.Count; i++)
        {
            var a = v[i];
            var b = v[(i + 1) % v.Count];
            var c = v[(i + 2) % v.Count];
            if ((b - a).Cross(c - b) < -1e-12)
            {
                throw new ArgumentException("polygon must be convex");
            }
        }
        Vertices = v;
        Area = SignedArea(v);
        Centroid = ComputeCentroid(v, Area);
    }

    public IReadOnlyList<Vec2> Vertices { get; }
    public double Area { get; }
    public Vec2 Centroid { get; }

    public static Polygon2 Rect(double xMin, double xMax, double zMin, double zMax) =>
        new([new(xMin, zMin), new(xMax, zMin), new(xMax, zMax), new(xMin, zMax)]);

    /// <summary>Rectangle centred at <paramref name="centre"/> with half sizes along its local X and Z, rotated by yaw (rad).</summary>
    public static Polygon2 OrientedRect(Vec2 centre, double halfX, double halfZ, double yawRad) =>
        new(new[] { new Vec2(-halfX, -halfZ), new Vec2(halfX, -halfZ), new Vec2(halfX, halfZ), new Vec2(-halfX, halfZ) }
            .Select(p => centre + p.Rotated(yawRad)));

    public Polygon2 Translated(Vec2 d) => new(Vertices.Select(p => p + d));

    public bool Contains(Vec2 p)
    {
        for (var i = 0; i < Vertices.Count; i++)
        {
            var a = Vertices[i];
            var b = Vertices[(i + 1) % Vertices.Count];
            if ((b - a).Cross(p - a) < -Eps)
            {
                return false;
            }
        }
        return true;
    }

    /// <summary>Distance from a point to the polygon boundary (also for points inside).</summary>
    public double DistanceToBoundary(Vec2 p)
    {
        var best = double.MaxValue;
        for (var i = 0; i < Vertices.Count; i++)
        {
            best = Math.Min(best, SegmentDistance(p, Vertices[i], Vertices[(i + 1) % Vertices.Count]));
        }
        return best;
    }

    /// <summary>Distance from a point to the polygon (0 inside).</summary>
    public double DistanceTo(Vec2 p) => Contains(p) ? 0 : DistanceToBoundary(p);

    /// <summary>A circle (sphere seen from above) overlaps the polygon: "at least partially in" an infinitely tall zone.</summary>
    public bool IntersectsCircle(Vec2 centre, double radius) => DistanceTo(centre) <= radius;

    /// <summary>A circle lies completely inside the polygon.</summary>
    public bool ContainsCircle(Vec2 centre, double radius) => Contains(centre) && DistanceToBoundary(centre) >= radius;

    /// <summary>Separating-axis test; touching counts as intersecting.</summary>
    public bool Intersects(Polygon2 other) => !HasSeparatingAxis(this, other) && !HasSeparatingAxis(other, this);

    /// <summary>Minimum distance between two polygons (0 when they overlap).</summary>
    public double DistanceTo(Polygon2 other)
    {
        if (Intersects(other))
        {
            return 0;
        }
        var best = double.MaxValue;
        foreach (var p in Vertices)
        {
            best = Math.Min(best, other.DistanceToBoundary(p));
        }
        foreach (var p in other.Vertices)
        {
            best = Math.Min(best, DistanceToBoundary(p));
        }
        return best;
    }

    /// <summary>Width of the polygon measured along a unit direction.</summary>
    public double ExtentAlong(Vec2 unitDir)
    {
        double lo = double.MaxValue, hi = double.MinValue;
        foreach (var p in Vertices)
        {
            var d = p.Dot(unitDir);
            lo = Math.Min(lo, d);
            hi = Math.Max(hi, d);
        }
        return hi - lo;
    }

    /// <summary>
    /// True if the polygon fits in an a × b rectangle in some orientation (R102/R105 sizing, vertical axis fixed).
    /// Tests every edge direction plus a 0.25° sweep; deterministic.
    /// </summary>
    public bool FitsInRectangle(double a, double b, double tolerance = 1e-9)
    {
        var (small, large) = a <= b ? (a, b) : (b, a);
        var angles = new List<double>();
        for (var i = 0; i < Vertices.Count; i++)
        {
            var e = Vertices[(i + 1) % Vertices.Count] - Vertices[i];
            angles.Add(Math.Atan2(e.Z, e.X));
        }
        for (var k = 0; k < 720; k++)
        {
            angles.Add(k * Math.PI / 720);
        }
        foreach (var angle in angles)
        {
            var u = new Vec2(Math.Cos(angle), Math.Sin(angle));
            var v = new Vec2(-u.Z, u.X);
            var w1 = ExtentAlong(u);
            var w2 = ExtentAlong(v);
            var (lo, hi) = w1 <= w2 ? (w1, w2) : (w2, w1);
            if (lo <= small + tolerance && hi <= large + tolerance)
            {
                return true;
            }
        }
        return false;
    }

    private static bool HasSeparatingAxis(Polygon2 a, Polygon2 b)
    {
        for (var i = 0; i < a.Vertices.Count; i++)
        {
            var p = a.Vertices[i];
            var edge = a.Vertices[(i + 1) % a.Vertices.Count] - p;
            var normal = new Vec2(edge.Z, -edge.X); // outward for counter-clockwise polygons
            var allOutside = true;
            foreach (var q in b.Vertices)
            {
                if ((q - p).Dot(normal) <= Eps)
                {
                    allOutside = false;
                    break;
                }
            }
            if (allOutside)
            {
                return true;
            }
        }
        return false;
    }

    private static double SegmentDistance(Vec2 p, Vec2 a, Vec2 b)
    {
        var ab = b - a;
        var len2 = ab.Dot(ab);
        var t = len2 <= 0 ? 0 : Math.Clamp((p - a).Dot(ab) / len2, 0, 1);
        return (a + ab * t).DistanceTo(p);
    }

    private static double SignedArea(IReadOnlyList<Vec2> v)
    {
        var s = 0.0;
        for (var i = 0; i < v.Count; i++)
        {
            s += v[i].Cross(v[(i + 1) % v.Count]);
        }
        return s / 2;
    }

    private static Vec2 ComputeCentroid(IReadOnlyList<Vec2> v, double area)
    {
        double cx = 0, cz = 0;
        for (var i = 0; i < v.Count; i++)
        {
            var c = v[i].Cross(v[(i + 1) % v.Count]);
            cx += (v[i].X + v[(i + 1) % v.Count].X) * c;
            cz += (v[i].Z + v[(i + 1) % v.Count].Z) * c;
        }
        return new Vec2(cx / (6 * area), cz / (6 * area));
    }
}
