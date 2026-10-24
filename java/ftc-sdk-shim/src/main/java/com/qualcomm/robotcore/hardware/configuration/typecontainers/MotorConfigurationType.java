package com.qualcomm.robotcore.hardware.configuration.typecontainers;

import org.firstinspires.ftc.robotcore.external.navigation.Rotation;

/**
 * The @MotorType of a configured motor (SDK 12.0 public API, subset): encoder ticks per revolution,
 * gearing, maximum RPM and orientation. The simulator fills it from config/motors.yaml sdk_motor_types.
 */
public class MotorConfigurationType {
  private final String name;
  private final String xmlTag;
  private final double ticksPerRev;
  private final double gearing;
  private final double maxRpm;
  private final double achieveableMaxRpmFraction;
  private final Rotation orientation;

  public MotorConfigurationType(String name, String xmlTag, double ticksPerRev, double gearing, double maxRpm,
      double achieveableMaxRpmFraction, Rotation orientation) {
    this.name = name;
    this.xmlTag = xmlTag;
    this.ticksPerRev = ticksPerRev;
    this.gearing = gearing;
    this.maxRpm = maxRpm;
    this.achieveableMaxRpmFraction = achieveableMaxRpmFraction;
    this.orientation = orientation;
  }

  /** The SDK's "Unspecified Motor" type (RobotCore UnspecifiedMotor @MotorType). */
  public static MotorConfigurationType getUnspecifiedMotorType() {
    return new MotorConfigurationType("Unspecified Motor", "Motor", 1440, 52, 165, 0.85, Rotation.CW);
  }

  public String getName() {
    return name;
  }

  public String getXmlTag() {
    return xmlTag;
  }

  public double getTicksPerRev() {
    return ticksPerRev;
  }

  public double getGearing() {
    return gearing;
  }

  public double getMaxRPM() {
    return maxRpm;
  }

  public double getAchieveableMaxRPMFraction() {
    return achieveableMaxRpmFraction;
  }

  public double getAchieveableMaxTicksPerSecond() {
    return ticksPerRev * maxRpm * achieveableMaxRpmFraction / 60.0;
  }

  public int getAchieveableMaxTicksPerSecondRounded() {
    return (int) Math.round(getAchieveableMaxTicksPerSecond());
  }

  public Rotation getOrientation() {
    return orientation;
  }

  @Override
  public String toString() {
    return name;
  }
}
