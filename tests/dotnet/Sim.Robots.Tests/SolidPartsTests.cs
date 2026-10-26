using Sim.Core.Numerics;

namespace RobotTests;

/// <summary>
/// Every robot part is solid to game elements (USER 2026-10-26, D-037): rollers and drive wheels are solid cylinders
/// with flat ends, sprung rollers press with their preload, and the RoboDojo robot moves elements physically.
/// </summary>
public class SolidPartsTests
{
    private static readonly ContactParams Nectar = new(Stiffness: 2500, Damping: 0, Friction: 1.0, FullSlipSpeed: 0.05);
    private const double R = 0.052;      // goBILDA mecanum wheel
    private const double HalfWidth = 0.019;
    private const double Ball = 0.0457;  // NECTAR

    private static Roller Wheel() => new(Vec3.Zero, new Vec3(1, 0, 0), R, HalfWidth, 0, Vec3.Zero, Vec3.Zero);

    [Fact]
    public void A_ball_beside_the_flat_wheel_face_is_touched_only_by_the_face()
    {
        // On the axle line 3 cm beyond the face: a rounded (capsule) end would still squeeze it by R - 3 cm; a flat face
        // does not touch it.
        var clear = new Ball(new Vec3(HalfWidth + Ball + 0.03, 0, 0), Ball, Vec3.Zero, Vec3.Zero);
        Assert.Null(CompliantContact.Compute(Wheel(), clear, Nectar));
        Assert.Equal(-0.03, CompliantContact.Squeeze(Wheel(), clear), 9);

        var touching = new Ball(new Vec3(HalfWidth + Ball - 0.002, 0.02, 0), Ball, Vec3.Zero, Vec3.Zero);
        var c = CompliantContact.Compute(Wheel(), touching, Nectar)!.Value;
        Assert.Equal(2500 * 0.002, c.NormalForce, 9);
        Assert.Equal(c.NormalForce, c.Force.X, 9);   // pushed straight out of the face, along the axle
        Assert.Equal(HalfWidth, c.Point.X, 9);       // on the face
    }

    [Fact]
    public void Beyond_the_rim_edge_the_ball_is_pushed_off_the_edge()
    {
        var edge = new Vec3(HalfWidth, R, 0);
        var direction = new Vec3(1, 1, 0) * (1 / Math.Sqrt(2));
        var ball = new Ball(edge + direction * (Ball - 0.001), Ball, Vec3.Zero, Vec3.Zero);
        var c = CompliantContact.Compute(Wheel(), ball, Nectar)!.Value;
        Assert.Equal(2500 * 0.001, c.NormalForce, 6);
        Assert.Equal(c.Force.X, c.Force.Y, 6);
        Assert.Equal(edge.X, c.Point.X, 9);
        Assert.Equal(edge.Y, c.Point.Y, 9);
    }

    [Fact]
    public void A_sprung_roller_presses_with_its_preload_after_a_millimetre()
    {
        var sprung = Nectar with { Stiffness = 400, Preload = 2.0 };
        Assert.Equal(400 * 0.0005 + 1.0, NormalAtSqueeze(0.0005, sprung), 9);   // preload builds up over 1 mm
        Assert.Equal(400 * 0.004 + 2.0, NormalAtSqueeze(0.004, sprung), 9);
    }

    private static double NormalAtSqueeze(double squeeze, ContactParams p)
    {
        var ball = new Ball(new Vec3(0, -(R + Ball - squeeze), 0), Ball, Vec3.Zero, Vec3.Zero);
        return CompliantContact.Compute(Wheel(), ball, p)!.Value.NormalForce;
    }

    [Fact]
    public void The_spinning_face_drags_the_ball_around_the_axle()
    {
        // Spin +ω about +X: at +Y on the face the surface moves towards +Z.
        var spinning = Wheel() with { Omega = 20 };
        var ball = new Ball(new Vec3(HalfWidth + Ball - 0.002, 0.03, 0), Ball, Vec3.Zero, Vec3.Zero);
        var c = CompliantContact.Compute(spinning, ball, Nectar)!.Value;
        Assert.True(c.Force.Z > 0);
        Assert.True(c.ShaftTorque < 0);   // the ball brakes the wheel
    }

    [Fact]
    public void RoboDojo_moves_elements_physically_with_sprung_gecko_stages()
    {
        var robot = RobotDefinition.Load(RobotDefinition.FindConfig("robodojo_23205"));
        Assert.True(robot.BallPath!.Physical);
        Assert.Equal(2.0, robot.Mechanism("intake_roller").ContactPreload);
        foreach (var stage in new[] { "transfer_stage2", "transfer_stage3" })
        {
            var m = robot.Mechanism(stage);
            Assert.Equal(0.024, m.Radius);
            Assert.Equal("gecko", m.ContactMaterial);
            Assert.Equal(2.0, m.ContactPreload);
            Assert.Null(m.Width);   // from the CAD colliders at load in the game
        }
        Assert.Equal(0.016, robot.Mechanism("transfer_out").Radius);
        Assert.Equal(0.038, robot.WheelWidth);
    }

    [Fact]
    public void The_StarterBot_keeps_the_guided_path()
    {
        var robot = RobotDefinition.Load(RobotDefinition.FindConfig("gobilda_starter"));
        Assert.False(robot.BallPath!.Physical);
        Assert.Equal(0.0378, robot.WheelWidth);
    }
}
