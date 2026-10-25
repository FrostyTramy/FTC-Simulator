package org.firstinspires.ftc.vision.apriltag;

import java.util.ArrayList;
import org.firstinspires.ftc.robotcore.external.matrices.VectorF;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;

/** The tags and clusters a processor knows (SDK 12.0 public API). */
public class AprilTagLibrary {
  private final AprilTagMetadata[] singleTags;
  private final AprilTagClusterMetadata[] clusters;

  private AprilTagLibrary(AprilTagMetadata[] singleTags, AprilTagClusterMetadata[] clusters) {
    this.singleTags = singleTags;
    this.clusters = clusters;
  }

  public AprilTagMetadata[] getAllTags() {
    return singleTags;
  }

  public AprilTagClusterMetadata[] getAllClusters() {
    return clusters;
  }

  public AprilTagMetadata lookupTag(int id) {
    for (AprilTagMetadata m : singleTags) {
      if (m.id == id) {
        return m;
      }
    }
    return null;
  }

  public AprilTagClusterMetadata lookupCluster(int id) {
    for (AprilTagClusterMetadata m : clusters) {
      if (m.containsTagId(id)) {
        return m;
      }
    }
    return null;
  }

  /** Builds a library; adding a duplicate id or name throws unless overwriting is allowed (SDK messages). */
  public static class Builder {
    private final ArrayList<AprilTagMetadata> singleTagData = new ArrayList<>();
    private final ArrayList<AprilTagClusterMetadata> clusterData = new ArrayList<>();
    private boolean allowOverwrite = false;

    public Builder setAllowOverwrite(boolean allowOverwrite) {
      this.allowOverwrite = allowOverwrite;
      return this;
    }

    public Builder addTag(AprilTagMetadata aprilTagMetadata) {
      for (AprilTagMetadata m : singleTagData) {
        if (m.id == aprilTagMetadata.id) {
          if (allowOverwrite) {
            singleTagData.remove(m);
            break;
          }
          throw new RuntimeException("You attempted to add a tag to the library when it already contains a tag with that ID. "
              + "You can call .setAllowOverwrite(true) to allow overwriting the existing entry");
        }
      }
      for (AprilTagClusterMetadata c : clusterData) {
        if (c.containsTagId(aprilTagMetadata.id)) {
          throw new RuntimeException("You attempted to add a tag to the library when it contains a tag cluster that has a "
              + "member with the same tag id");
        }
      }
      singleTagData.add(aprilTagMetadata);
      return this;
    }

    public Builder addCluster(AprilTagClusterMetadata cluster) {
      for (AprilTagMetadata m : singleTagData) {
        if (cluster.containsTagId(m.id)) {
          throw new RuntimeException("You attempted to add a tag cluster to the library when a tag ID in that cluster is "
              + "already contained as a single tag in this library.");
        }
      }
      for (AprilTagClusterMetadata c : clusterData) {
        for (Integer i : c.getMemberIds()) {
          if (cluster.containsTagId(i)) {
            throw new RuntimeException("You attempted to add a tag cluster to the library that has clashing tag ids with "
                + "another cluster in the library");
          }
        }
      }
      for (AprilTagClusterMetadata c : clusterData) {
        if (c.name.equals(cluster.name)) {
          if (allowOverwrite) {
            clusterData.remove(c);
            break;
          }
          throw new RuntimeException("You attempted to add a tag cluster to the library when it already contains a tag "
              + "cluster with the same name. You can call .setAllowOverwrite(true) to allow overwriting the existing entry");
        }
      }
      clusterData.add(cluster);
      return this;
    }

    public Builder addTag(int id, String name, double size, VectorF fieldPosition, DistanceUnit distanceUnit,
        Quaternion fieldOrientation) {
      return addTag(new AprilTagMetadata(id, name, size, fieldPosition, distanceUnit, fieldOrientation));
    }

    public Builder addTag(int id, String name, double size, DistanceUnit distanceUnit) {
      return addTag(new AprilTagMetadata(id, name, size, new VectorF(0f, 0f, 0f), distanceUnit,
          Quaternion.identityQuaternion()));
    }

    public Builder addTags(AprilTagLibrary library) {
      for (AprilTagMetadata m : library.getAllTags()) {
        addTag(m);
      }
      return this;
    }

    public Builder addClusters(AprilTagLibrary library) {
      for (AprilTagClusterMetadata m : library.getAllClusters()) {
        addCluster(m);
      }
      return this;
    }

    public Builder addLibrary(AprilTagLibrary lib) {
      addTags(lib);
      addClusters(lib);
      return this;
    }

    public AprilTagLibrary build() {
      return new AprilTagLibrary(singleTagData.toArray(new AprilTagMetadata[0]),
          clusterData.toArray(new AprilTagClusterMetadata[0]));
    }
  }
}
