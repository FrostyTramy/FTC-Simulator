package org.firstinspires.ftc.vision.apriltag;

import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Pose3D;

/** A tag cluster, its pose solved from all visible members (SDK 12.0 public API). */
public class AprilTagClusterDetection extends AprilTagDetection {
  public final int percentClusterFound;
  public final AprilTagClusterMetadata metadata;

  public AprilTagClusterDetection(int percentClusterFound, AprilTagClusterMetadata metadata, DistanceUnit distanceUnit,
      AprilTagPoseFtc ftcPose, AprilTagPoseRaw rawPose, Pose3D robotPose, long frameAcquisitionNanoTime) {
    super(ftcPose, rawPose, robotPose, frameAcquisitionNanoTime, distanceUnit);
    this.percentClusterFound = percentClusterFound;
    this.metadata = metadata;
  }
}
