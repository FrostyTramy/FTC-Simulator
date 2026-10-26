package org.firstinspires.ftc.sim;

import com.qualcomm.robotcore.eventloop.opmode.Autonomous;
import com.qualcomm.robotcore.eventloop.opmode.LinearOpMode;
import com.qualcomm.robotcore.eventloop.opmode.OpMode;
import com.qualcomm.robotcore.eventloop.opmode.TeleOp;
import com.qualcomm.robotcore.hardware.Gamepad;
import com.qualcomm.robotcore.hardware.HardwareMap;

/**
 * Runs one OpMode like the Robot Controller's event loop (spec 06 "OpMode lifecycle"):
 *
 * <ul>
 *   <li>Iterative: init(), then init_loop() until START, start(), loop() until STOP, then stop(). Fresh
 *       gamepad data is copied in before each init_loop()/loop() and telemetry is sent after each call.
 *   <li>Linear: runOpMode() on the OpMode thread; START releases waitForStart(); STOP sets the stop
 *       flag and interrupts the thread, which must end within 900 ms (virtual time) or the RC "restarts".
 *   <li>Uncaught exceptions stop the OpMode with the DS message "User code threw an uncaught exception".
 * </ul>
 *
 * <p>Everything runs on the OpMode thread under {@link SimRuntime}, so timing is virtual and
 * deterministic.
 */
public final class OpModeDriver {
  public enum State {
    INIT,
    RUNNING,
    STOPPING,
    STOPPED,
    CRASHED
  }

  private final SimRuntime runtime;
  private final Class<? extends OpMode> opModeClass;
  private final HardwareMap hardwareMap;
  private final SimTelemetry telemetry;
  private final Gamepad latest1 = new Gamepad();
  private final Gamepad latest2 = new Gamepad();
  private final boolean linear;
  private volatile OpMode opMode;
  private volatile boolean startRequested;
  private volatile boolean stopRequested;
  private volatile State state = State.INIT;
  private volatile String error;
  private long stopRequestedAtNanos = -1;
  private Thread thread;

  public OpModeDriver(SimRuntime runtime, Class<? extends OpMode> opModeClass, HardwareMap hardwareMap, SimTelemetry telemetry) {
    this.runtime = runtime;
    this.opModeClass = opModeClass;
    this.hardwareMap = hardwareMap;
    this.telemetry = telemetry;
    this.linear = LinearOpMode.class.isAssignableFrom(opModeClass);
  }

  public State state() {
    return state;
  }

  /** The error shown on the Driver Station, or null. */
  public String error() {
    return error;
  }

  public boolean isLinear() {
    return linear;
  }

  /** INIT pressed: constructs the OpMode and starts its thread (user code runs on that thread only). */
  public void init() {
    thread = new Thread(this::run, "OpMode " + opModeClass.getSimpleName());
    thread.setDaemon(true);
    runtime.attachOpModeThread(thread);
    thread.start();
  }

  /** START pressed. */
  public void start() {
    startRequested = true;
    OpMode op = opMode;
    if (op instanceof LinearOpMode linearOp) {
      linearOp.internalSimStart();
    }
    if (state == State.INIT) {
      state = State.RUNNING;
    }
  }

  /** STOP pressed (or the field disabled the robot). */
  public void stop() {
    if (stopRequested) {
      return;
    }
    stopRequested = true;
    stopRequestedAtNanos = runtime.nowNanos();
    if (state == State.INIT || state == State.RUNNING) {
      state = State.STOPPING;
    }
    OpMode op = opMode;
    if (op != null) {
      op.internalSimRequestStop();
    }
    if (linear) {
      runtime.requestInterrupt();
    }
  }

  /** New Driver Station gamepad data (called by the runner between ticks, while the OpMode is parked). */
  public void setGamepads(Gamepad g1, Gamepad g2) {
    latest1.copy(g1);
    latest2.copy(g2);
    OpMode op = opMode;
    if (linear && op != null) {
      op.gamepad1.copy(latest1);
      op.gamepad2.copy(latest2);
    }
  }

  /**
   * Lets the OpMode run until virtual time {@code endNanos}. Returns false if user code kept running
   * without any SDK call for {@code wallTimeoutMs} of real time (stuck).
   */
  public boolean runUntil(long endNanos, long wallTimeoutMs) throws InterruptedException {
    if (state == State.STOPPED || state == State.CRASHED) {
      runtime.advanceIdle(endNanos);
      return true;
    }
    boolean ok = runtime.runUntil(endNanos, wallTimeoutMs);
    if (!ok) {
      fail("The OpMode is stuck: user code ran for more than " + wallTimeoutMs + " ms without calling the SDK.");
      return false;
    }
    if (stopRequested && thread.isAlive()
        && runtime.nowNanos() - stopRequestedAtNanos > OpMode.MS_BEFORE_FORCE_STOP_AFTER_STOP_REQUESTED * 1_000_000L) {
      fail("User code did not stop within 900 ms after STOP was requested (stuck in stop()). The Robot Controller restarts.");
    }
    return true;
  }

  /**
   * One Robot Controller event loop iteration, run by the runner between ticks while the OpMode thread is parked: a
   * LinearOpMode's throttled telemetry goes out once the interval allows (SDK LinearOpMode.internalOnEventLoopIteration
   * calls TelemetryImpl.tryUpdateIfDirty). Iterative OpModes send telemetry after every user method instead.
   */
  public void eventLoopIteration() {
    if (!linear || (state != State.INIT && state != State.RUNNING && state != State.STOPPING)) {
      return;
    }
    try {
      telemetry.tryUpdateIfDirty();
    } catch (RuntimeException e) { // a telemetry action or value producer threw
      String message = e.getMessage();
      fail("User code threw an uncaught exception: " + e.getClass().getSimpleName() + (message != null ? " - " + message : ""));
    }
  }

  private void fail(String message) {
    if (error == null) {
      error = message;
    }
    state = State.CRASHED;
    if (thread != null) {
      thread.interrupt();
    }
  }

  private void run() {
    try {
      runtime.consume(0); // wait for the runner's first tick: no user code runs outside a tick window
      OpMode op = opModeClass.getDeclaredConstructor().newInstance();
      op.hardwareMap = hardwareMap;
      op.telemetry = telemetry;
      opMode = op;
      if (startRequested && op instanceof LinearOpMode linearOp) {
        linearOp.internalSimStart();
      }
      com.qualcomm.robotcore.eventloop.opmode.OpModeManagerImpl.simPreInit(op, displayName());
      op.internalPreInit();
      if (op instanceof LinearOpMode linearOp) {
        linearOp.runOpMode();
      } else {
        runIterative(op);
      }
      finish(null);
    } catch (OpMode.ForceStopException e) {
      finish(null);
    } catch (InterruptedException e) {
      finish(null); // a LinearOpMode leaving runOpMode via InterruptedException after STOP is normal
    } catch (java.lang.reflect.InvocationTargetException e) {
      finish(e.getCause());
    } catch (ReflectiveOperationException e) {
      finish(new IllegalStateException("cannot create OpMode " + opModeClass.getName() + ": " + e));
    } catch (Throwable t) {
      finish(t);
    }
  }

  private void runIterative(OpMode op) {
    long overhead = runtime.costs().loopOverheadNanos;
    op.init();
    op.internalPostInitLoop();
    runtime.consume(overhead);
    while (!startRequested && !isStopping(op)) {
      copyGamepads(op);
      op.internalSimUpdateTime();
      op.init_loop();
      op.internalPostInitLoop();
      runtime.consume(overhead);
    }
    if (startRequested && !isStopping(op)) {
      op.internalSimUpdateTime();
      com.qualcomm.robotcore.eventloop.opmode.OpModeManagerImpl.simPreStart(op);
      op.start();
      op.internalPostLoop(); // SDK internalPostUserCode: telemetry after every user call
      runtime.consume(overhead);
      while (!isStopping(op)) {
        copyGamepads(op);
        op.internalSimUpdateTime();
        op.loop();
        op.internalPostLoop();
        runtime.consume(overhead);
      }
    }
    op.stop();
    op.internalPostLoop();
  }

  private boolean isStopping(OpMode op) {
    return stopRequested || op.internalSimIsStopRequested();
  }

  private void copyGamepads(OpMode op) {
    op.gamepad1.copy(latest1);
    op.gamepad2.copy(latest2);
  }

  /** The name the Driver Station lists (the @TeleOp / @Autonomous name, else the class name). */
  private String displayName() {
    TeleOp teleOp = opModeClass.getAnnotation(TeleOp.class);
    if (teleOp != null && !teleOp.name().isEmpty()) {
      return teleOp.name();
    }
    Autonomous auto = opModeClass.getAnnotation(Autonomous.class);
    return auto != null && !auto.name().isEmpty() ? auto.name() : opModeClass.getSimpleName();
  }

  private void finish(Throwable t) {
    OpMode op = opMode;
    if (op != null) {
      com.qualcomm.robotcore.eventloop.opmode.OpModeManagerImpl.simPostStop(op);
    }
    if (t != null && error == null) {
      String message = t.getMessage();
      error = "User code threw an uncaught exception: " + t.getClass().getSimpleName() + (message != null ? " - " + message : "");
      StackTraceElement[] trace = t.getStackTrace();
      if (trace.length > 0) {
        runtime.bus().log("E " + error + " at " + trace[0]);
      }
    }
    state = error != null ? State.CRASHED : State.STOPPED;
  }
}
