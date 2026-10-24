package com.qualcomm.robotcore.hardware;

import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;

/** How an IMU is mounted on the robot (SDK 12.0 public API). */
public interface ImuOrientationOnRobot {
  /** Orientation of the IMU coordinate system as seen from the robot. */
  Quaternion imuCoordinateSystemOrientationFromPerspectiveOfRobot();

  /** Rotation that turns IMU readings into robot readings. */
  Quaternion imuRotationOffset();

  /** Rotation applied to angular velocities. */
  Quaternion angularVelocityTransform();
}
