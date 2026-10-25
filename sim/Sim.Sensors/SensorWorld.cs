using System.Numerics;

namespace Sim.Sensors;

/// <summary>Nearest surface on a ray: distance (m), surface normal and diffuse colour (linear RGB 0..1), world frame.</summary>
public readonly record struct SurfaceHit(float Distance, Vector3 Normal, Vector3 Color);

/// <summary>
/// Ground truth the sensor models sample once per physics step (spec 09 "Ground truth"; never visible to robot code).
/// World = simulation frame: +Y up, +X towards the blue side (right as seen from the audience), +Z towards the audience,
/// metres. The robot frame is the robot YAML's: +X right, +Y up, −Z forward, origin on the floor.
/// </summary>
public interface ISensorWorld
{
    /// <summary>Robot origin in the world.</summary>
    Vector3 RobotPosition { get; }

    /// <summary>Rotation from the robot frame to the world.</summary>
    Quaternion RobotRotation { get; }

    /// <summary>Nearest surface along a world-frame ray within <paramref name="maxDistance"/>, ignoring the robot itself.</summary>
    SurfaceHit? Raycast(Vector3 origin, Vector3 direction, float maxDistance);
}

/// <summary>Helpers on robot poses shared by the sensor models.</summary>
public static class Poses
{
    /// <summary>A robot-frame point in the world.</summary>
    public static Vector3 ToWorld(this ISensorWorld w, Vector3 robotPoint) => w.RobotPosition + Vector3.Transform(robotPoint, w.RobotRotation);

    /// <summary>A robot-frame direction in the world.</summary>
    public static Vector3 DirectionToWorld(this ISensorWorld w, Vector3 robotDirection) => Vector3.Transform(robotDirection, w.RobotRotation);

    /// <summary>
    /// Heading of a rotation about the world +Y axis, counter-clockwise seen from above (the FTC/Pinpoint sense): the
    /// angle of the rotated −Z (forward) direction, 0 when it points along −Z.
    /// </summary>
    public static double Heading(Quaternion rotation)
    {
        var f = Vector3.Transform(-Vector3.UnitZ, rotation);
        return Math.Atan2(-f.X, -f.Z);
    }

    /// <summary>Angle between the rotated +Y axis and the world +Y axis (radians).</summary>
    public static double Tilt(Quaternion rotation)
    {
        var up = Vector3.Transform(Vector3.UnitY, rotation);
        return Math.Acos(Math.Clamp(up.Y / up.Length(), -1f, 1f));
    }

    /// <summary>Angle wrapped to (−π, π].</summary>
    public static double Wrap(double angle)
    {
        angle = Math.IEEERemainder(angle, 2 * Math.PI);
        return angle <= -Math.PI ? angle + 2 * Math.PI : angle;
    }
}
