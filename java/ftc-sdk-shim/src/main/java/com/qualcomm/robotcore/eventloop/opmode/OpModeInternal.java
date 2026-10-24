package com.qualcomm.robotcore.eventloop.opmode;

import com.qualcomm.robotcore.hardware.Gamepad;
import com.qualcomm.robotcore.hardware.HardwareMap;
import org.firstinspires.ftc.robotcore.external.Telemetry;

/** Fields shared by all OpModes (SDK 12.0: package-private base class of {@link OpMode}). */
abstract class OpModeInternal {
  /** Time the SDK waits for an OpMode to stop before restarting the Robot Controller. */
  public static final int MS_BEFORE_FORCE_STOP_AFTER_STOP_REQUESTED = 900;

  public volatile Gamepad gamepad1 = new Gamepad();
  public volatile Gamepad gamepad2 = new Gamepad();
  public Telemetry telemetry;
  public volatile HardwareMap hardwareMap;
  public int msStuckDetectStop = 900;

  volatile boolean stopRequested;

  /** Asks the Robot Controller to stop this OpMode (as if STOP was pressed on the DS). */
  public final void requestOpModeStop() {
    stopRequested = true;
  }
}
