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

/// <summary>
/// Contact constants: element compliance (physics.yaml elements.*.contact_compliance_*), pair friction, slip speed and
/// the spring preload of a roller on a sprung arm (mechanism contact_preload, N; it builds up over the first
/// <see cref="CompliantContact.PreloadRamp"/> of squeeze so the force stays continuous).
/// </summary>
public readonly record struct ContactParams(double Stiffness, double Damping, double Friction, double FullSlipSpeed, double Preload = 0);

/// <summary>Force on the ball at <paramref name="Point"/>; the robot gets −Force there; <paramref name="ShaftTorque"/> loads the roller.</summary>
public readonly record struct ContactForce(Vec3 Force, Vec3 Point, double NormalForce, double ShaftTorque);

/// <summary>
/// Intake rollers, sweeper wheels, flywheels and drive wheels touching game elements (spec 04 "Intakes": contact
/// surfaces with tangential velocity + compression; "Flywheel launchers": contact time, energy transfer, RPM drop per
/// shot). The roller is a solid cylinder (radius R, flat ends): the element is squeezed by δ = r − (distance from the
/// nearest cylinder point), i.e. δ = R + r − d on the rim (d = distance from the axle); normal force k·δ + c·(closing
/// speed) (+ a sprung arm's preload), tangential friction μ·N·sat(slip / s₀) towards the surface velocity. The shaft
/// torque (about the axle, on the roller) slows the roller, so a flywheel loses speed per shot and recovers through
/// its motor.
/// </summary>
public static class CompliantContact
{
    /// <summary>Squeeze over which a sprung roller's preload builds up, m.</summary>
    public const double PreloadRamp = 0.001;

    /// <summary>How far the element overlaps the roller, m (negative: the gap between them).</summary>
    public static double Squeeze(in Roller roller, in Ball ball) => Nearest(roller, ball)?.Squeeze ?? double.NegativeInfinity;

    /// <summary>
    /// Nearest point of the solid cylinder to the ball centre: the point, the unit normal towards the ball, the squeeze
    /// and the point on the axle it turns about. Beside the rim the normal is radial; beyond a flat end it points from
    /// the nearest end-face point. Null when the ball centre is on the axle (no direction).
    /// </summary>
    private static (Vec3 Point, Vec3 Normal, double Squeeze, Vec3 Hub)? Nearest(in Roller roller, in Ball ball)
    {
        var a = roller.Axis;
        var rel = ball.Center - roller.AxisPoint;
        var along = rel.Dot(a);
        var radial = rel - a * along;
        var rho = radial.Length;
        if (Math.Abs(along) <= roller.HalfWidth)
        {
            if (rho < 1e-9)
            {
                return null;
            }
            var n = radial * (1 / rho);
            var hub = roller.AxisPoint + a * along;
            return (hub + n * roller.Radius, n, roller.Radius + ball.Radius - rho, hub);
        }
        var end = roller.AxisPoint + a * (Math.Sign(along) * roller.HalfWidth);
        var u = rho > 1e-9 ? radial * (1 / rho) : Vec3.Zero;
        var point = end + u * Math.Min(rho, roller.Radius);
        var offset = ball.Center - point;
        var d = offset.Length;
        return d < 1e-9 ? null : (point, offset * (1 / d), ball.Radius - d, end);
    }

    public static ContactForce? Compute(in Roller roller, in Ball ball, in ContactParams p)
    {
        if (Nearest(roller, ball) is not { Squeeze: > 0 } nearest)
        {
            return null;
        }
        var a = roller.Axis;
        var (point, n, squeeze, hub) = nearest;                  // n: from the roller towards the ball centre
        var rollerVelocity = roller.PointVelocity + Cross(roller.BodyAngularVelocity, point - roller.AxisPoint)
                             + Cross(a * roller.Omega, point - hub);
        var ballVelocity = ball.Velocity + Cross(ball.AngularVelocity, n * -ball.Radius);
        var relative = ballVelocity - rollerVelocity;
        var closing = -relative.Dot(n);
        var normal = Math.Max(0, p.Stiffness * squeeze + p.Damping * closing + p.Preload * Math.Min(1, squeeze / PreloadRamp));
        if (normal == 0)
        {
            return null;
        }
        var slip = relative - n * relative.Dot(n);
        var slipSpeed = slip.Length;
        var friction = slipSpeed < 1e-12 ? Vec3.Zero : slip * (-p.Friction * normal * Math.Min(1, slipSpeed / p.FullSlipSpeed) / slipSpeed);
        var force = n * normal + friction;
        var shaftTorque = Cross(point - hub, force * -1).Dot(a);
        return new ContactForce(force, point, normal, shaftTorque);
    }

    public static Vec3 Cross(Vec3 u, Vec3 v) => new(u.Y * v.Z - u.Z * v.Y, u.Z * v.X - u.X * v.Z, u.X * v.Y - u.Y * v.X);
}
