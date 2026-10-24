package org.firstinspires.ftc.robotcore.external.navigation;

import java.util.Locale;

/** Three Euler angles with their reference, order and unit (SDK 12.0 public API, subset). */
public class Orientation {
  public AxesReference axesReference;
  public AxesOrder axesOrder;
  public AngleUnit angleUnit;
  public float firstAngle;
  public float secondAngle;
  public float thirdAngle;
  public long acquisitionTime;

  public Orientation() {
    this(AxesReference.EXTRINSIC, AxesOrder.XYZ, AngleUnit.RADIANS, 0, 0, 0, 0);
  }

  public Orientation(AxesReference axesReference, AxesOrder axesOrder, AngleUnit angleUnit, float firstAngle,
      float secondAngle, float thirdAngle, long acquisitionTime) {
    this.axesReference = axesReference;
    this.axesOrder = axesOrder;
    this.angleUnit = angleUnit;
    this.firstAngle = firstAngle;
    this.secondAngle = secondAngle;
    this.thirdAngle = thirdAngle;
    this.acquisitionTime = acquisitionTime;
  }

  public Orientation toAngleUnit(AngleUnit unit) {
    if (unit == angleUnit) {
      return this;
    }
    return new Orientation(axesReference, axesOrder, unit, unit.fromUnit(angleUnit, firstAngle),
        unit.fromUnit(angleUnit, secondAngle), unit.fromUnit(angleUnit, thirdAngle), acquisitionTime);
  }

  /** Same rotation, expressed with another reference/order/unit. */
  public Orientation toAxesOrder(AxesReference reference, AxesOrder order, AngleUnit unit) {
    return Quaternion.fromOrientation(this).toOrientation(reference, order, unit);
  }

  @Override
  public String toString() {
    String u = angleUnit == AngleUnit.DEGREES ? "°" : "rad";
    return String.format(Locale.US, "{%s %s %.1f%s %.1f%s %.1f%s}", axesReference, axesOrder, firstAngle, u, secondAngle, u,
        thirdAngle, u);
  }
}
