package org.firstinspires.ftc.robotcore.external.navigation;

/** Angle units without normalization (angular rates, accumulated angles). */
public enum UnnormalizedAngleUnit {
  DEGREES,
  RADIANS;

  public double fromDegrees(double degrees) {
    return this == DEGREES ? degrees : Math.toRadians(degrees);
  }

  public double fromRadians(double radians) {
    return this == RADIANS ? radians : Math.toDegrees(radians);
  }

  public double toDegrees(double inOurUnits) {
    return this == DEGREES ? inOurUnits : Math.toDegrees(inOurUnits);
  }

  public double toRadians(double inOurUnits) {
    return this == RADIANS ? inOurUnits : Math.toRadians(inOurUnits);
  }

  public double fromUnit(UnnormalizedAngleUnit them, double theirs) {
    return them == DEGREES ? fromDegrees(theirs) : fromRadians(theirs);
  }

  public AngleUnit getNormalized() {
    return this == DEGREES ? AngleUnit.DEGREES : AngleUnit.RADIANS;
  }
}
