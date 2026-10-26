package org.firstinspires.ftc.teamcode.calibration;

import com.qualcomm.hardware.lynx.LynxModule;
import com.qualcomm.robotcore.eventloop.opmode.LinearOpMode;
import com.qualcomm.robotcore.eventloop.opmode.TeleOp;
import com.qualcomm.robotcore.hardware.DcMotor;
import com.qualcomm.robotcore.hardware.DcMotorEx;
import com.qualcomm.robotcore.hardware.DcMotorSimple;
import com.qualcomm.robotcore.hardware.VoltageSensor;
import com.qualcomm.robotcore.util.ElapsedTime;
import java.io.IOException;
import org.firstinspires.ftc.robotcore.external.navigation.CurrentUnit;

/*
 * Drivetrain calibration (docs/calibration.md, spec 00 benchmarks): full power from rest for RUN_SECONDS, then brake,
 * logging every loop the time, wheel encoders and velocities, motor currents and battery voltage.
 *
 * During INIT choose the test with the dpad: up = FORWARD, left/right = STRAFE (right), down = TURN (clockwise).
 * Needs about 3.5 m of free floor in the test direction for 2 s at full power. Set NAMES and REVERSED like your TeleOp.
 */
@TeleOp(name = "Cal: Drive", group = "Calibration")
public class CalDrive extends LinearOpMode {
  static final String[] NAMES = {"left_front_drive", "right_front_drive", "left_back_drive", "right_back_drive"};
  static final boolean[] REVERSED = {true, false, true, false};
  static final double POWER = 1.0;
  static final double RUN_SECONDS = 2.0;
  static final double BRAKE_SECONDS = 1.0;

  enum Test {
    FORWARD(1, 1, 1, 1),
    STRAFE(1, -1, -1, 1),
    TURN(1, -1, 1, -1);

    final double[] sign;

    Test(double lf, double rf, double lb, double rb) {
      sign = new double[] {lf, rf, lb, rb};
    }
  }

  @Override
  public void runOpMode() {
    DcMotorEx[] motors = new DcMotorEx[NAMES.length];
    for (int i = 0; i < NAMES.length; i++) {
      motors[i] = hardwareMap.get(DcMotorEx.class, NAMES[i]);
      motors[i].setDirection(REVERSED[i] ? DcMotorSimple.Direction.REVERSE : DcMotorSimple.Direction.FORWARD);
      motors[i].setZeroPowerBehavior(DcMotor.ZeroPowerBehavior.BRAKE);
      motors[i].setMode(DcMotor.RunMode.STOP_AND_RESET_ENCODER);
      motors[i].setMode(DcMotor.RunMode.RUN_WITHOUT_ENCODER);
    }
    for (LynxModule hub : hardwareMap.getAll(LynxModule.class)) {
      hub.setBulkCachingMode(LynxModule.BulkCachingMode.AUTO);
    }
    VoltageSensor battery = hardwareMap.voltageSensor.iterator().next();

    Test test = Test.FORWARD;
    while (opModeInInit()) {
      if (gamepad1.dpad_up) {
        test = Test.FORWARD;
      } else if (gamepad1.dpad_left || gamepad1.dpad_right) {
        test = Test.STRAFE;
      } else if (gamepad1.dpad_down) {
        test = Test.TURN;
      }
      telemetry.addData("Test (dpad)", test);
      telemetry.addData("Space needed", test == Test.TURN ? "turns in place" : "about 3.5 m in the test direction");
      telemetry.update();
    }
    if (isStopRequested()) {
      return;
    }

    try (CalibrationLog log = new CalibrationLog("drive_" + test.name().toLowerCase(),
        "t,power,pos_lf,pos_rf,pos_lb,pos_rb,vel_lf,vel_rf,vel_lb,vel_rb,amp_lf,amp_rf,amp_lb,amp_rb,volts")) {
      ElapsedTime timer = new ElapsedTime();
      while (opModeIsActive() && timer.seconds() < RUN_SECONDS + BRAKE_SECONDS) {
        double t = timer.seconds();
        double power = t < RUN_SECONDS ? POWER : 0;
        for (int i = 0; i < motors.length; i++) {
          motors[i].setPower(power * test.sign[i]);
        }
        Object[] row = new Object[15];
        row[0] = t;
        row[1] = power;
        for (int i = 0; i < 4; i++) {
          row[2 + i] = motors[i].getCurrentPosition();
          row[6 + i] = motors[i].getVelocity();
          row[10 + i] = motors[i].getCurrent(CurrentUnit.AMPS);
        }
        row[14] = battery.getVoltage();
        log.row(row);
      }
      for (DcMotorEx m : motors) {
        m.setPower(0);
      }
      telemetry.addData("Saved", log.file().getPath());
      telemetry.update();
    } catch (IOException e) {
      telemetry.addData("Error", e.getMessage());
      telemetry.update();
    }
    while (opModeIsActive()) {
      idle();
    }
  }
}
