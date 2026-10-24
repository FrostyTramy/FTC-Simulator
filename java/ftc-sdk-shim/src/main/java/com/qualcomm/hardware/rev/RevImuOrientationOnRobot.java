package com.qualcomm.hardware.rev;

import com.qualcomm.robotcore.hardware.ImuOrientationOnRobot;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;

/**
 * Base of the REV IMU mounting descriptions (SDK 12.0 public API). Holds the orientation of the IMU
 * frame seen from the robot; readings are turned into robot readings with its inverse.
 */
public abstract class RevImuOrientationOnRobot implements ImuOrientationOnRobot {
  private final Quaternion imuInRobot;

  protected RevImuOrientationOnRobot(Quaternion imuCoordinateSystemOrientationFromPerspectiveOfRobot) {
    this.imuInRobot = imuCoordinateSystemOrientationFromPerspectiveOfRobot.normalized();
  }

  @Override
  public Quaternion imuCoordinateSystemOrientationFromPerspectiveOfRobot() {
    return imuInRobot;
  }

  @Override
  public Quaternion imuRotationOffset() {
    return imuInRobot.inverse();
  }

  @Override
  public Quaternion angularVelocityTransform() {
    return imuInRobot;
  }
}
