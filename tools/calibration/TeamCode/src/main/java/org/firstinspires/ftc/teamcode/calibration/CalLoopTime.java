package org.firstinspires.ftc.teamcode.calibration;

import com.qualcomm.hardware.lynx.LynxModule;
import com.qualcomm.robotcore.eventloop.opmode.LinearOpMode;
import com.qualcomm.robotcore.eventloop.opmode.TeleOp;
import com.qualcomm.robotcore.hardware.DcMotorEx;
import com.qualcomm.robotcore.util.ElapsedTime;
import java.io.IOException;
import java.util.ArrayList;
import java.util.Collections;
import java.util.List;
import java.util.Locale;

/*
 * Loop time calibration (docs/calibration.md, spec 00 "loop times in the same range as the real Control Hub"): for
 * PHASE_SECONDS each, reads the position and velocity of every motor with bulk caching OFF, AUTO and MANUAL
 * (clearBulkCache once per loop) and logs every loop's duration. Nothing moves.
 */
@TeleOp(name = "Cal: Loop time", group = "Calibration")
public class CalLoopTime extends LinearOpMode {
  static final double PHASE_SECONDS = 3.0;

  @Override
  public void runOpMode() {
    List<DcMotorEx> motors = hardwareMap.getAll(DcMotorEx.class);
    List<LynxModule> hubs = hardwareMap.getAll(LynxModule.class);
    telemetry.addData("Motors", motors.size());
    telemetry.update();
    waitForStart();

    StringBuilder summary = new StringBuilder();
    try (CalibrationLog log = new CalibrationLog("loop_time", "mode,t,loop_ms")) {
      for (LynxModule.BulkCachingMode mode : new LynxModule.BulkCachingMode[] {
          LynxModule.BulkCachingMode.OFF, LynxModule.BulkCachingMode.AUTO, LynxModule.BulkCachingMode.MANUAL}) {
        for (LynxModule hub : hubs) {
          hub.setBulkCachingMode(mode);
        }
        List<Double> loops = new ArrayList<>();
        ElapsedTime phase = new ElapsedTime();
        ElapsedTime loop = new ElapsedTime();
        while (opModeIsActive() && phase.seconds() < PHASE_SECONDS) {
          if (mode == LynxModule.BulkCachingMode.MANUAL) {
            for (LynxModule hub : hubs) {
              hub.clearBulkCache();
            }
          }
          for (DcMotorEx m : motors) {
            m.getCurrentPosition();
            m.getVelocity();
          }
          double ms = loop.milliseconds();
          loop.reset();
          loops.add(ms);
          log.row(mode, phase.seconds(), ms);
        }
        Collections.sort(loops);
        double mean = loops.stream().mapToDouble(Double::doubleValue).average().orElse(0);
        double p95 = loops.isEmpty() ? 0 : loops.get((int) Math.floor(0.95 * (loops.size() - 1)));
        summary.append(String.format(Locale.US, "%s: mean %.2f ms, p95 %.2f ms (%d loops)\n", mode, mean, p95, loops.size()));
      }
      telemetry.addData("Saved", log.file().getPath());
    } catch (IOException e) {
      telemetry.addData("Error", e.getMessage());
    }
    telemetry.addLine(summary.toString());
    telemetry.update();
    while (opModeIsActive()) {
      idle();
    }
  }
}
