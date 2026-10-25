package org.firstinspires.ftc.robotcore.external.navigation;

/**
 * Rotation matrices of Euler angles and back, for all 12 axis orders and both references. Intrinsic ABC:
 * R = R_A(a1)·R_B(a2)·R_C(a3); extrinsic ABC: R = R_C(a3)·R_B(a2)·R_A(a1) (rotations counter-clockwise looking down
 * the axis). Angle extraction gives the principal solution (middle angle in [−90°, 90°] for Tait–Bryan orders,
 * [0°, 180°] for proper Euler orders) or, on request, the other one.
 */
final class EulerMath {
  private EulerMath() {}

  static double[][] axis(int axis, double angle) {
    double c = Math.cos(angle);
    double s = Math.sin(angle);
    switch (axis) {
      case 0:
        return new double[][] {{1, 0, 0}, {0, c, -s}, {0, s, c}};
      case 1:
        return new double[][] {{c, 0, s}, {0, 1, 0}, {-s, 0, c}};
      default:
        return new double[][] {{c, -s, 0}, {s, c, 0}, {0, 0, 1}};
    }
  }

  static double[][] mul(double[][] a, double[][] b) {
    double[][] r = new double[3][3];
    for (int i = 0; i < 3; i++) {
      for (int j = 0; j < 3; j++) {
        r[i][j] = a[i][0] * b[0][j] + a[i][1] * b[1][j] + a[i][2] * b[2][j];
      }
    }
    return r;
  }

  static double[][] rotation(AxesReference reference, AxesOrder order, double a1, double a2, double a3) {
    int[] ix = order.indices();
    double[][] r1 = axis(ix[0], a1);
    double[][] r2 = axis(ix[1], a2);
    double[][] r3 = axis(ix[2], a3);
    return reference == AxesReference.INTRINSIC ? mul(mul(r1, r2), r3) : mul(mul(r3, r2), r1);
  }

  /** The three angles (radians) of {@code r} in the given convention. */
  static double[] angles(double[][] r, AxesReference reference, AxesOrder order, boolean theOther) {
    if (reference == AxesReference.EXTRINSIC) {
      double[] intrinsic = angles(r, AxesReference.INTRINSIC, order.reverse(), theOther);
      return new double[] {intrinsic[2], intrinsic[1], intrinsic[0]};
    }
    int[] ix = order.indices();
    int i = ix[0];
    int j = ix[1];
    double s = ((j - i + 3) % 3 == 1) ? 1 : -1;
    double a;
    double b;
    double c;
    if (ix[2] != i) { // Tait–Bryan
      int k = ix[2];
      b = Math.asin(clamp(s * r[i][k]));
      if (Math.abs(r[i][k]) < 0.9999999) {
        a = Math.atan2(-s * r[j][k], r[k][k]);
        c = Math.atan2(-s * r[i][j], r[i][i]);
      } else {
        a = Math.atan2(s * r[k][j], r[j][j]);
        c = 0;
      }
      if (theOther) {
        a += Math.PI;
        b = Math.PI - b;
        c += Math.PI;
      }
    } else { // proper Euler
      int k = 3 - i - j;
      b = Math.acos(clamp(r[i][i]));
      if (Math.sin(b) > 1e-7) {
        a = Math.atan2(r[j][i], -s * r[k][i]);
        c = Math.atan2(r[i][j], s * r[i][k]);
      } else {
        a = Math.atan2(s * r[k][j], r[j][j]);
        c = 0;
      }
      if (theOther) {
        a += Math.PI;
        b = -b;
        c += Math.PI;
      }
    }
    return new double[] {AngleUnit.normalizeRadians(a), AngleUnit.normalizeRadians(b), AngleUnit.normalizeRadians(c)};
  }

  private static double clamp(double v) {
    return Math.max(-1, Math.min(1, v));
  }
}
