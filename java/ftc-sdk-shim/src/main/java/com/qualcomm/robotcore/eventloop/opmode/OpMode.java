package com.qualcomm.robotcore.eventloop.opmode;

import java.util.HashMap;
import org.firstinspires.ftc.robotcore.external.Telemetry;
import org.firstinspires.ftc.sim.SimRuntime;

/**
 * Iterative OpMode (SDK 12.0 public API): init() once, init_loop() repeatedly until START, start() once,
 * loop() repeatedly until STOP, then stop(). The simulator's OpMode driver calls these with the same
 * telemetry auto-update and gamepad-copy behaviour as the SDK event loop.
 */
public abstract class OpMode extends OpModeInternal {
  /** Data shared between OpModes of one Robot Controller session (e.g. AUTO → TELEOP). */
  public static final HashMap<String, Object> blackboard = new HashMap<>();

  /** Seconds since the OpMode was initialized (updated before each loop). */
  public volatile double time = 0.0;

  public int msStuckDetectInit = 5000;
  public int msStuckDetectInitLoop = 5000;
  public int msStuckDetectStart = 5000;
  public int msStuckDetectLoop = 5000;

  private long startTimeNanos;

  public OpMode() {
    startTimeNanos = SimRuntime.get().nowNanos();
  }

  public abstract void init();

  public void init_loop() {}

  public void start() {}

  public abstract void loop();

  public void stop() {}

  /** Stops the OpMode immediately from inside user code (SDK 9.1+): unwinds via an exception. */
  public final void terminateOpModeNow() {
    throw new ForceStopException();
  }

  /** Seconds since {@link #resetRuntime()} or the start of the OpMode. */
  public double getRuntime() {
    return (SimRuntime.get().nowNanos() - startTimeNanos) / 1e9;
  }

  public void resetRuntime() {
    startTimeNanos = SimRuntime.get().nowNanos();
  }

  /** Transmits telemetry (subject to its transmission interval). */
  public void updateTelemetry(Telemetry telemetry) {
    telemetry.update();
  }

  /** Called by the event loop before init(). */
  public void internalPreInit() {
    startTimeNanos = SimRuntime.get().nowNanos();
    time = 0;
  }

  /** Called by the event loop after each init_loop(): sends telemetry like the SDK. */
  public void internalPostInitLoop() {
    if (telemetry != null) {
      telemetry.update();
    }
  }

  /** Called by the event loop after each loop(): sends telemetry like the SDK. */
  public void internalPostLoop() {
    if (telemetry != null) {
      telemetry.update();
    }
  }

  /** Simulator hook: refreshes {@link #time} before init_loop()/loop(). */
  public final void internalSimUpdateTime() {
    time = getRuntime();
  }

  /** Simulator hook: true once STOP was requested by the DS, the field or requestOpModeStop(). */
  public final boolean internalSimIsStopRequested() {
    return stopRequested;
  }

  /** Simulator hook: STOP pressed on the Driver Station (or the match ended). */
  public void internalSimRequestStop() {
    stopRequested = true;
  }

  /** Thrown by {@link #terminateOpModeNow()}; the driver treats it as a normal stop. */
  public static final class ForceStopException extends RuntimeException {
    public ForceStopException() {
      super("OpMode terminated by terminateOpModeNow()");
    }
  }
}
