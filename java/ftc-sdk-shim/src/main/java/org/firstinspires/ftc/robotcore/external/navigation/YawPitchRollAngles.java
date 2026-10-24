package org.firstinspires.ftc.robotcore.external.navigation;

import java.util.Locale;

/**
 * Robot orientation as yaw (about +Z, counter-clockwise positive), pitch (about +X) and roll (about +Y)
 * in the FTC robot frame (+X right, +Y forward, +Z up), SDK 12.0 public API.
 */
public class YawPitchRollAngles {
  private final AngleUnit angleUnit;
  private final double yaw;
  private final double pitch;
  private final double roll;
  private final long acquisitionTime;

  public YawPitchRollAngles(AngleUnit angleUnit, double yaw, double pitch, double roll, long acquisitionTime) {
    this.angleUnit = angleUnit;
    this.yaw = yaw;
    this.pitch = pitch;
    this.roll = roll;
    this.acquisitionTime = acquisitionTime;
  }

  public double getYaw() {
    return getYaw(AngleUnit.DEGREES);
  }

  public double getYaw(AngleUnit unit) {
    return unit.fromUnit(angleUnit, yaw);
  }

  public double getPitch() {
    return getPitch(AngleUnit.DEGREES);
  }

  public double getPitch(AngleUnit unit) {
    return unit.fromUnit(angleUnit, pitch);
  }

  public double getRoll() {
    return getRoll(AngleUnit.DEGREES);
  }

  public double getRoll(AngleUnit unit) {
    return unit.fromUnit(angleUnit, roll);
  }

  public long getAcquisitionTime() {
    return acquisitionTime;
  }

  @Override
  public String toString() {
    return String.format(Locale.US, "{yaw=%.3f, pitch=%.3f, roll=%.3f}", getYaw(), getPitch(), getRoll());
  }
}
