package org.firstinspires.ftc.sim;

import static org.junit.jupiter.api.Assertions.assertArrayEquals;
import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.qualcomm.hardware.gobilda.GoBildaPinpointDriver;
import com.qualcomm.hardware.lynx.LynxModule;
import com.qualcomm.hardware.rev.Rev2mDistanceSensor;
import com.qualcomm.hardware.sparkfun.SparkFunOTOS;
import com.qualcomm.robotcore.hardware.DigitalChannel;
import com.qualcomm.robotcore.hardware.DistanceSensor;
import com.qualcomm.robotcore.hardware.HardwareMap;
import com.qualcomm.robotcore.hardware.NormalizedColorSensor;
import com.qualcomm.robotcore.hardware.NormalizedRGBA;
import com.qualcomm.robotcore.hardware.SwitchableLight;
import com.qualcomm.robotcore.hardware.TouchSensor;
import java.util.List;
import java.util.Map;
import org.firstinspires.ftc.robotcore.external.hardware.camera.WebcamName;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Pose2D;
import org.firstinspires.ftc.robotcore.external.navigation.UnnormalizedAngleUnit;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

/** SDK sensor drivers on simulated devices (spec 09): values, units, costs and the writes they send. */
class SensorShimTest {
  private SimRuntime runtime;
  private HardwareMap map;

  @BeforeEach
  void setUp() {
    runtime = ShimTestSupport.freshRuntime();
    map = new SimRobot(runtime, List.of(
        new SimRobot.DeviceSpec("pinpoint", "pinpoint", 0, 1, null, 0, 0, 0, 0, null),
        new SimRobot.DeviceSpec("sensor_otos", "otos", 0, 2, null, 0, 0, 0, 0, null),
        new SimRobot.DeviceSpec("sensor_color", "color", 0, 3, null, 0, 0, 0, 0, null),
        new SimRobot.DeviceSpec("sensor_distance", "distance", 1, 0, null, 0, 0, 0, 0, null),
        new SimRobot.DeviceSpec("sensor_touch", "touch", 0, 1, null, 0, 0, 0, 0, null),
        new SimRobot.DeviceSpec("digitalTouch", "digital", 1, 3, null, 0, 0, 0, 0, null),
        new SimRobot.DeviceSpec("Webcam 1", "webcam", 0, 0, null, 0, 0, 0, 0, null, Map.of("width", 640.0, "fx", 622.0))),
        2).hardwareMap();
  }

  private void tick() {
    runtime.bus().countTick();
  }

  private static double[] pinpointValues(double x, double y, double h) {
    double[] v = new double[22];
    v[0] = 1; // READY
    v[1] = 666;
    v[2] = 1000;
    v[3] = -50;
    v[4] = x;
    v[5] = y;
    v[6] = h;
    v[10] = 1;
    v[12] = 1;
    v[13] = 19.89436789;
    v[16] = 1;
    return v;
  }

  @Test
  void pinpoint_update_reads_the_pose_in_mm_and_radians_in_one_i2c_read() {
    GoBildaPinpointDriver p = map.get(GoBildaPinpointDriver.class, "pinpoint");
    runtime.bus().setSensorInput("pinpoint", pinpointValues(254, -25.4, Math.PI / 2 + 2 * Math.PI));
    long before = runtime.nowNanos();
    p.update();
    assertEquals(runtime.costs().controlHubI2cReadNanos, runtime.nowNanos() - before);
    assertEquals(10.0, p.getPosX(DistanceUnit.INCH), 1e-4);
    assertEquals(-1.0, p.getPosY(DistanceUnit.INCH), 1e-4);
    assertEquals(90.0, p.getHeading(AngleUnit.DEGREES), 1e-3); // normalized
    assertEquals(450.0, p.getHeading(UnnormalizedAngleUnit.DEGREES), 1e-3);
    assertEquals(1000, p.getEncoderX());
    assertEquals(GoBildaPinpointDriver.DeviceStatus.READY, p.getDeviceStatus());
    assertEquals(1_000_000.0 / 666, p.getFrequency(), 1e-6);
    Pose2D pose = p.getPosition();
    assertEquals(254, pose.getX(DistanceUnit.MM), 1e-3);
  }

  @Test
  void pinpoint_setters_send_the_driver_register_writes() {
    GoBildaPinpointDriver p = map.get(GoBildaPinpointDriver.class, "pinpoint");
    p.setOffsets(-84.0, -168.0, DistanceUnit.MM);
    p.setEncoderResolution(GoBildaPinpointDriver.GoBildaOdometryPods.goBILDA_4_BAR_POD);
    p.setEncoderDirections(GoBildaPinpointDriver.EncoderDirection.FORWARD, GoBildaPinpointDriver.EncoderDirection.REVERSED);
    p.resetPosAndIMU();
    List<DeviceBus.SensorCommand> c = runtime.bus().drainSensorCommands();
    assertEquals(6, c.size());
    assertArrayEquals(new double[] {15, -84}, c.get(0).args(), 1e-6);
    assertArrayEquals(new double[] {16, -168}, c.get(1).args(), 1e-6);
    assertArrayEquals(new double[] {14, 19.89436789f}, c.get(2).args(), 1e-6);
    assertArrayEquals(new double[] {4, 1 << 5}, c.get(3).args(), 1e-6);
    assertArrayEquals(new double[] {4, 1 << 2}, c.get(4).args(), 1e-6);
    assertArrayEquals(new double[] {4, 1 << 1}, c.get(5).args(), 1e-6);
    assertEquals(-84.0f, p.getXOffset(DistanceUnit.MM), 1e-6); // the written register reads back before the sim reports it
  }

  @Test
  void pinpoint_written_position_shows_until_the_simulator_reports_it() {
    GoBildaPinpointDriver p = map.get(GoBildaPinpointDriver.class, "pinpoint");
    runtime.bus().setSensorInput("pinpoint", pinpointValues(500, 0, 0));
    tick();
    p.setPosition(new Pose2D(DistanceUnit.INCH, 10, 0, AngleUnit.DEGREES, 0));
    p.update();
    assertEquals(254, p.getPosX(DistanceUnit.MM), 1e-3);
    tick();
    p.update();
    assertEquals(254, p.getPosX(DistanceUnit.MM), 1e-3);
    tick();
    p.update();
    assertEquals(500, p.getPosX(DistanceUnit.MM), 1e-3); // two ticks later: whatever the device now reports
  }

  @Test
  void pinpoint_flags_a_bad_read_when_the_device_reports_no_loop_time() {
    GoBildaPinpointDriver p = map.get(GoBildaPinpointDriver.class, "pinpoint");
    p.update(); // no data yet: loop time 0 (GBDRV isPositionCorrupt noData)
    assertEquals(GoBildaPinpointDriver.DeviceStatus.FAULT_BAD_READ, p.getDeviceStatus());
  }

  @Test
  void otos_converts_int16_registers_to_inches_and_degrees_by_default() {
    SparkFunOTOS otos = map.get(SparkFunOTOS.class, "sensor_otos");
    double[] v = new double[30];
    v[0] = 95;
    v[12] = Math.round(0.254 * 3276.8); // x = 10 in
    v[13] = Math.round(-0.0254 * 3276.8);
    v[14] = Math.round(Math.PI / 2 * 10430.378350470453);
    runtime.bus().setSensorInput("sensor_otos", v);
    assertTrue(otos.isConnected());
    SparkFunOTOS.Pose2D pos = otos.getPosition();
    assertEquals(10.0, pos.x, 0.02);
    assertEquals(-1.0, pos.y, 0.02);
    assertEquals(90.0, pos.h, 0.01);
    otos.setLinearUnit(DistanceUnit.METER);
    otos.setAngularUnit(AngleUnit.RADIANS);
    assertEquals(0.254, otos.getPosition().x, 0.0005);
  }

  @Test
  void otos_scalar_out_of_range_is_refused_and_writes_go_out_in_raw_units() {
    SparkFunOTOS otos = map.get(SparkFunOTOS.class, "sensor_otos");
    assertFalse(otos.setLinearScalar(1.2));
    assertTrue(otos.setLinearScalar(1.05));
    otos.setOffset(new SparkFunOTOS.Pose2D(-5, 10, -90)); // inches, degrees
    List<DeviceBus.SensorCommand> c = runtime.bus().drainSensorCommands();
    assertEquals(2, c.size());
    assertArrayEquals(new double[] {4, 50}, c.get(0).args(), 1e-9);
    assertEquals(16, c.get(1).args()[0]);
    assertEquals((short) (-0.127 * 3276.8), c.get(1).args()[1], 1e-9);
    assertEquals(1.05, otos.getLinearScalar(), 1e-9);
  }

  @Test
  void otos_calibrate_imu_waits_in_virtual_time_until_the_samples_are_done() {
    SparkFunOTOS otos = map.get(SparkFunOTOS.class, "sensor_otos");
    double[] v = new double[30];
    runtime.bus().setSensorInput("sensor_otos", v);
    long before = runtime.nowNanos();
    assertFalse(otos.calibrateImu(10, true)); // the written sample count shows; the test never lowers it...
    // ... so it polls numSamples times, 3 ms apart, plus its I2C reads, then gives up.
    assertTrue(runtime.nowNanos() - before > 10 * 3_000_000L);
  }

  @Test
  void color_sensor_reports_raw_and_normalized_colours_and_distance() {
    NormalizedColorSensor color = map.get(NormalizedColorSensor.class, "sensor_color");
    runtime.bus().setSensorInput("sensor_color", new double[] {6000, 5000, 400, 3800, 2.5, 900, 0xC2, 1});
    color.setGain(2);
    NormalizedRGBA rgba = color.getNormalizedColors();
    assertEquals(12000 / 65535f, rgba.red, 1e-6);
    assertEquals(2.5, ((DistanceSensor) color).getDistance(DistanceUnit.CM), 1e-9);
    assertFalse(color instanceof SwitchableLight); // like the SDK: SensorColor's light toggle is skipped
  }

  @Test
  void distance_sensor_passes_the_out_of_range_code() {
    DistanceSensor d = map.get(DistanceSensor.class, "sensor_distance");
    runtime.bus().setSensorInput("sensor_distance", new double[] {8190, 0xEE, 0});
    long before = runtime.nowNanos();
    assertEquals(819.0, d.getDistance(DistanceUnit.CM), 1e-9);
    assertEquals(runtime.costs().expansionHubI2cReadNanos, runtime.nowNanos() - before);
    assertEquals((byte) 0xEE, ((Rev2mDistanceSensor) d).getModelID());
  }

  @Test
  void touch_sensor_reads_through_the_hub_bulk_cache() {
    TouchSensor touch = map.get(TouchSensor.class, "sensor_touch");
    DigitalChannel channel = map.get(DigitalChannel.class, "digitalTouch");
    runtime.bus().setSensorInput("sensor_touch", new double[] {1});
    runtime.bus().setSensorInput("digitalTouch", new double[] {0});
    assertTrue(touch.isPressed());
    assertEquals(1.0, touch.getValue());
    assertTrue(channel.getState()); // not pressed: pulled high
    LynxModule hub = map.getAll(LynxModule.class).get(0);
    hub.setBulkCachingMode(LynxModule.BulkCachingMode.MANUAL);
    assertTrue(touch.isPressed());
    runtime.bus().setSensorInput("sensor_touch", new double[] {0});
    assertTrue(touch.isPressed(), "MANUAL cache: stale until clearBulkCache");
    hub.clearBulkCache();
    assertFalse(touch.isPressed());
  }

  @Test
  void webcam_is_in_the_hardware_map_with_its_model_size() {
    WebcamName cam = map.get(WebcamName.class, "Webcam 1");
    assertTrue(cam.isWebcam());
    assertEquals(640, cam.getCameraCharacteristics().getDefaultSize().getWidth());
  }
}
