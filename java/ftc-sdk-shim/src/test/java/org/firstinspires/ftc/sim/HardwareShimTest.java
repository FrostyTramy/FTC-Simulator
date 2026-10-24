package org.firstinspires.ftc.sim;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertFalse;
import static org.junit.jupiter.api.Assertions.assertThrows;
import static org.junit.jupiter.api.Assertions.assertTrue;

import com.qualcomm.hardware.lynx.LynxModule;
import com.qualcomm.robotcore.exception.TargetPositionNotSetException;
import com.qualcomm.robotcore.hardware.CRServo;
import com.qualcomm.robotcore.hardware.DcMotor;
import com.qualcomm.robotcore.hardware.DcMotorEx;
import com.qualcomm.robotcore.hardware.DcMotorSimple;
import com.qualcomm.robotcore.hardware.Gamepad;
import com.qualcomm.robotcore.hardware.HardwareMap;
import com.qualcomm.robotcore.hardware.PIDFCoefficients;
import com.qualcomm.robotcore.hardware.Servo;
import java.util.List;
import org.junit.jupiter.api.BeforeEach;
import org.junit.jupiter.api.Test;

/** SDK-visible device behaviour of the shim (spec 06 "Exact behaviors to replicate"). */
class HardwareShimTest {
  private SimRuntime runtime;
  private HardwareMap hw;

  @BeforeEach
  void setUp() {
    runtime = ShimTestSupport.freshRuntime();
    hw = ShimTestSupport.starterBot(runtime).hardwareMap();
  }

  private DeviceBus.MotorCommand cmd(String key) {
    return runtime.bus().motorCommands().get(key);
  }

  @Test
  void hardwareMapFindsDevicesByNameAndTypeLikeTheSdk() {
    DcMotor m = hw.get(DcMotor.class, "left_front_drive");
    assertTrue(hw.get(DcMotorEx.class, "left_front_drive") == m); // DcMotorImplEx serves both types
    assertTrue(hw.dcMotor.get("intake") instanceof DcMotorEx);
    IllegalArgumentException e = assertThrows(IllegalArgumentException.class, () -> hw.get(Servo.class, "windmillServo"));
    assertEquals("Unable to find a hardware device with name \"windmillServo\" and type Servo", e.getMessage());
    assertThrows(IllegalArgumentException.class, () -> hw.get(DcMotor.class, "Left_Front_Drive")); // case sensitive
    assertEquals(2, hw.getAll(LynxModule.class).size());
  }

  @Test
  void reverseDirectionNegatesPowerAndPositionAndPowerIsClipped() {
    DcMotor m = hw.get(DcMotor.class, "left_front_drive");
    m.setDirection(DcMotorSimple.Direction.REVERSE);
    m.setPower(1.7);
    assertEquals(1.0, m.getPower());
    assertEquals(-1.0, cmd("0:0").power);
    runtime.bus().motorInput("0:0").position = 120;
    assertEquals(-120, m.getCurrentPosition());
  }

  @Test
  void runToPositionNeedsATargetFirst() {
    DcMotor m = hw.get(DcMotor.class, "intake");
    TargetPositionNotSetException e = assertThrows(TargetPositionNotSetException.class, () -> m.setMode(DcMotor.RunMode.RUN_TO_POSITION));
    assertTrue(e.getMessage().startsWith("Failed to enable motor."));
    m.setTargetPosition(500);
    m.setMode(DcMotor.RunMode.RUN_TO_POSITION);
    assertEquals("RUN_TO_POSITION", cmd("1:0").mode);
    assertEquals(500, cmd("1:0").targetPosition);
  }

  @Test
  void stopAndResetEncoderResetsAndIgnoresPower() {
    DcMotor m = hw.get(DcMotor.class, "intake");
    m.setPower(0.5);
    m.setMode(DcMotor.RunMode.STOP_AND_RESET_ENCODER);
    assertEquals(1, cmd("1:0").resetCount);
    assertEquals(0.0, cmd("1:0").power);
    m.setPower(0.8);
    assertEquals(0.0, cmd("1:0").power);
  }

  @Test
  void velocityAndPidfGoToTheHub() {
    DcMotorEx launcher = hw.get(DcMotorEx.class, "launcher");
    launcher.setMode(DcMotor.RunMode.RUN_USING_ENCODER);
    launcher.setPIDFCoefficients(DcMotor.RunMode.RUN_USING_ENCODER, new PIDFCoefficients(40, 0, 0, 12.5));
    launcher.setVelocity(1250);
    DeviceBus.MotorCommand c = cmd("1:1");
    assertTrue(c.velocitySet);
    assertEquals(1250, c.velocity);
    assertEquals(40, c.velocityPidf[0]);
    assertEquals(12.5, c.velocityPidf[3]);
    assertEquals(40, launcher.getPIDFCoefficients(DcMotor.RunMode.RUN_USING_ENCODER).p);
    assertThrows(UnsupportedOperationException.class,
        () -> launcher.setPIDFCoefficients(DcMotor.RunMode.RUN_WITHOUT_ENCODER, new PIDFCoefficients(1, 0, 0, 0)));
  }

  @Test
  void unchangedWritesCostNoTimeAndChangedOnesCostOneHubCommand() {
    DcMotor cm = hw.get(DcMotor.class, "left_front_drive"); // Control Hub: 2 ms per command
    DcMotor em = hw.get(DcMotor.class, "intake"); // Expansion Hub: 3 ms
    long t0 = runtime.nowNanos();
    cm.setPower(0.3);
    cm.setPower(0.3);
    em.setPower(0.3);
    assertEquals(5_000_000L, runtime.nowNanos() - t0);
  }

  @Test
  void bulkCachingOffReadsEveryTime() {
    DcMotor m = hw.get(DcMotor.class, "left_front_drive");
    long t0 = runtime.nowNanos();
    m.getCurrentPosition();
    m.getCurrentPosition();
    assertEquals(4_000_000L, runtime.nowNanos() - t0);
  }

  @Test
  void bulkCachingAutoRefreshesWhenAValueIsReadTwice() {
    LynxModule controlHub = hw.getAll(LynxModule.class).get(0);
    controlHub.setBulkCachingMode(LynxModule.BulkCachingMode.AUTO);
    DcMotor lf = hw.get(DcMotor.class, "left_front_drive");
    DcMotor rf = hw.get(DcMotor.class, "right_front_drive");
    runtime.bus().motorInput("0:0").position = 10;
    long t0 = runtime.nowNanos();
    assertEquals(10, lf.getCurrentPosition()); // bulk read
    rf.getCurrentPosition(); // cached
    runtime.bus().motorInput("0:0").position = 20;
    assertEquals(20, lf.getCurrentPosition()); // read twice -> new bulk read
    assertEquals(4_000_000L, runtime.nowNanos() - t0);
  }

  @Test
  void bulkCachingManualReturnsStaleDataUntilCleared() {
    LynxModule controlHub = hw.getAll(LynxModule.class).get(0);
    controlHub.setBulkCachingMode(LynxModule.BulkCachingMode.MANUAL);
    DcMotor lf = hw.get(DcMotor.class, "left_front_drive");
    runtime.bus().motorInput("0:0").position = 10;
    assertEquals(10, lf.getCurrentPosition());
    runtime.bus().motorInput("0:0").position = 99;
    assertEquals(10, lf.getCurrentPosition()); // stale: user forgot clearBulkCache()
    controlHub.clearBulkCache();
    assertEquals(99, lf.getCurrentPosition());
  }

  @Test
  void servoPositionMapsToThePwmRangeWithDirectionAndScaleRange() {
    Servo arm = hw.get(Servo.class, "arm");
    arm.setPosition(0.5);
    assertEquals(1500, runtime.bus().servoCommands().get("0:3").pulseUs, 1e-9);
    arm.setPosition(1.4);
    assertEquals(1.0, arm.getPosition());
    assertEquals(2400, runtime.bus().servoCommands().get("0:3").pulseUs, 1e-9);
    arm.setDirection(Servo.Direction.REVERSE);
    arm.scaleRange(0.2, 0.8);
    arm.setPosition(0.0); // reversed -> 1.0 -> scaled to 0.8
    assertEquals(600 + 0.8 * 1800, runtime.bus().servoCommands().get("0:3").pulseUs, 1e-9);
  }

  @Test
  void crServoPowerMapsAroundTheCentrePulse() {
    CRServo s = hw.get(CRServo.class, "windmillServo");
    s.setPower(0);
    assertEquals(1500, runtime.bus().servoCommands().get("0:0").pulseUs, 1e-9);
    s.setDirection(DcMotorSimple.Direction.REVERSE);
    s.setPower(1);
    assertEquals(600, runtime.bus().servoCommands().get("0:0").pulseUs, 1e-9);
  }

  @Test
  void gamepadEdgesAndAliases() {
    Gamepad user = new Gamepad();
    Gamepad ds = new Gamepad();
    ds.cross = true;
    user.copy(ds);
    assertTrue(user.a);
    assertTrue(user.aWasPressed());
    assertFalse(user.aWasPressed()); // reported once
    ds.cross = false;
    user.copy(ds);
    assertTrue(user.crossWasReleased());
    ds.right_trigger = 0.7f;
    user.copy(ds);
    assertTrue(user.right_trigger_pressed);
    assertTrue(user.rightTriggerWasPressed());
  }

  @Test
  void opModeBlackboardIsShared() {
    com.qualcomm.robotcore.eventloop.opmode.OpMode.blackboard.put("k", 1);
    assertEquals(1, com.qualcomm.robotcore.eventloop.opmode.OpMode.blackboard.get("k"));
    com.qualcomm.robotcore.eventloop.opmode.OpMode.blackboard.clear();
    assertEquals(List.of(), List.copyOf(com.qualcomm.robotcore.eventloop.opmode.OpMode.blackboard.keySet()));
  }
}
