package org.firstinspires.ftc.robotcore.internal.system;

import java.util.concurrent.TimeUnit;
import org.firstinspires.ftc.sim.SimRuntime;

/** A deadline in OpMode time (SDK 12.0 API, subset). */
public class Deadline {
  private final long durationNanos;
  private long startNanos;

  public Deadline(long duration, TimeUnit unit) {
    durationNanos = unit.toNanos(duration);
    reset();
  }

  public void reset() {
    startNanos = SimRuntime.get().nowNanos();
  }

  public boolean hasExpired() {
    return SimRuntime.get().nowNanos() - startNanos >= durationNanos;
  }

  public long timeRemaining(TimeUnit unit) {
    return unit.convert(Math.max(0, startNanos + durationNanos - SimRuntime.get().nowNanos()), TimeUnit.NANOSECONDS);
  }
}
