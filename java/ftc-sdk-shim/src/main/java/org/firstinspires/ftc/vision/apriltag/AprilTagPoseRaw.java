package org.firstinspires.ftc.vision.apriltag;

import org.firstinspires.ftc.robotcore.external.matrices.MatrixF;

/** Tag pose in the camera frame (OpenCV axes: x right, y down, z forward) with its rotation matrix (SDK 12.0 API). */
public class AprilTagPoseRaw {
  public final double x;
  public final double y;
  public final double z;
  public final MatrixF R;

  public AprilTagPoseRaw(double x, double y, double z, MatrixF R) {
    this.x = x;
    this.y = y;
    this.z = z;
    this.R = R;
  }
}
