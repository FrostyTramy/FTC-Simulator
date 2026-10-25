package org.firstinspires.ftc.robotcore.external.navigation;

import java.util.Locale;

/** A planar pose with units (SDK 12.0 public API). */
public class Pose2D {
  protected final double x;
  protected final double y;
  protected final DistanceUnit distanceUnit;
  protected final double heading;
  protected final AngleUnit headingUnit;

  public Pose2D(DistanceUnit distanceUnit, double x, double y, AngleUnit headingUnit, double heading) {
    this.x = x;
    this.y = y;
    this.distanceUnit = distanceUnit;
    this.heading = heading;
    this.headingUnit = headingUnit;
  }

  public double getX(DistanceUnit unit) {
    return unit.fromUnit(distanceUnit, x);
  }

  public double getY(DistanceUnit unit) {
    return unit.fromUnit(distanceUnit, y);
  }

  public double getHeading(AngleUnit unit) {
    return unit.fromUnit(headingUnit, heading);
  }

  @Override
  public String toString() {
    return String.format(Locale.US, "(%.3f %.3f)%s  %.3f%s", x, y, distanceUnit, heading, headingUnit);
  }
}
