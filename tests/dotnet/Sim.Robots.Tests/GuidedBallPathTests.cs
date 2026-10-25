using System.Numerics;

namespace RobotTests;

/// <summary>D-027 guided element path: capture, capacity, feeding per quarter turn, launch speed and flywheel impulse.</summary>
public class GuidedBallPathTests
{
    private static readonly RobotDefinition Robot = RobotDefinition.Load(RobotDefinition.FindConfig("gobilda_starter"));

    private static GuidedBallPath NewPath() => new(Robot.BallPath!);

    [Fact]
    public void Starter_bot_yaml_defines_the_guided_path()
    {
        var b = Robot.BallPath!;
        Assert.Equal(4, b.Capacity);
        Assert.Equal(90, b.FeedAngleDeg);
        Assert.Equal(0.5, b.ExitSpeedRatio);
        Assert.Equal(4, b.StorageSlots.Count);
        Assert.Equal(1, b.LaunchDirection.Length(), 5);
    }

    [Fact]
    public void Capture_needs_the_zone_and_an_inward_turning_roller()
    {
        var path = NewPath();
        var inZone = new Vector3(0, 0.04f, -0.16f);
        Assert.False(path.TryCapture(1, inZone, inwardSurfaceSpeed: 0.1));    // roller too slow
        Assert.False(path.TryCapture(1, inZone, inwardSurfaceSpeed: -1.0));   // spitting out
        Assert.False(path.TryCapture(1, new Vector3(0, 0.04f, -0.40f), 1.0)); // outside the zone
        Assert.True(path.TryCapture(1, inZone, 1.0));
        Assert.False(path.TryCapture(1, inZone, 1.0));                        // already stored
    }

    [Fact]
    public void A_full_hopper_takes_nothing()
    {
        var path = NewPath();
        for (var id = 1; id <= 4; id++)
        {
            Assert.True(path.Preload(id));
        }
        Assert.True(path.Full);
        Assert.False(path.TryCapture(5, new Vector3(0, 0.04f, -0.16f), 1.0));
    }

    [Fact]
    public void Feeder_launches_one_element_per_quarter_turn_only_when_the_flywheel_is_fast()
    {
        var path = NewPath();
        path.Preload(7);
        path.Preload(8);
        Assert.Null(path.Feed(60, flywheelSurfaceSpeed: 13));
        Assert.Equal(7, path.Feed(40, 13));        // 100° turned: first element out, 10° carried over
        Assert.Null(path.Feed(50, 13));
        Assert.Null(path.Feed(40, flywheelSurfaceSpeed: 1));   // flywheel too slow: the element waits in the nip
        Assert.Equal(8, path.Feed(0, 13));          // flywheel back up to speed
        Assert.Empty(path.Stored);
        Assert.Null(path.Feed(720, 13));             // empty hopper
    }

    [Fact]
    public void Launch_speed_and_flywheel_impulse_follow_rolling_kinematics()
    {
        var path = NewPath();
        // 1250 ticks/s on a 28-count 1:1 motor = 280.5 rad/s; Hogback rim 48 mm → 13.46 m/s; ball exits at half.
        var rim = 1250.0 / 28 * 2 * Math.PI * 0.048;
        Assert.Equal(6.73, path.ExitSpeed(rim), 2);
        Assert.Equal(0.02495 * 6.73 * 0.048, GuidedBallPath.FlywheelImpulse(0.02495, 6.73, 0.048), 9);
    }
}
