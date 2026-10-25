using System.Numerics;

namespace SensorTests;

/// <summary>SparkFun OTOS, REV colour, REV 2m distance and touch sensor models (spec 09).</summary>
public class OtherSensorTests
{
    private const int Status = 11, PosX = 12, PosY = 13, PosH = 14;   // OtosDevice snapshot indexes
    private const double M = OtosDevice.MeterToInt16, R = OtosDevice.RadToInt16;

    private static OtosDevice Otos(SensorTestWorld w)
    {
        var o = new OtosDevice(SensorTestWorld.Sensor("sensor_otos"), SensorTestWorld.Models, 3);
        w.Wait(0.7, o);   // power-on IMU calibration: 255 samples × 2.4 ms
        return o;
    }

    [Fact]
    public void Otos_calibrates_its_imu_after_power_on()
    {
        var w = new SensorTestWorld();
        var o = new OtosDevice(SensorTestWorld.Sensor("sensor_otos"), SensorTestWorld.Models, 3);
        w.Step(o);
        Assert.Equal(95, o.Snapshot()[0]);                 // product id 0x5F
        Assert.InRange(o.Snapshot()[5], 250, 255);          // samples left
        w.Wait(0.7, o);
        Assert.Equal(0, o.Snapshot()[5]);
    }

    [Fact]
    public void Otos_forward_is_plus_y_and_right_is_plus_x()
    {
        var w = new SensorTestWorld();
        var o = Otos(w);
        w.Move(w.RobotToWorld(new Vector3(0, 0, -1)), 0, 1.0, o);
        Assert.Equal(1.0, o.Snapshot()[PosY] / M, 0.002);
        Assert.Equal(0, o.Snapshot()[PosX] / M, 0.002);
        w.Move(w.RobotToWorld(new Vector3(0.5f, 0, 0)), 0, 1.0, o);
        Assert.Equal(0.5, o.Snapshot()[PosX] / M, 0.002);
    }

    [Fact]
    public void Otos_offset_turns_sensor_motion_into_robot_centre_motion()
    {
        var w = new SensorTestWorld();
        var right = Otos(w);
        var w2 = new SensorTestWorld();
        var zero = Otos(w2);
        // The sensor sits 5 cm behind the centre: offset (x 0, y −0.05 m, h 0), then reset tracking.
        right.Apply("write", [OtosDevice.RegOffset, 0, Math.Round(-0.05 * M), 0]);
        right.Apply("write", [OtosDevice.RegReset, 1]);
        zero.Apply("write", [OtosDevice.RegReset, 1]);
        w.Move(Vector3.Zero, Math.PI / 2, 1.0, right);
        w2.Move(Vector3.Zero, Math.PI / 2, 1.0, zero);
        Assert.Equal(Math.PI / 2, right.Snapshot()[PosH] / R, 0.01);
        Assert.InRange(Math.Abs(right.Snapshot()[PosX] / M), 0, 0.002);
        Assert.InRange(Math.Abs(right.Snapshot()[PosY] / M), 0, 0.002);
        Assert.InRange(Math.Abs(zero.Snapshot()[PosX] / M), 0.04, 0.06);   // the sensor point swings 5 cm sideways
    }

    [Fact]
    public void Otos_linear_scalar_scales_the_distance()
    {
        var w = new SensorTestWorld();
        var o = Otos(w);
        o.Apply("write", [OtosDevice.RegScalarLinear, 50]);   // 1.050
        w.Move(w.RobotToWorld(new Vector3(0, 0, -1)), 0, 1.0, o);
        Assert.Equal(1.05, o.Snapshot()[PosY] / M, 0.002);
    }

    [Fact]
    public void Otos_stops_tracking_when_tilted()
    {
        var w = new SensorTestWorld();
        var o = Otos(w);
        w.Tilt = Quaternion.CreateFromAxisAngle(Vector3.UnitX, 0.2f);   // 11° nose up
        w.Move(new Vector3(0, 0, -0.5f), 0, 0.5, o);
        Assert.Equal(1, (int)o.Snapshot()[Status] & 1);                         // warnTiltAngle
        Assert.Equal(0, o.Snapshot()[PosY] / M, 0.002);
    }

    [Fact]
    public void Otos_set_position_is_the_new_robot_pose()
    {
        var w = new SensorTestWorld();
        var o = Otos(w);
        o.Apply("write", [OtosDevice.RegPosition, Math.Round(0.3 * M), Math.Round(-0.2 * M), Math.Round(Math.PI / 2 * R)]);
        w.Move(w.RobotToWorld(new Vector3(0, 0, -0.1f)), 0, 0.5, o);
        Assert.Equal(0.2, o.Snapshot()[PosX] / M, 0.002);    // heading 90°: forward is −X in the tracking frame
        Assert.Equal(-0.2, o.Snapshot()[PosY] / M, 0.002);
    }

    [Fact]
    public void Color_sensor_sees_a_yellow_ball_close_and_ambient_light_otherwise()
    {
        var w = new SensorTestWorld();
        var c = new ColorSensorDevice(SensorTestWorld.Sensor("sensor_color"), SensorTestWorld.Models, 5);
        w.Step(c);
        var empty = c.Snapshot();
        Assert.Equal(6.0, empty[4], 3);                       // saturated distance (cm)
        Assert.InRange(empty[0], 30, 50);                      // ambient counts
        // A yellow surface 2 cm below the sensor (the sensor looks down from 8 cm).
        w.Planes.Add((new Vector3(0, 0.06f, 0), Vector3.UnitY, new Vector3(0.9f, 0.8f, 0.05f)));
        w.Step(c);
        var s = c.Snapshot();
        Assert.Equal(2.0, s[4], 0.3);
        Assert.True(s[0] > 5 * s[2] && s[1] > 5 * s[2], $"yellow: r {s[0]} g {s[1]} b {s[2]}");
        Assert.True(s[5] > empty[5]);                          // raw proximity rises when close
    }

    [Fact]
    public void Distance_sensor_ranges_a_wall_and_reports_out_of_range_beyond_2_m()
    {
        var w = new SensorTestWorld();
        var d = new DistanceSensorDevice(SensorTestWorld.Sensor("sensor_distance"), SensorTestWorld.Models, 9);
        w.Step(d);
        Assert.Equal(8190, d.Snapshot()[0]);
        w.Planes.Add((new Vector3(0, 0, -1.226f), Vector3.UnitZ, Vector3.One));   // wall 1 m in front of the sensor
        w.Wait(0.05, d);
        Assert.InRange(d.Snapshot()[0], 900, 1100);
        Assert.Equal(238, d.Snapshot()[1]);                    // VL53L0X model id 0xEE
    }

    [Fact]
    public void Touch_sensor_is_pressed_against_a_wall()
    {
        var w = new SensorTestWorld();
        var t = new TouchSensorDevice(SensorTestWorld.Sensor("sensor_touch"), SensorTestWorld.Models);
        w.Step(t);
        Assert.Equal(0, t.Snapshot()[0]);
        w.Planes.Add((new Vector3(0, 0, 0.230f), -Vector3.UnitZ, Vector3.One));   // wall 2 mm behind the button
        w.Step(t);
        Assert.Equal(1, t.Snapshot()[0]);
    }
}
