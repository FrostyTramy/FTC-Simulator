package com.qualcomm.robotcore.eventloop.opmode;

import org.firstinspires.ftc.sim.SimRuntime;

/**
 * Sequential OpMode (SDK 12.0 public API): runOpMode() runs on its own thread. waitForStart() blocks
 * until START (or STOP); opModeIsActive() is true between START and STOP and calls idle(); a STOP
 * interrupts the thread, which must then return within 900 ms or the Robot Controller restarts.
 */
public abstract class LinearOpMode extends OpMode {
  private volatile boolean started;

  public LinearOpMode() {}

  public abstract void runOpMode() throws InterruptedException;

  /** Pauses until START is pressed or a stop is requested. */
  public void waitForStart() {
    while (!isStarted() && !isStopRequested()) {
      idle();
    }
  }

  /**
   * Gives other threads a chance to run (SDK: Thread.yield). In the simulator it also lets a little
   * virtual time pass so that busy-wait loops make progress.
   */
  public final void idle() {
    SimRuntime runtime = SimRuntime.get();
    runtime.consume(runtime.costs().trivialCallNanos);
    Thread.yield();
  }

  /** Sleeps for the given number of milliseconds (returns early with the interrupt flag set on STOP). */
  public final void sleep(long milliseconds) {
    try {
      SimRuntime runtime = SimRuntime.get();
      runtime.sleepUntil(runtime.nowNanos() + milliseconds * 1_000_000L);
    } catch (InterruptedException e) {
      Thread.currentThread().interrupt();
    }
  }

  /** True between START and STOP. Calls idle() when active, like the SDK. */
  public final boolean opModeIsActive() {
    boolean active = !isStopRequested() && isStarted();
    if (active) {
      idle();
    }
    return active;
  }

  /** True after INIT and before START or STOP. */
  public final boolean opModeInInit() {
    return !isStarted() && !isStopRequested();
  }

  public final boolean isStarted() {
    return started;
  }

  public final boolean isStopRequested() {
    return stopRequested || Thread.currentThread().isInterrupted();
  }

  // The iterative entry points are final in the SDK: a LinearOpMode only implements runOpMode().
  @Override
  public final void init() {}

  @Override
  public final void init_loop() {}

  @Override
  public final void start() {}

  @Override
  public final void loop() {}

  @Override
  public final void stop() {}

  /** Simulator hook: START pressed. */
  public final void internalSimStart() {
    started = true;
  }
}
