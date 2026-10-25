package org.firstinspires.ftc.vision.apriltag;

import java.util.ArrayList;
import org.firstinspires.ftc.robotcore.external.matrices.VectorF;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;

/**
 * Co-planar tags whose positions are given relative to a common origin (SDK 12.0 public API); the cluster's pose is
 * solved from every visible member. At least 2 members, no duplicate ids.
 */
public class AprilTagClusterMetadata {
  public final String name;
  public final String shortName;
  public final DistanceUnit distanceUnit;
  public final VectorF fieldPosition;
  public final Quaternion fieldOrientation;
  double xExtentMin = Double.POSITIVE_INFINITY;
  double xExtentMax = Double.NEGATIVE_INFINITY;
  double yExtentMin = Double.POSITIVE_INFINITY;
  double yExtentMax = Double.NEGATIVE_INFINITY;
  double zExtentMin = Double.POSITIVE_INFINITY;
  double zExtentMax = Double.NEGATIVE_INFINITY;
  double largestTagSize = 0.0;
  ArrayList<AprilTagClusterMemberMetadata> clusterMembers;

  public AprilTagClusterMetadata(ArrayList<AprilTagClusterMemberMetadata> clusterMembers, String name, String shortName,
      VectorF fieldPosition, DistanceUnit distanceUnit, Quaternion fieldOrientation) {
    if (clusterMembers.size() < 2) {
      throw new IllegalArgumentException("Cannot make a cluster with less than 2 tags");
    }
    for (int i = 0; i < clusterMembers.size(); i++) {
      for (int j = 0; j < clusterMembers.size(); j++) {
        if (i != j && clusterMembers.get(i).id == clusterMembers.get(j).id) {
          throw new IllegalArgumentException("Cannot create cluster with duplicate tag IDs");
        }
      }
    }
    for (AprilTagClusterMemberMetadata m : clusterMembers) {
      xExtentMin = Math.min(xExtentMin, m.positionInClusterPlane.get(0) - m.tagsize / 2.0);
      xExtentMax = Math.max(xExtentMax, m.positionInClusterPlane.get(0) + m.tagsize / 2.0);
      yExtentMin = Math.min(yExtentMin, m.positionInClusterPlane.get(1) - m.tagsize / 2.0);
      yExtentMax = Math.max(yExtentMax, m.positionInClusterPlane.get(1) + m.tagsize / 2.0);
      zExtentMin = Math.min(zExtentMin, m.positionInClusterPlane.get(2));
      zExtentMax = Math.max(zExtentMax, m.positionInClusterPlane.get(2));
      largestTagSize = Math.max(largestTagSize, m.tagsize);
    }
    this.clusterMembers = clusterMembers;
    this.name = name;
    this.shortName = shortName;
    this.distanceUnit = distanceUnit;
    this.fieldPosition = fieldPosition;
    this.fieldOrientation = fieldOrientation;
  }

  protected boolean containsTagId(int id) {
    for (AprilTagClusterMemberMetadata m : clusterMembers) {
      if (m.id == id) {
        return true;
      }
    }
    return false;
  }

  protected ArrayList<Integer> getMemberIds() {
    ArrayList<Integer> ids = new ArrayList<>();
    for (AprilTagClusterMemberMetadata m : clusterMembers) {
      ids.add(m.id);
    }
    return ids;
  }

  protected AprilTagClusterMemberMetadata getMemberMetadata(int id) {
    for (AprilTagClusterMemberMetadata m : clusterMembers) {
      if (m.id == id) {
        return m;
      }
    }
    return null;
  }
}
