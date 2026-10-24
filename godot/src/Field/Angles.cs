using Godot;

namespace Sim.Field;

public static class Angles
{
    /// <summary>Signed rotation (radians, right-handed about <paramref name="axis"/>) that takes
    /// orientation <paramref name="from"/> to <paramref name="to"/>, measured on a vector
    /// perpendicular to the axis. Range (-π, π].</summary>
    public static float SignedAngle(Basis from, Basis to, Vector3 axis)
    {
        var a = axis.Normalized();
        var helper = Mathf.Abs(a.Y) < 0.9f ? Vector3.Up : Vector3.Right;
        var perp = helper.Cross(a).Normalized();
        var local = from.Inverse() * perp;
        var v0 = perp;
        var v1 = to * local;
        return Mathf.Atan2(a.Dot(v0.Cross(v1)), v0.Dot(v1));
    }
}
