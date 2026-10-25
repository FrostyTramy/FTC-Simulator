package org.firstinspires.ftc.vision.apriltag;

import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Pose3D;

/** A detected tag or tag cluster (SDK 12.0 public API): see AprilTagSingleDetection and AprilTagClusterDetection. */
public abstract class AprilTagDetection {
  public final AprilTagPoseFtc ftcPose;
  public final AprilTagPoseRaw rawPose;
  public final Pose3D robotPose;
  public final long frameAcquisitionNanoTime;
  public final DistanceUnit distanceUnit;

  public AprilTagDetection(AprilTagPoseFtc ftcPose, AprilTagPoseRaw rawPose, Pose3D robotPose, long frameAcquisitionNanoTime,
      DistanceUnit distanceUnit) {
    this.ftcPose = ftcPose;
    this.rawPose = rawPose;
    this.robotPose = robotPose;
    this.frameAcquisitionNanoTime = frameAcquisitionNanoTime;
    this.distanceUnit = distanceUnit;
  }
}
