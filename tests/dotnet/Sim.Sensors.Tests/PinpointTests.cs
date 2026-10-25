using System.Numerics;

namespace SensorTests;

/// <summary>goBILDA Pinpoint model: firmware odometry from physical pods and its gyro (spec 09).</summary>
public class PinpointTests
{
    private const int X = 4, Y = 5, H = 6;   // snapshot indexes (PinpointDevice)

    private static PinpointDevice Pinpoint(ulong seed = 1) => new(SensorTestWorld.Sensor("pinpoint"), SensorTestWorld.Models, seed);

    /// <summary>The configuration of SDK sample SensorGoBildaPinpoint: offsets −84/−168 mm, 4-bar pods, both FORWARD.</summary>
    private static void ConfigureLikeTheSample(PinpointDevice p, SensorTestWorld w)
    {
        p.Apply("write", [PinpointDevice.RegXPodOffset, -84]);
        p.Apply("write", [PinpointDevice.RegYPodOffset, -168]);
        p.Apply("write", [PinpointDevice.RegMmPerTick, 19.89436789]);
        p.Apply("write", [PinpointDevice.RegDeviceControl, 1 << 5]);
        p.Apply("write", [PinpointDevice.RegDeviceControl, 1 << 3]);
        p.Apply("write", [PinpointDevice.RegDeviceControl, 1 << 1]);   // resetPosAndIMU
        w.Wait(0.3, p);
    }

    [Fact]
    public void Calibrates_after_power_on_then_reports_ready()
    {
        var w = new SensorTestWorld();
        var p = Pinpoint();
        w.Step(p);
        Assert.Equal(PinpointDevice.StatusCalibrating, (int)p.Snapshot()[0]);
        w.Wait(0.3, p);
        Assert.Equal(PinpointDevice.StatusReady, (int)p.Snapshot()[0]);
        Assert.Equal(666, p.Snapshot()[1]);
    }

    [Fact]
    public void Driving_forward_one_metre_reads_1000_mm_in_x()
    {
        var w = new SensorTestWorld();
        var p = Pinpoint();
        ConfigureLikeTheSample(p, w);
        w.Move(w.RobotToWorld(new Vector3(0, 0, -1)), 0, 2.0, p);
        var s = p.Snapshot();
        Assert.Equal(1000, s[X], 0.5);   // pod counts are quantized at 19.9 per mm
        Assert.Equal(0, s[Y], 0.5);
        Assert.Equal(0, s[H], 0.01);
        Assert.Equal(19894, s[2], 1);    // raw X pod counts
    }

    [Fact]
    public void Strafing_left_reads_positive_y()
    {
        var w = new SensorTestWorld();
        var p = Pinpoint();
        ConfigureLikeTheSample(p, w);
        w.Move(w.RobotToWorld(new Vector3(-0.5f, 0, 0)), 0, 1.0, p);
        Assert.Equal(500, p.Snapshot()[Y], 0.5);
        Assert.Equal(0, p.Snapshot()[X], 0.5);
    }

    [Fact]
    public void Turning_in_place_keeps_the_position_when_the_offsets_are_right()
    {
        var w = new SensorTestWorld();
        var p = Pinpoint();
        ConfigureLikeTheSample(p, w);
        w.Move(Vector3.Zero, Math.PI / 2, 1.0, p);
        var s = p.Snapshot();
        Assert.Equal(Math.PI / 2, s[H], 0.01);   // gyro noise and residual bias only
        Assert.InRange(Math.Abs(s[X]), 0, 1.0);
        Assert.InRange(Math.Abs(s[Y]), 0, 1.0);
    }

    [Fact]
    public void Wrong_offsets_make_a_turn_in_place_look_like_travel()
    {
        var w = new SensorTestWorld();
        var p = Pinpoint();
        ConfigureLikeTheSample(p, w);
        p.Apply("write", [PinpointDevice.RegXPodOffset, 0]);
        p.Apply("write", [PinpointDevice.RegYPodOffset, 0]);
        w.Move(Vector3.Zero, Math.PI / 2, 1.0, p);
        var s = p.Snapshot();
        // The X pod (84 mm right of the centre) travels 84 mm · π/2 = 132 mm along its arc.
        Assert.InRange(Math.Sqrt(s[X] * s[X] + s[Y] * s[Y]), 100, 400);
    }

    [Fact]
    public void Swingarm_resolution_on_4_bar_pods_overreads_by_1_5()
    {
        var w = new SensorTestWorld();
        var p = Pinpoint();
        ConfigureLikeTheSample(p, w);
        p.Apply("write", [PinpointDevice.RegMmPerTick, 13.26291192]);
        w.Move(w.RobotToWorld(new Vector3(0, 0, -1)), 0, 2.0, p);
        Assert.Equal(1000 * 19.89436789 / 13.26291192, p.Snapshot()[X], 1.0);
    }

    [Fact]
    public void Reversed_x_encoder_reads_backwards()
    {
        var w = new SensorTestWorld();
        var p = Pinpoint();
        ConfigureLikeTheSample(p, w);
        p.Apply("write", [PinpointDevice.RegDeviceControl, 1 << 4]);
        w.Move(w.RobotToWorld(new Vector3(0, 0, -0.5f)), 0, 1.0, p);
        Assert.Equal(-500, p.Snapshot()[X], 0.5);
        Assert.True(p.Snapshot()[2] > 0, "the encoder register stays raw");
    }

    [Fact]
    public void Set_position_moves_the_pose_and_tracking_continues_from_there()
    {
        var w = new SensorTestWorld();
        var p = Pinpoint();
        ConfigureLikeTheSample(p, w);
        p.Apply("write", [PinpointDevice.RegXPosition, 100]);
        p.Apply("write", [PinpointDevice.RegYPosition, -200]);
        p.Apply("write", [PinpointDevice.RegHOrientation, Math.PI / 2]);
        w.Move(w.RobotToWorld(new Vector3(0, 0, -0.3f)), 0, 1.0, p);
        var s = p.Snapshot();
        Assert.Equal(100, s[X], 1);       // heading +90°: forward travel goes to +Y
        Assert.Equal(100, s[Y], 1);
        Assert.Equal(Math.PI / 2, s[H], 0.01);
    }

    [Fact]
    public void Moving_during_calibration_leaves_a_heading_drift()
    {
        var (wStill, wMoving) = (new SensorTestWorld(), new SensorTestWorld());
        var (still, moving) = (Pinpoint(7), Pinpoint(7));
        wStill.Wait(0.3, still);
        wMoving.Wait(0.3, moving);
        still.Apply("write", [PinpointDevice.RegDeviceControl, 1 << 1]);    // resetPosAndIMU: 0.25 s calibration
        moving.Apply("write", [PinpointDevice.RegDeviceControl, 1 << 1]);
        wStill.Wait(0.25, still);
        wMoving.Move(Vector3.Zero, 0.2, 0.25, moving);                     // turned 0.2 rad while calibrating: 0.8 rad/s
        wStill.Wait(2.0, still);
        wMoving.Wait(2.0, moving);
        Assert.InRange(Math.Abs(still.Snapshot()[H]), 0, 0.01);
        Assert.InRange(moving.Snapshot()[H], -1.8, -1.4);                    // the zero rate it learnt: −0.8 rad/s for 2 s
    }

    [Fact]
    public void Same_seed_same_readings()
    {
        double[] Run()
        {
            var w = new SensorTestWorld();
            var p = Pinpoint(42);
            ConfigureLikeTheSample(p, w);
            w.Move(w.RobotToWorld(new Vector3(0.3f, 0, -0.7f)), 1.0, 1.5, p);
            return p.Snapshot();
        }
        Assert.Equal(Run(), Run());
    }
}
