package org.firstinspires.ftc.vision.apriltag;

import java.util.ArrayList;
import org.firstinspires.ftc.robotcore.external.matrices.VectorF;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;

/**
 * The season tag libraries of SDK 12.0 (data from the SDK's Vision-12.0.0 AprilTagGameDatabase). BIOBUZZ tags form 4
 * clusters whose origin is the centre of the CELL opening (SDK 12.0 release notes); their field pose is the identity
 * because the HIVES tip, so a cluster's robotPose is relative to that origin, not to the field.
 */
public class AprilTagGameDatabase {
  public static AprilTagLibrary getCurrentGameTagLibrary() {
    return new AprilTagLibrary.Builder().addLibrary(getSampleTagLibrary()).addLibrary(getBioBuzzTagLibrary()).build();
  }

  public static AprilTagLibrary getCenterStageTagLibrary() {
    Quaternion backdrop = new Quaternion(0.3535534f, -0.6123724f, 0.6123724f, -0.3535534f, 0);
    Quaternion audience = new Quaternion(0.5f, -0.5f, -0.5f, 0.5f, 0);
    return new AprilTagLibrary.Builder()
        .addTag(1, "BlueAllianceLeft", 2.0, new VectorF(60.25f, 41.41f, 4f), DistanceUnit.INCH, backdrop)
        .addTag(2, "BlueAllianceCenter", 2.0, new VectorF(60.25f, 35.41f, 4f), DistanceUnit.INCH, backdrop)
        .addTag(3, "BlueAllianceRight", 2.0, new VectorF(60.25f, 29.41f, 4f), DistanceUnit.INCH, backdrop)
        .addTag(4, "RedAllianceLeft", 2.0, new VectorF(60.25f, -29.41f, 4f), DistanceUnit.INCH, backdrop)
        .addTag(5, "RedAllianceCenter", 2.0, new VectorF(60.25f, -35.41f, 4f), DistanceUnit.INCH, backdrop)
        .addTag(6, "RedAllianceRight", 2.0, new VectorF(60.25f, -41.41f, 4f), DistanceUnit.INCH, backdrop)
        .addTag(7, "RedAudienceWallLarge", 5.0, new VectorF(-70.25f, -40.625f, 5.5f), DistanceUnit.INCH, audience)
        .addTag(8, "RedAudienceWallSmall", 2.0, new VectorF(-70.25f, -35.125f, 4f), DistanceUnit.INCH, audience)
        .addTag(9, "BlueAudienceWallSmall", 2.0, new VectorF(-70.25f, 35.125f, 4f), DistanceUnit.INCH, audience)
        .addTag(10, "BlueAudienceWallLarge", 5.0, new VectorF(-70.25f, 40.625f, 5.5f), DistanceUnit.INCH, audience)
        .build();
  }

  public static AprilTagLibrary getIntoTheDeepTagLibrary() {
    return new AprilTagLibrary.Builder()
        .addTag(11, "BlueAudienceWall", 4.0, new VectorF(-70.25f, 46.83f, 5.75f), DistanceUnit.INCH,
            new Quaternion(0.5f, -0.5f, -0.5f, 0.5f, 0))
        .addTag(12, "BlueAllianceWall", 4.0, new VectorF(0f, 70.25f, 5.75f), DistanceUnit.INCH,
            new Quaternion(0.7071067f, -0.7071067f, 0f, 0f, 0))
        .addTag(13, "BlueRearWall", 4.0, new VectorF(70.25f, 46.83f, 5.75f), DistanceUnit.INCH,
            new Quaternion(0.5f, -0.5f, 0.5f, -0.5f, 0))
        .addTag(14, "RedRearWall", 4.0, new VectorF(70.25f, -46.83f, 5.75f), DistanceUnit.INCH,
            new Quaternion(0.5f, -0.5f, 0.5f, -0.5f, 0))
        .addTag(15, "RedAllianceWall", 4.0, new VectorF(0f, -70.25f, 5.75f), DistanceUnit.INCH,
            new Quaternion(0f, 0f, 0.7071067f, -0.7071067f, 0))
        .addTag(16, "RedAudienceWall", 4.0, new VectorF(-70.25f, -46.83f, 5.75f), DistanceUnit.INCH,
            new Quaternion(0.5f, -0.5f, -0.5f, 0.5f, 0))
        .build();
  }

  public static AprilTagLibrary getDecodeTagLibrary() {
    return new AprilTagLibrary.Builder()
        .addTag(20, "BlueTarget", 6.5, new VectorF(-58.3727f, -55.6425f, 29.5f), DistanceUnit.INCH,
            new Quaternion(0.2182149f, -0.2182149f, -0.6725937f, 0.6725937f, 0))
        .addTag(21, "Obelisk_GPP", 6.5, DistanceUnit.INCH)
        .addTag(22, "Obelisk_PGP", 6.5, DistanceUnit.INCH)
        .addTag(23, "Obelisk_PPG", 6.5, DistanceUnit.INCH)
        .addTag(24, "RedTarget", 6.5, new VectorF(-58.3727f, 55.6425f, 29.5f), DistanceUnit.INCH,
            new Quaternion(0.6725937f, -0.6725937f, -0.2182149f, 0.2182149f, 0))
        .build();
  }

  public static AprilTagLibrary getBioBuzzTagLibrary() {
    AprilTagClusterMetadata redScoring = new AprilTagClusterMetadata(getBioBuzzCluster(30), "RED SCORING", "RED SCORING",
        new VectorF(0f, 0f, 0f), DistanceUnit.INCH, Quaternion.identityQuaternion());
    AprilTagClusterMetadata redAudience = new AprilTagClusterMetadata(getBioBuzzCluster(34), "RED AUDIENCE",
        "RED AUDIENCE", new VectorF(0f, 0f, 0f), DistanceUnit.INCH, Quaternion.identityQuaternion());
    AprilTagClusterMetadata blueAudience = new AprilTagClusterMetadata(getBioBuzzCluster(38), "BLUE AUDIENCE",
        "BLUE AUDIENCE", new VectorF(0f, 0f, 0f), DistanceUnit.INCH, Quaternion.identityQuaternion());
    AprilTagClusterMetadata blueScoring = new AprilTagClusterMetadata(getBioBuzzCluster(42), "BLUE SCORING",
        "BLUE SCORING", new VectorF(0f, 0f, 0f), DistanceUnit.INCH, Quaternion.identityQuaternion());
    return new AprilTagLibrary.Builder().addCluster(redScoring).addCluster(redAudience).addCluster(blueAudience)
        .addCluster(blueScoring).build();
  }

  /** Four 3.25 in tags, left to right, 7.1874 in below and 5.622 in in front of the cluster origin (SDK data). */
  private static ArrayList<AprilTagClusterMemberMetadata> getBioBuzzCluster(int firstID) {
    ArrayList<AprilTagClusterMemberMetadata> cluster = new ArrayList<>();
    cluster.add(new AprilTagClusterMemberMetadata(firstID, new VectorF(-6.5f, 7.1874f, -5.622f), 3.25));
    cluster.add(new AprilTagClusterMemberMetadata(firstID + 1, new VectorF(-2.75f, 7.1874f, -5.622f), 3.25));
    cluster.add(new AprilTagClusterMemberMetadata(firstID + 2, new VectorF(2.75f, 7.1874f, -5.622f), 3.25));
    cluster.add(new AprilTagClusterMemberMetadata(firstID + 3, new VectorF(6.5f, 7.1874f, -5.622f), 3.25));
    return cluster;
  }

  public static AprilTagLibrary getSampleTagLibrary() {
    ArrayList<AprilTagClusterMemberMetadata> members = new ArrayList<>();
    members.add(new AprilTagClusterMemberMetadata(581, new VectorF(-2f, 0f, 0f), 3.0));
    members.add(new AprilTagClusterMemberMetadata(582, new VectorF(2f, 0f, 0f), 3.0));
    AprilTagClusterMetadata goal = new AprilTagClusterMetadata(members, "Center Goal", "GOAL", new VectorF(0f, 0f, 0f),
        DistanceUnit.INCH, Quaternion.identityQuaternion());
    return new AprilTagLibrary.Builder()
        .addTag(583, "Robbie", 2.0, DistanceUnit.INCH)
        .addTag(584, "Number 5", 4.0, DistanceUnit.INCH)
        .addTag(585, "C3PO", 6.0, DistanceUnit.INCH)
        .addTag(586, "K9", 6.0, DistanceUnit.INCH)
        .addCluster(goal)
        .build();
  }
}
