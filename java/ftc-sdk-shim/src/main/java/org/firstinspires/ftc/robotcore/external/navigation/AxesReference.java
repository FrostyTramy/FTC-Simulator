package org.firstinspires.ftc.robotcore.external.navigation;

/** Whether rotations are about fixed (EXTRINSIC) or moving (INTRINSIC) axes (SDK 12.0 public API). */
public enum AxesReference {
  EXTRINSIC,
  INTRINSIC;

  public AxesReference reverse() {
    return this == EXTRINSIC ? INTRINSIC : EXTRINSIC;
  }
}
