namespace RunnerTests;

/// <summary>Wire format, hub mounting math and config loading of the simulator side of the runner protocol.</summary>
public class ProtocolTests
{
    [Fact]
    public void Frames_are_big_endian_with_a_length_prefix_and_round_trip()
    {
        var w = new FrameWriter(MessageType.Tick).I32(0x01020304).I64(-2).F32(1.5f).F64(-0.25).Str("ŝtop ✓").Str(null).Bool(true);
        var bytes = w.Frame.ToArray();
        Assert.Equal(new byte[] { 0, 0, 0, (byte)(bytes.Length - 4), MessageType.Tick, 1, 2, 3, 4 }, bytes[..9]);

        using var stream = new MemoryStream(bytes);
        var r = FrameReader.Read(stream)!;
        Assert.Equal(MessageType.Tick, r.U8());
        Assert.Equal(0x01020304, r.I32());
        Assert.Equal(-2L, r.I64());
        Assert.Equal(1.5f, r.F32());
        Assert.Equal(-0.25, r.F64());
        Assert.Equal("ŝtop ✓", r.Str());
        Assert.Null(r.Str());
        Assert.True(r.Bool());
        Assert.Equal(0, r.Remaining);
        Assert.Null(FrameReader.Read(stream));   // clean end of stream
    }

    [Fact]
    public void Logo_up_usb_forward_is_the_identity_mounting()
    {
        AssertSameRotation(Quat.Identity, RunnerBridge.HubMount("UP", "FORWARD"));
    }

    [Fact]
    public void Usb_left_turns_the_chip_90_degrees_counter_clockwise()
    {
        // Chip +Y (USB) points left, so chip +X points forward: a +90° yaw of the chip relative to the robot.
        AssertSameRotation(Quat.FromYaw(Math.PI / 2), RunnerBridge.HubMount("UP", "LEFT"));
    }

    [Theory]
    [InlineData("UP", "UP")]
    [InlineData("LEFT", "RIGHT")]
    public void Impossible_mountings_are_rejected(string logo, string usb)
    {
        Assert.Throws<RunnerException>(() => RunnerBridge.HubMount(logo, usb));
    }

    [Fact]
    public void Imu_reading_follows_the_mounting_in_the_robot_yaml()
    {
        var hardware = HardwareConfig.Load();
        var robot = RobotDefinition.Load(RobotDefinition.FindConfig("gobilda_starter"));
        var bridge = new RunnerBridge(new RobotHardware(robot, hardware, 240), hardware.HubController);
        var yaw = Quat.FromYaw(0.5);
        var (orientation, rate) = bridge.Imu(yaw, new Vec3d(0, 0, 1.25));
        AssertSameRotation(yaw, orientation);   // starter bot: logo UP, USB FORWARD
        Assert.Equal(1.25, rate.Z, 12);

        // A roll rate about the robot's forward axis appears about chip +X when USB points left.
        var mount = RunnerBridge.HubMount("UP", "LEFT");
        var chipRate = mount.Conjugate.Rotate(new Vec3d(0, 2, 0));
        Assert.Equal(2, chipRate.X, 12);
        Assert.Equal(0, chipRate.Y, 12);
    }

    [Fact]
    public void Hardware_costs_load_from_the_latency_config()
    {
        var costs = HardwareCosts.Load();
        Assert.Equal(new HardwareCosts(2_000_000, 3_000_000, 3_000_000, 500_000, 1_000, 20_000_000, 7_000_000, 2_000_000, 3_000_000), costs);
    }

    [Fact]
    public void Devices_use_the_exact_names_and_sdk_motor_types()
    {
        var hardware = HardwareConfig.Load();
        var robot = RobotDefinition.Load(RobotDefinition.FindConfig("gobilda_starter"));
        var devices = new RunnerBridge(new RobotHardware(robot, hardware, 240), hardware.HubController).Devices();
        var launcher = devices.Single(d => d.Name == "launcher");
        Assert.Equal(("motor", 1, 1), (launcher.Type, launcher.Hub, launcher.Port));
        Assert.Equal("GoBILDA 5202/3/4 series", launcher.MotorTypeName);
        Assert.Equal(2786.0, launcher.TicksPerRev);
        Assert.Equal("crservo", devices.Single(d => d.Name == "windmillServo").Type);
        Assert.Equal("imu", devices.Single(d => d.Name == "imu").Type);
        Assert.Equal(10, devices.Count);
    }

    private static void AssertSameRotation(Quat expected, Quat actual)
    {
        var dot = expected.W * actual.W + expected.X * actual.X + expected.Y * actual.Y + expected.Z * actual.Z;
        Assert.Equal(1, Math.Abs(dot), 9);   // q and −q are the same rotation
    }
}
