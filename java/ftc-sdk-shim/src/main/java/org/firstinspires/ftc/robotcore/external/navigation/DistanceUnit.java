package org.firstinspires.ftc.robotcore.external.navigation;

import java.util.Locale;

/** Distance units (SDK 12.0 public API); {@link #infinity} marks "no reading". */
public enum DistanceUnit {
  METER(0),
  CM(1),
  MM(2),
  INCH(3);

  public final byte bVal;
  public static final double infinity = Double.MAX_VALUE;
  public static final double mmPerInch = 25.4;
  public static final double mPerInch = mmPerInch * 0.001;

  DistanceUnit(int b) {
    this.bVal = (byte) b;
  }

  private double metersPerUnit() {
    switch (this) {
      case CM:
        return 0.01;
      case MM:
        return 0.001;
      case INCH:
        return mPerInch;
      default:
        return 1.0;
    }
  }

  private double fromMetersRaw(double meters) {
    return meters == infinity ? infinity : meters / metersPerUnit();
  }

  public double fromMeters(double meters) {
    return fromMetersRaw(meters);
  }

  public double fromInches(double inches) {
    return inches == infinity ? infinity : fromMetersRaw(inches * mPerInch);
  }

  public double fromCm(double cm) {
    return cm == infinity ? infinity : fromMetersRaw(cm * 0.01);
  }

  public double fromMm(double mm) {
    return mm == infinity ? infinity : fromMetersRaw(mm * 0.001);
  }

  public double fromUnit(DistanceUnit him, double his) {
    return his == infinity ? infinity : fromMetersRaw(his * him.metersPerUnit());
  }

  public double toMeters(double inOurUnits) {
    return inOurUnits == infinity ? infinity : inOurUnits * metersPerUnit();
  }

  public double toInches(double inOurUnits) {
    return inOurUnits == infinity ? infinity : toMeters(inOurUnits) / mPerInch;
  }

  public double toCm(double inOurUnits) {
    return inOurUnits == infinity ? infinity : toMeters(inOurUnits) * 100;
  }

  public double toMm(double inOurUnits) {
    return inOurUnits == infinity ? infinity : toMeters(inOurUnits) * 1000;
  }

  public String toString(double inOurUnits) {
    return String.format(Locale.US, "%.3f%s", inOurUnits, toString());
  }

  @Override
  public String toString() {
    switch (this) {
      case CM:
        return "cm";
      case MM:
        return "mm";
      case INCH:
        return "in";
      default:
        return "m";
    }
  }
}
