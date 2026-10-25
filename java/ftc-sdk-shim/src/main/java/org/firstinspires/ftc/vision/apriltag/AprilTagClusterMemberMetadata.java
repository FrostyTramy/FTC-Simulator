package org.firstinspires.ftc.vision.apriltag;

import org.firstinspires.ftc.robotcore.external.matrices.VectorF;

/** One tag of a cluster: id, centre in the cluster frame and size (SDK 12.0 public API). */
public class AprilTagClusterMemberMetadata {
  public final int id;
  public final VectorF positionInClusterPlane;
  public final double tagsize;

  public AprilTagClusterMemberMetadata(int id, VectorF positionInClusterPlane, double tagsize) {
    this.id = id;
    this.positionInClusterPlane = positionInClusterPlane;
    this.tagsize = tagsize;
  }
}
