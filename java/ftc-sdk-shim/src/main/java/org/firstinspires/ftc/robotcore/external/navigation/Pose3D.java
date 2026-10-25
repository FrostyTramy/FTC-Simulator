package org.firstinspires.ftc.robotcore.external.navigation;

/** A position and orientation (SDK 12.0 public API). */
public class Pose3D {
  protected final Position position;
  protected final YawPitchRollAngles orientation;

  public Pose3D(Position position, YawPitchRollAngles orientation) {
    this.position = position;
    this.orientation = orientation;
  }

  public YawPitchRollAngles getOrientation() {
    return orientation;
  }

  public Position getPosition() {
    return position;
  }

  @Override
  public String toString() {
    return "position=" + position + " orientation=" + orientation;
  }
}
