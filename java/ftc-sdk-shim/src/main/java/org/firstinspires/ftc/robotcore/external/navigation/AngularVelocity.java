package org.firstinspires.ftc.robotcore.external.navigation;

import java.util.Locale;

/** Rotation rates about the X, Y and Z axes (SDK 12.0 public API). */
public class AngularVelocity {
  public UnnormalizedAngleUnit angleUnit;
  public float xRotationRate;
  public float yRotationRate;
  public float zRotationRate;
  public long acquisitionTime;

  public AngularVelocity() {
    this(UnnormalizedAngleUnit.DEGREES, 0, 0, 0, 0);
  }

  public AngularVelocity(UnnormalizedAngleUnit unit, float x, float y, float z, long acquisitionTime) {
    this.angleUnit = unit;
    this.xRotationRate = x;
    this.yRotationRate = y;
    this.zRotationRate = z;
    this.acquisitionTime = acquisitionTime;
  }

  public AngularVelocity(AngleUnit unit, float x, float y, float z, long acquisitionTime) {
    this(unit.getUnnormalized(), x, y, z, acquisitionTime);
  }

  public AngularVelocity toAngleUnit(AngleUnit unit) {
    UnnormalizedAngleUnit u = unit.getUnnormalized();
    return new AngularVelocity(u, (float) u.fromUnit(angleUnit, xRotationRate), (float) u.fromUnit(angleUnit, yRotationRate),
        (float) u.fromUnit(angleUnit, zRotationRate), acquisitionTime);
  }

  @Override
  public String toString() {
    return String.format(Locale.US, "{x=%.3f, y=%.3f, z=%.3f}", xRotationRate, yRotationRate, zRotationRate);
  }
}
