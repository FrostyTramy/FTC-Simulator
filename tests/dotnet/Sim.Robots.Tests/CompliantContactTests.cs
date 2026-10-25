using Sim.Core.Numerics;

namespace RobotTests;

/// <summary>Roller/flywheel contact with game elements (spec 04 Intakes, Flywheel launchers).</summary>
public class CompliantContactTests
{
    private static readonly ContactParams Pollen = new(Stiffness: 2000, Damping: 5, Friction: 1.0, FullSlipSpeed: 0.05);
    private const double R = 0.024;      // Gecko 48 mm
    private const double Ball = 0.03556; // POLLEN

    private static Roller RollerAt(double omega) => new(Vec3.Zero, new Vec3(1, 0, 0), R, 0.09, omega, Vec3.Zero, Vec3.Zero);

    [Fact]
    public void No_force_without_squeeze()
    {
        var ball = new Ball(new Vec3(0, -(R + Ball + 0.001), 0), Ball, Vec3.Zero, Vec3.Zero);
        Assert.Null(CompliantContact.Compute(RollerAt(10), ball, Pollen));
    }

    [Fact]
    public void Resting_squeeze_gives_a_pure_normal_force()
    {
        var ball = new Ball(new Vec3(0.02, -(R + Ball - 0.004), 0), Ball, Vec3.Zero, Vec3.Zero);
        var c = CompliantContact.Compute(RollerAt(0), ball, Pollen)!.Value;
        Assert.Equal(2000 * 0.004, c.NormalForce, 9);
        Assert.Equal(-8.0, c.Force.Y, 9);   // pushed away from the axle (down)
        Assert.Equal(0, c.ShaftTorque, 9);
        Assert.Equal(0.02, c.Point.X, 9);   // contact along the roller, not at its middle
    }

    [Fact]
    public void Spinning_roller_drags_the_ball_along_its_surface_and_is_slowed_by_it()
    {
        // Spin +ω about +X: the surface under the axle moves towards −Z; a ball touching it from below is dragged to −Z.
        var ball = new Ball(new Vec3(0, -(R + Ball - 0.003), 0), Ball, Vec3.Zero, Vec3.Zero);
        var c = CompliantContact.Compute(RollerAt(50), ball, Pollen)!.Value;
        Assert.True(c.Force.Z < 0);
        Assert.Equal(c.NormalForce * Pollen.Friction, -c.Force.Z, 6);   // full slip (ωR = 1.2 m/s ≫ s₀)
        Assert.True(c.ShaftTorque < 0);                                  // opposes the spin
        Assert.Equal(-R * Math.Abs(c.Force.Z), c.ShaftTorque, 9);
    }

    [Fact]
    public void Ball_already_at_surface_speed_feels_no_friction()
    {
        var omega = 50.0;
        // Ball carried along with the surface (−Z at ω·R) without spinning: its top point moves with the roller.
        var ball = new Ball(new Vec3(0, -(R + Ball - 0.002), 0), Ball, new Vec3(0, 0, -omega * R), Vec3.Zero);
        var c = CompliantContact.Compute(RollerAt(omega), ball, Pollen)!.Value;
        Assert.Equal(0, c.Force.Z, 6);
        Assert.Equal(0, c.ShaftTorque, 6);
    }
}
