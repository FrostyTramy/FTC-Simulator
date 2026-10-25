package org.firstinspires.ftc.vision.apriltag;

import org.firstinspires.ftc.robotcore.external.matrices.VectorF;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;

/** A single tag in a library: id, name, size and field pose (SDK 12.0 public API). */
public class AprilTagMetadata {
  public final int id;
  public final double tagsize;
  public final String name;
  public final DistanceUnit distanceUnit;
  public final VectorF fieldPosition;
  public final Quaternion fieldOrientation;

  public AprilTagMetadata(int id, String name, double tagsize, VectorF fieldPosition, DistanceUnit distanceUnit,
      Quaternion fieldOrientation) {
    this.id = id;
    this.name = name;
    this.tagsize = tagsize;
    this.fieldOrientation = fieldOrientation;
    this.fieldPosition = fieldPosition;
    this.distanceUnit = distanceUnit;
  }

  public AprilTagMetadata(int id, String name, double tagsize, DistanceUnit distanceUnit) {
    this(id, name, tagsize, new VectorF(0f, 0f, 0f), distanceUnit, Quaternion.identityQuaternion());
  }
}
