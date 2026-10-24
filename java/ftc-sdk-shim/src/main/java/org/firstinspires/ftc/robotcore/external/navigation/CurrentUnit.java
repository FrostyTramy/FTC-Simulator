package org.firstinspires.ftc.robotcore.external.navigation;

/** Electric current units (SDK 12.0 public API). */
public enum CurrentUnit {
  AMPS,
  MILLIAMPS;

  public double toAmps(double current) {
    return this == AMPS ? current : current / 1000.0;
  }

  public double toMilliAmps(double current) {
    return this == MILLIAMPS ? current : current * 1000.0;
  }

  /** Converts {@code current}, given in {@code unit}, into this unit. */
  public double convert(double current, CurrentUnit unit) {
    double amps = unit.toAmps(current);
    return this == AMPS ? amps : amps * 1000.0;
  }
}
