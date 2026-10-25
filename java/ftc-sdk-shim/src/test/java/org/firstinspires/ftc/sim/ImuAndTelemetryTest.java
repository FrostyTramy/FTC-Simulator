package org.firstinspires.ftc.sim;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.qualcomm.hardware.rev.RevHubOrientationOnRobot;
import com.qualcomm.hardware.rev.RevHubOrientationOnRobot.LogoFacingDirection;
import com.qualcomm.hardware.rev.RevHubOrientationOnRobot.UsbFacingDirection;
import com.qualcomm.robotcore.hardware.IMU;
import java.util.List;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;
import org.firstinspires.ftc.robotcore.external.navigation.YawPitchRollAngles;
import org.junit.jupiter.api.Test;

/** IMU orientation for every hub mounting (spec 06 "IMU") and telemetry behaviour. */
class ImuAndTelemetryTest {
  private static Quaternion yawQuaternion(double degrees) {
    double h = Math.toRadians(degrees) / 2;
    return new Quaternion((float) Math.cos(h), 0, 0, (float) Math.sin(h), 0);
  }

  @Test
  void everyValidHubMountingReportsTheRobotYawWhenConfiguredCorrectly() {
    int combos = 0;
    for (LogoFacingDirection logo : LogoFacingDirection.values()) {
      for (UsbFacingDirection usb : UsbFacingDirection.values()) {
        RevHubOrientationOnRobot mount;
        try {
          mount = new RevHubOrientationOnRobot(logo, usb);
        } catch (IllegalArgumentException parallel) {
          continue;
        }
        combos++;
        SimRuntime runtime = ShimTestSupport.freshRuntime();
        IMU imu = ShimTestSupport.starterBot(runtime).hardwareMap().get(IMU.class, "imu");
        imu.initialize(new IMU.Parameters(mount));
        // The robot is turned 30° to the left and pitched 10° nose up: the physical hub orientation is
        // robot ⊗ mount; the IMU must give the robot angles back.
        Quaternion pitch = new Quaternion((float) Math.cos(Math.toRadians(5)), (float) Math.sin(Math.toRadians(5)), 0, 0, 0);
        Quaternion robot = yawQuaternion(30).multiply(pitch, 0);
        Quaternion hub = robot.multiply(mount.imuCoordinateSystemOrientationFromPerspectiveOfRobot(), 0);
        runtime.bus().setImu(new double[] {hub.w, hub.x, hub.y, hub.z}, new double[] {0, 0, 0});
        YawPitchRollAngles a = imu.getRobotYawPitchRollAngles();
        assertEquals(30, a.getYaw(AngleUnit.DEGREES), 1e-3, logo + "/" + usb);
        assertEquals(10, a.getPitch(AngleUnit.DEGREES), 1e-3, logo + "/" + usb);
        assertEquals(0, a.getRoll(AngleUnit.DEGREES), 1e-3, logo + "/" + usb);
      }
    }
    assertEquals(24, combos);
  }

  @Test
  void parallelLogoAndUsbDirectionsAreRejected() {
    assertThrows(IllegalArgumentException.class, () -> new RevHubOrientationOnRobot(LogoFacingDirection.UP, UsbFacingDirection.UP));
    assertThrows(IllegalArgumentException.class, () -> new RevHubOrientationOnRobot(LogoFacingDirection.UP, UsbFacingDirection.DOWN));
  }

  @Test
  void wrongMountingInUserCodeGivesWrongAnglesLikeOnTheRealRobot() {
    SimRuntime runtime = ShimTestSupport.freshRuntime();
    IMU imu = ShimTestSupport.starterBot(runtime).hardwareMap().get(IMU.class, "imu");
    RevHubOrientationOnRobot real = new RevHubOrientationOnRobot(LogoFacingDirection.LEFT, UsbFacingDirection.UP);
    imu.initialize(new IMU.Parameters(new RevHubOrientationOnRobot(LogoFacingDirection.UP, UsbFacingDirection.FORWARD)));
    Quaternion hub = yawQuaternion(45).multiply(real.imuCoordinateSystemOrientationFromPerspectiveOfRobot(), 0);
    runtime.bus().setImu(new double[] {hub.w, hub.x, hub.y, hub.z}, new double[] {0, 0, 0});
    assertTrue(Math.abs(imu.getRobotYawPitchRollAngles().getPitch(AngleUnit.DEGREES)) > 45);
  }

  @Test
  void resetYawZeroesTheHeading() {
    SimRuntime runtime = ShimTestSupport.freshRuntime();
    IMU imu = ShimTestSupport.starterBot(runtime).hardwareMap().get(IMU.class, "imu");
    imu.initialize(new IMU.Parameters(new RevHubOrientationOnRobot(LogoFacingDirection.UP, UsbFacingDirection.FORWARD)));
    Quaternion q = yawQuaternion(-70);
    runtime.bus().setImu(new double[] {q.w, q.x, q.y, q.z}, new double[] {0, 0, 0.5});
    imu.resetYaw();
    assertEquals(0, imu.getRobotYawPitchRollAngles().getYaw(), 1e-3);
    assertEquals(Math.toDegrees(0.5), imu.getRobotAngularVelocity(AngleUnit.DEGREES).zRotationRate, 1e-3);
  }

  @Test
  void telemetryIsRateLimitedAndAutoCleared() throws InterruptedException {
    SimRuntime runtime = ShimTestSupport.freshRuntime();
    SimTelemetry t = new SimTelemetry(runtime);
    t.addData("Status", "Initialized");
    assertTrue(t.update());
    assertEquals(List.of("Status : Initialized"), t.lastTransmitted());
    t.addData("Status", "Running");
    assertFalse(t.update()); // within 250 ms of the last transmission
    runtime.sleepUntil(runtime.nowNanos() + 300_000_000L);
    t.addData("Launcher", "%.0f ticks/s", 1250.0);
    t.addLine("line").addData("a", 1).addData("b", 2);
    assertTrue(t.update());
    assertEquals(List.of("Launcher : 1250 ticks/s", "linea : 1 | b : 2"), t.lastTransmitted());
  }

  @Test
  void telemetryLogKeepsTheLastLinesAndSendsThemAsSoonAsAllowed() throws InterruptedException {
    SimRuntime runtime = ShimTestSupport.freshRuntime();
    SimTelemetry t = new SimTelemetry(runtime);
    t.log().setCapacity(2);
    t.log().add("one"); // SDK LogImpl.add transmits at once when the interval allows
    assertEquals(List.of("one"), t.lastTransmitted());
    t.log().add("two");
    t.log().add("three");
    assertFalse(t.update()); // within 250 ms
    runtime.sleepUntil(runtime.nowNanos() + 260_000_000L);
    assertTrue(t.tryUpdateIfDirty());
    assertEquals(List.of("two", "three"), t.lastTransmitted());
  }

  @Test
  void throttledUpdateIsSentByTheEventLoopOnceTheIntervalPasses() throws InterruptedException {
    SimRuntime runtime = ShimTestSupport.freshRuntime();
    SimTelemetry t = new SimTelemetry(runtime);
    t.addData("a", 1);
    assertTrue(t.update());
    t.addData("Status", "Done"); // auto-clear removes "a" now, at the next addData
    assertFalse(t.update()); // throttled: marked dirty, items kept
    assertFalse(t.tryUpdateIfDirty()); // still within 250 ms
    runtime.sleepUntil(runtime.nowNanos() + 260_000_000L);
    assertTrue(t.tryUpdateIfDirty());
    assertEquals(List.of("Status : Done"), t.lastTransmitted());
    assertFalse(t.tryUpdateIfDirty()); // clean again
  }
}
