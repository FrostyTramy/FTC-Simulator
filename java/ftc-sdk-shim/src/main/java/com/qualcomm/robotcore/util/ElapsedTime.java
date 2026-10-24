package com.qualcomm.robotcore.util;

import java.util.Locale;
import java.util.concurrent.TimeUnit;
import org.firstinspires.ftc.sim.SimRuntime;

/**
 * Measures elapsed time (SDK 12.0 public API). In the simulator the clock is the OpMode's virtual time,
 * not {@code System.nanoTime()}, so timed code behaves the same on every run (determinism).
 */
public class ElapsedTime {
  public static final long SECOND_IN_NANO = 1000000000L;
  public static final long MILLIS_IN_NANO = 1000000L;

  /** Resolution of {@link #time()}. */
  public enum Resolution {
    SECONDS,
    MILLISECONDS
  }

  private long startTime;
  private final double resolution;
  private final Resolution resolutionEnum;

  public ElapsedTime() {
    this(Resolution.SECONDS);
  }

  public ElapsedTime(long startTime) {
    this.startTime = startTime;
    this.resolution = SECOND_IN_NANO;
    this.resolutionEnum = Resolution.SECONDS;
  }

  public ElapsedTime(Resolution resolution) {
    this.resolutionEnum = resolution;
    this.resolution = resolution == Resolution.SECONDS ? SECOND_IN_NANO : MILLIS_IN_NANO;
    reset();
  }

  protected long nsNow() {
    return SimRuntime.get().nowNanos();
  }

  public long now(TimeUnit unit) {
    return unit.convert(nsNow(), TimeUnit.NANOSECONDS);
  }

  public void reset() {
    startTime = nsNow();
  }

  public double startTime() {
    return startTime / resolution;
  }

  public long startTimeNanoseconds() {
    return startTime;
  }

  public double time() {
    return (nsNow() - startTime) / resolution;
  }

  public long time(TimeUnit unit) {
    return unit.convert(nanoseconds(), TimeUnit.NANOSECONDS);
  }

  public double seconds() {
    return nanoseconds() / (double) SECOND_IN_NANO;
  }

  public double milliseconds() {
    return seconds() * 1000;
  }

  public long nanoseconds() {
    return nsNow() - startTime;
  }

  public Resolution getResolution() {
    return resolutionEnum;
  }

  public void log(String label) {
    RobotLog.v(String.format(Locale.US, "TIMER: %20s - %1.3f %s", label, time(), resolutionString()));
  }

  private String resolutionString() {
    return resolutionEnum == Resolution.SECONDS ? "seconds" : "milliseconds";
  }

  @Override
  public String toString() {
    return String.format(Locale.US, "%1.4f %s", time(), resolutionString());
  }
}
