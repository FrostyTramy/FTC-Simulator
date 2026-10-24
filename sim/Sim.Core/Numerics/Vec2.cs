namespace Sim.Core.Numerics;

/// <summary>Double-precision point/vector on the floor plane. Components are the world X and Z axes.</summary>
public readonly record struct Vec2(double X, double Z)
{
    public static Vec2 operator +(Vec2 a, Vec2 b) => new(a.X + b.X, a.Z + b.Z);
    public static Vec2 operator -(Vec2 a, Vec2 b) => new(a.X - b.X, a.Z - b.Z);
    public static Vec2 operator *(Vec2 a, double s) => new(a.X * s, a.Z * s);

    public double Dot(Vec2 o) => X * o.X + Z * o.Z;

    /// <summary>2D cross product (z component of the 3D cross product of (X, Z) vectors).</summary>
    public double Cross(Vec2 o) => X * o.Z - Z * o.X;

    public double Length => System.Math.Sqrt(Dot(this));
    public double DistanceTo(Vec2 o) => (this - o).Length;

    public Vec2 Rotated(double angleRad)
    {
        var c = System.Math.Cos(angleRad);
        var s = System.Math.Sin(angleRad);
        return new Vec2(X * c - Z * s, X * s + Z * c);
    }
}
