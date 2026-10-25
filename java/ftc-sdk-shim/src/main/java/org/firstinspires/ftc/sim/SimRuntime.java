package org.firstinspires.ftc.sim;

/**
 * Simulator side of the SDK shim: the virtual OpMode clock and the cooperative scheduler that keeps
 * user code in lockstep with the simulation (spec 01 "tick loop", spec 06 "Timing").
 *
 * <p>User code runs on its own thread. Every SDK call that takes time on a real robot (hardware
 * transactions, loop overhead, sleep) calls {@link #consume(long)}; when the virtual time reaches the end
 * of the current simulation tick the OpMode thread parks until the runner grants the next tick. Results
 * therefore depend only on the inputs, never on the wall clock (determinism).
 */
public final class SimRuntime {
  private static volatile SimRuntime current = new SimRuntime(HardwareCosts.defaults());

  private final Object lock = new Object();
  private final HardwareCosts costs;
  private final DeviceBus bus = new DeviceBus();
  private long nowNanos;
  private long budgetEndNanos = Long.MAX_VALUE; // unlimited until a runner takes control
  private boolean parked;
  private boolean controlled;
  private boolean interruptPending;
  private Thread opModeThread;

  public SimRuntime(HardwareCosts costs) {
    this.costs = costs;
  }

  /** The runtime used by SDK classes created from now on (one OpMode per JVM). */
  public static SimRuntime get() {
    return current;
  }

  public static void install(SimRuntime runtime) {
    current = runtime;
  }

  public HardwareCosts costs() {
    return costs;
  }

  public DeviceBus bus() {
    return bus;
  }

  /** Virtual time of the OpMode in nanoseconds (sim time at the start of the match = 0). */
  public long nowNanos() {
    synchronized (lock) {
      return nowNanos;
    }
  }

  /**
   * Marks the OpMode thread (only that thread is ever parked). It gets no time until the runner's first
   * {@link #runUntil}, so user code never runs while the runner is between ticks.
   */
  public void attachOpModeThread(Thread thread) {
    synchronized (lock) {
      opModeThread = thread;
      controlled = true;
      budgetEndNanos = nowNanos;
    }
  }

  /**
   * Advances the virtual clock by the cost of an operation. Called on the OpMode thread; parks it at
   * the end of the current tick and resumes it when the runner grants more time.
   */
  public void consume(long nanos) {
    if (nanos < 0) {
      throw new IllegalArgumentException("negative duration");
    }
    synchronized (lock) {
      if (controlled && Thread.currentThread() != opModeThread) {
        return; // other threads (the runner flushing telemetry, team threads) do not move the OpMode's clock
      }
      nowNanos += nanos;
      parkWhileOverBudget();
    }
  }

  /** Lets virtual time pass until {@code targetNanos} (LinearOpMode.sleep, waits). */
  public void sleepUntil(long targetNanos) throws InterruptedException {
    synchronized (lock) {
      while (nowNanos < targetNanos) {
        if (Thread.currentThread().isInterrupted()) {
          throw new InterruptedException();
        }
        if (controlled && Thread.currentThread() == opModeThread && budgetEndNanos <= targetNanos) {
          nowNanos = Math.max(nowNanos, budgetEndNanos);
          parkWhileOverBudget();
          if (Thread.interrupted()) {
            throw new InterruptedException();
          }
        } else {
          nowNanos = targetNanos;
        }
      }
    }
  }

  /**
   * Parks the OpMode thread until the runner grants time beyond {@code nowNanos}. Interrupts never cut
   * a park short (the OpMode only runs inside the runner's time window, which keeps it deterministic);
   * they and stop requests ({@link #requestInterrupt}) are delivered as the interrupt flag on resume.
   */
  private void parkWhileOverBudget() {
    if (!controlled || Thread.currentThread() != opModeThread) {
      return;
    }
    boolean interrupted = false;
    while (nowNanos >= budgetEndNanos) {
      if (Thread.interrupted()) {
        interrupted = true;
      }
      parked = true;
      lock.notifyAll();
      try {
        lock.wait();
      } catch (InterruptedException e) {
        interrupted = true;
      }
    }
    parked = false;
    if (interrupted || interruptPending) {
      interruptPending = false;
      Thread.currentThread().interrupt();
    }
  }

  // ------------------------------------------------------------------ runner side

  /**
   * Runner: lets the OpMode thread run until the virtual time reaches {@code endNanos}, the thread
   * finishes, or {@code wallTimeoutMs} of real time pass (user code spinning without SDK calls).
   *
   * @return true if the OpMode thread parked or finished in time, false on a wall-clock timeout
   */
  public boolean runUntil(long endNanos, long wallTimeoutMs) throws InterruptedException {
    synchronized (lock) {
      budgetEndNanos = endNanos;
      parked = false;
      lock.notifyAll();
      long deadline = System.currentTimeMillis() + wallTimeoutMs;
      while (!parked && opModeThread != null && opModeThread.isAlive()) {
        long left = deadline - System.currentTimeMillis();
        if (left <= 0) {
          return false;
        }
        lock.wait(Math.min(left, 50));
      }
      if (nowNanos < endNanos && (opModeThread == null || !opModeThread.isAlive())) {
        nowNanos = endNanos; // nothing runs: the clock simply follows the simulation
      }
      return true;
    }
  }

  /** Runner: moves the clock without an OpMode thread (before INIT, after the OpMode ended). */
  public void advanceIdle(long toNanos) {
    synchronized (lock) {
      nowNanos = Math.max(nowNanos, toNanos);
    }
  }

  /**
   * Runner: interrupts the OpMode thread when it next resumes (LinearOpMode STOP). Called between ticks
   * while the thread is parked, so the interrupt lands at the same virtual time in every run.
   */
  public void requestInterrupt() {
    synchronized (lock) {
      interruptPending = true;
    }
  }
}
