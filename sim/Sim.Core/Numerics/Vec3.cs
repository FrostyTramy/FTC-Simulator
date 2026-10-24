namespace Sim.Core.Numerics;

/// <summary>Double-precision 3D vector in the sim/CAD/Godot frame: X towards blue, Y up, Z towards the audience.</summary>
public readonly record struct Vec3(double X, double Y, double Z)
{
    public static readonly Vec3 Zero = new(0, 0, 0);

    public static Vec3 operator +(Vec3 a, Vec3 b) => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);
    public static Vec3 operator -(Vec3 a, Vec3 b) => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);
    public static Vec3 operator *(Vec3 a, double s) => new(a.X * s, a.Y * s, a.Z * s);
    public static Vec3 operator *(double s, Vec3 a) => a * s;

    public double Dot(Vec3 o) => X * o.X + Y * o.Y + Z * o.Z;
    public double Length => System.Math.Sqrt(Dot(this));

    /// <summary>Projection onto the floor plane (X, Z).</summary>
    public Vec2 Floor => new(X, Z);
}
