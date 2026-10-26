package org.firstinspires.ftc.teamcode.calibration;

import com.qualcomm.hardware.lynx.LynxModule;
import com.qualcomm.robotcore.eventloop.opmode.LinearOpMode;
import com.qualcomm.robotcore.eventloop.opmode.TeleOp;
import com.qualcomm.robotcore.hardware.DcMotor;
import com.qualcomm.robotcore.hardware.DcMotorEx;
import com.qualcomm.robotcore.hardware.VoltageSensor;
import com.qualcomm.robotcore.util.ElapsedTime;
import java.io.IOException;
import org.firstinspires.ftc.robotcore.external.navigation.CurrentUnit;

/*
 * Launcher calibration (docs/calibration.md): spin the flywheel up at full power for SPIN_SECONDS, then let it coast for
 * COAST_SECONDS, logging velocity, current and battery voltage. While it spins at speed, feed one element by hand (or
 * with your feeder) to log the speed drop of a shot. The flywheel motor is the first of NAMES that exists.
 */
@TeleOp(name = "Cal: Launcher", group = "Calibration")
public class CalLauncher extends LinearOpMode {
  static final String[] NAMES = {"launcher", "shooter", "flywheel"};
  static final double SPIN_SECONDS = 3.0;
  static final double COAST_SECONDS = 3.0;

  @Override
  public void runOpMode() {
    DcMotorEx launcher = null;
    for (String name : NAMES) {
      launcher = hardwareMap.tryGet(DcMotorEx.class, name);
      if (launcher != null) {
        break;
      }
    }
    if (launcher == null) {
      telemetry.addData("Error", "no launcher motor named %s", String.join(" / ", NAMES));
      telemetry.update();
      waitForStart();
      return;
    }
    launcher.setZeroPowerBehavior(DcMotor.ZeroPowerBehavior.FLOAT);
    launcher.setMode(DcMotor.RunMode.STOP_AND_RESET_ENCODER);
    launcher.setMode(DcMotor.RunMode.RUN_WITHOUT_ENCODER);
    for (LynxModule hub : hardwareMap.getAll(LynxModule.class)) {
      hub.setBulkCachingMode(LynxModule.BulkCachingMode.AUTO);
    }
    VoltageSensor battery = hardwareMap.voltageSensor.iterator().next();
    telemetry.addData("Ready", "full power for %.0f s, then coast %.0f s", SPIN_SECONDS, COAST_SECONDS);
    telemetry.update();
    waitForStart();

    try (CalibrationLog log = new CalibrationLog("launcher", "t,power,velocity,amps,volts")) {
      ElapsedTime timer = new ElapsedTime();
      while (opModeIsActive() && timer.seconds() < SPIN_SECONDS + COAST_SECONDS) {
        double t = timer.seconds();
        double power = t < SPIN_SECONDS ? 1.0 : 0.0;
        launcher.setPower(power);
        log.row(t, power, launcher.getVelocity(), launcher.getCurrent(CurrentUnit.AMPS), battery.getVoltage());
      }
      launcher.setPower(0);
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
