package org.firstinspires.ftc.robotcore.external.navigation;

/** Order of the three Euler rotations (SDK 12.0 public API). Index 0 = X, 1 = Y, 2 = Z. */
public enum AxesOrder {
  XZX(0, 2, 0),
  XYX(0, 1, 0),
  YXY(1, 0, 1),
  YZY(1, 2, 1),
  ZYZ(2, 1, 2),
  ZXZ(2, 0, 2),
  XZY(0, 2, 1),
  XYZ(0, 1, 2),
  YXZ(1, 0, 2),
  YZX(1, 2, 0),
  ZYX(2, 1, 0),
  ZXY(2, 0, 1);

  private final int[] indices;

  AxesOrder(int a, int b, int c) {
    indices = new int[] {a, b, c};
  }

  /** Axis indices of the three rotations in order. */
  public int[] indices() {
    return indices.clone();
  }

  public AxesOrder reverse() {
    for (AxesOrder o : values()) {
      if (o.indices[0] == indices[2] && o.indices[1] == indices[1] && o.indices[2] == indices[0]) {
        return o;
      }
    }
    return this;
  }
}
