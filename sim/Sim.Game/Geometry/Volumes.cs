using Sim.Core.Numerics;

namespace Sim.Game.Geometry;

/// <summary>Rotation by an angle about a unit axis (Rodrigues' formula), as a 3×3 matrix.</summary>
public readonly struct Rotation3
{
    private readonly double _m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22;

    private Rotation3(double m00, double m01, double m02, double m10, double m11, double m12, double m20, double m21, double m22)
    {
        (_m00, _m01, _m02, _m10, _m11, _m12, _m20, _m21, _m22) = (m00, m01, m02, m10, m11, m12, m20, m21, m22);
    }

    public static Rotation3 Identity => new(1, 0, 0, 0, 1, 0, 0, 0, 1);

    /// <summary>R = I + sin(θ) K + (1 − cos(θ)) K², K = cross-product matrix of the unit axis (right-hand rule).</summary>
    public static Rotation3 AxisAngle(Vec3 axis, double angleRad)
    {
        var n = axis * (1.0 / axis.Length);
        var (x, y, z) = (n.X, n.Y, n.Z);
        var c = Math.Cos(angleRad);
        var s = Math.Sin(angleRad);
        var t = 1 - c;
        return new Rotation3(
            t * x * x + c, t * x * y - s * z, t * x * z + s * y,
            t * x * y + s * z, t * y * y + c, t * y * z - s * x,
            t * x * z - s * y, t * y * z + s * x, t * z * z + c);
    }

    public Vec3 Apply(Vec3 v) => new(
        _m00 * v.X + _m01 * v.Y + _m02 * v.Z,
        _m10 * v.X + _m11 * v.Y + _m12 * v.Z,
        _m20 * v.X + _m21 * v.Y + _m22 * v.Z);
}

/// <summary>Oriented box: centre, three orthonormal axes and half sizes along them.</summary>
public sealed record OrientedBox(Vec3 Centre, Vec3 AxisX, Vec3 AxisY, Vec3 AxisZ, Vec3 HalfSize)
{
    /// <summary>Box from a row-major 4×4 transform (box axes = rotation columns, centre = translation) and full sizes.</summary>
    public static OrientedBox FromRowMajor(IReadOnlyList<double> m, IReadOnlyList<double> size)
    {
        if (m.Count != 16 || size.Count != 3)
        {
            throw new ArgumentException("expected a 4x4 row-major matrix and 3 sizes");
        }
        return new OrientedBox(
            new Vec3(m[3], m[7], m[11]),
            new Vec3(m[0], m[4], m[8]),
            new Vec3(m[1], m[5], m[9]),
            new Vec3(m[2], m[6], m[10]),
            new Vec3(size[0] / 2, size[1] / 2, size[2] / 2));
    }

    public bool Contains(Vec3 p)
    {
        var d = p - Centre;
        return Math.Abs(d.Dot(AxisX)) <= HalfSize.X
            && Math.Abs(d.Dot(AxisY)) <= HalfSize.Y
            && Math.Abs(d.Dot(AxisZ)) <= HalfSize.Z;
    }

    /// <summary>The box rotated by <paramref name="rotation"/> about <paramref name="pivot"/> (a hinge moving its body).</summary>
    public OrientedBox RotatedAbout(Vec3 pivot, Rotation3 rotation) => new(
        pivot + rotation.Apply(Centre - pivot),
        rotation.Apply(AxisX),
        rotation.Apply(AxisY),
        rotation.Apply(AxisZ),
        HalfSize);
}

/// <summary>Vertical solid cylinder between two heights (FLOWER scoring volume, §10.5.2).</summary>
public sealed record CylinderVolume(Vec2 AxisXZ, double Radius, double YMin, double YMax)
{
    /// <summary>
    /// Sphere overlaps the cylinder ("at least partially within"): distance from the sphere centre to
    /// the solid cylinder ≤ radius. Radial and vertical excess combine as the distance to the rim.
    /// </summary>
    public bool IntersectsSphere(Vec3 centre, double radius)
    {
        var radial = Math.Max(0, (centre.Floor - AxisXZ).Length - Radius);
        var vertical = centre.Y < YMin ? YMin - centre.Y : centre.Y > YMax ? centre.Y - YMax : 0;
        return radial * radial + vertical * vertical <= radius * radius;
    }

    public bool ContainsPoint(Vec3 p) => (p.Floor - AxisXZ).Length <= Radius && p.Y >= YMin && p.Y <= YMax;
}

/// <summary>
/// Zone predicates of spec 03 ("Trigger zones and counting"): one implementation per definition so
/// every rule uses the same, tested geometry.
/// </summary>
public static class ZonePredicates
{
    /// <summary>Element sphere fully inside an infinitely tall zone.</summary>
    public static bool FullyInside(Polygon2 zone, ElementState e) => zone.ContainsCircle(e.Position.Floor, e.Radius);

    /// <summary>Element centre inside an infinitely tall zone.</summary>
    public static bool CentroidInside(Polygon2 zone, ElementState e) => zone.Contains(e.Position.Floor);

    /// <summary>Element at least partially inside an infinitely tall zone (§10.5.3 GARDEN).</summary>
    public static bool Intersects(Polygon2 zone, ElementState e) => zone.IntersectsCircle(e.Position.Floor, e.Radius);

    /// <summary>Robot at least partially inside an infinitely tall zone (§10.5.4 PARK).</summary>
    public static bool Intersects(Polygon2 zone, RobotState r) => zone.Intersects(r.Footprint);

    /// <summary>Element at least partially within a vertical cylinder (§10.5.2 FLOWER).</summary>
    public static bool Intersects(CylinderVolume volume, ElementState e) => volume.IntersectsSphere(e.Position, e.Radius);

    /// <summary>Element centre inside an oriented box (CELL volume; ASSUMED reading of "left in a CELL").</summary>
    public static bool CentroidInside(OrientedBox box, ElementState e) => box.Contains(e.Position);

    /// <summary>Element sphere crosses a plane (given by a point and a unit normal).</summary>
    public static bool BreaksPlane(Vec3 point, Vec3 unitNormal, ElementState e) =>
        Math.Abs((e.Position - point).Dot(unitNormal)) <= e.Radius;
}
