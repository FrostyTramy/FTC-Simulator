using Sim.Core.Numerics;

namespace Sim.Robots;

/// <summary>A spinning roller or wheel of a mechanism, in world coordinates for one tick.</summary>
/// <param name="AxisPoint">A point on the axle (the middle of the roller).</param>
/// <param name="Axis">Unit axle direction; <paramref name="Omega"/> is the spin about it (right-handed).</param>
/// <param name="HalfWidth">Half the roller width along the axle.</param>
/// <param name="PointVelocity">Velocity of the axle point (the robot's motion).</param>
/// <param name="BodyAngularVelocity">Angular velocity of the robot the roller is mounted on.</param>
public readonly record struct Roller(
    Vec3 AxisPoint,
    Vec3 Axis,
    double Radius,
    double HalfWidth,
    double Omega,
    Vec3 PointVelocity,
    Vec3 BodyAngularVelocity);

/// <summary>A game element (sphere) in world coordinates.</summary>
public readonly record struct Ball(Vec3 Center, double Radius, Vec3 Velocity, Vec3 AngularVelocity);

/// <summary>Contact constants: element compliance (physics.yaml elements.*.contact_compliance_*), pair friction, slip speed.</summary>
public readonly record struct ContactParams(double Stiffness, double Damping, double Friction, double FullSlipSpeed);

/// <summary>Force on the ball at <paramref name="Point"/>; the robot gets −Force there; <paramref name="ShaftTorque"/> loads the roller.</summary>
public readonly record struct ContactForce(Vec3 Force, Vec3 Point, double NormalForce, double ShaftTorque);

/// <summary>
/// Intake rollers, sweeper wheels and flywheels touching game elements (spec 04 "Intakes": contact surfaces with
/// tangential velocity + compression; "Flywheel launchers": contact time, energy transfer, RPM drop per shot). The
/// element is squeezed by δ = R + r − d (d = distance from the axle segment): normal force k·δ + c·(closing speed),
/// tangential friction μ·N·sat(slip / s₀) towards the roller surface velocity. The shaft torque (about the axle, on the
/// roller) slows the roller, so a flywheel loses speed per shot and recovers through its motor.
/// </summary>
public static class CompliantContact
{
    public static ContactForce? Compute(in Roller roller, in Ball ball, in ContactParams p)
    {
        var a = roller.Axis;
        var along = Math.Clamp((ball.Center - roller.AxisPoint).Dot(a), -roller.HalfWidth, roller.HalfWidth);
        var q = roller.AxisPoint + a * along;
        var offset = ball.Center - q;
        var d = offset.Length;
        var squeeze = roller.Radius + ball.Radius - d;
        if (squeeze <= 0 || d < 1e-9)
        {
            return null;
        }
        var n = offset * (1 / d);                                // from the axle towards the ball centre
        var point = q + n * roller.Radius;                       // on the roller surface
        var rollerVelocity = roller.PointVelocity + Cross(roller.BodyAngularVelocity, point - roller.AxisPoint)
                             + Cross(a * roller.Omega, n * roller.Radius);
        var ballVelocity = ball.Velocity + Cross(ball.AngularVelocity, n * -ball.Radius);
        var relative = ballVelocity - rollerVelocity;
        var closing = -relative.Dot(n);
        var normal = Math.Max(0, p.Stiffness * squeeze + p.Damping * closing);
        if (normal == 0)
        {
            return null;
        }
        var slip = relative - n * relative.Dot(n);
        var slipSpeed = slip.Length;
        var friction = slipSpeed < 1e-12 ? Vec3.Zero : slip * (-p.Friction * normal * Math.Min(1, slipSpeed / p.FullSlipSpeed) / slipSpeed);
        var force = n * normal + friction;
        var shaftTorque = Cross(n * roller.Radius, force * -1).Dot(a);
        return new ContactForce(force, point, normal, shaftTorque);
    }

    public static Vec3 Cross(Vec3 u, Vec3 v) => new(u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);
}
