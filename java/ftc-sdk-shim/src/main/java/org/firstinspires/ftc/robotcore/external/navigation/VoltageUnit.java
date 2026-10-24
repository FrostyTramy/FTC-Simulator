package org.firstinspires.ftc.robotcore.external.navigation;

/** Voltage units (SDK 12.0 public API). */
public enum VoltageUnit {
  VOLTS,
  MILLIVOLTS;

  public double toVolts(double voltage) {
    return this == VOLTS ? voltage : voltage / 1000.0;
  }

  public double toMilliVolts(double voltage) {
    return this == MILLIVOLTS ? voltage : voltage * 1000.0;
  }

  /** Converts {@code voltage}, given in {@code unit}, into this unit. */
  public double convert(double voltage, VoltageUnit unit) {
    double volts = unit.toVolts(voltage);
    return this == VOLTS ? volts : volts * 1000.0;
  }
}
