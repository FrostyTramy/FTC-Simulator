package org.firstinspires.ftc.robotcore.external.navigation;

import java.util.Locale;

/**
 * Unit quaternion w + xi + yj + zk (SDK 12.0 public API, subset) with conversions to and from Euler
 * angles for all twelve axis orders.
 */
public class Quaternion {
  public float w;
  public float x;
  public float y;
  public float z;
  public long acquisitionTime;

  public Quaternion() {
    this(1, 0, 0, 0, 0);
  }

  public Quaternion(float w, float x, float y, float z, long acquisitionTime) {
    this.w = w;
    this.x = x;
    this.y = y;
    this.z = z;
    this.acquisitionTime = acquisitionTime;
  }

  public static Quaternion identityQuaternion() {
    return new Quaternion(1, 0, 0, 0, 0);
  }

  public float magnitude() {
    return (float) Math.sqrt(w * w + x * x + y * y + z * z);
  }

  public Quaternion normalized() {
    float m = magnitude();
    return new Quaternion(w / m, x / m, y / m, z / m, acquisitionTime);
  }

  public Quaternion conjugate() {
    return new Quaternion(w, -x, -y, -z, acquisitionTime);
  }

  public Quaternion inverse() {
    return normalized().conjugate();
  }

  /** Hamilton product this ⊗ q. */
  public Quaternion multiply(Quaternion q, long acquisitionTime) {
    return new Quaternion(
        w * q.w - x * q.x - y * q.y - z * q.z,
        w * q.x + x * q.w + y * q.z - z * q.y,
        w * q.y - x * q.z + y * q.w + z * q.x,
        w * q.z + x * q.y - y * q.x + z * q.w,
        acquisitionTime);
  }

  /** Rotation matrix (row-major 3×3) of this quaternion. */
  double[][] toMatrix() {
    double ww = w, xx = x, yy = y, zz = z;
    double n = Math.sqrt(ww * ww + xx * xx + yy * yy + zz * zz);
    ww /= n;
    xx /= n;
    yy /= n;
    zz /= n;
    return new double[][] {
      {1 - 2 * (yy * yy + zz * zz), 2 * (xx * yy - ww * zz), 2 * (xx * zz + ww * yy)},
      {2 * (xx * yy + ww * zz), 1 - 2 * (xx * xx + zz * zz), 2 * (yy * zz - ww * xx)},
      {2 * (xx * zz - ww * yy), 2 * (yy * zz + ww * xx), 1 - 2 * (xx * xx + yy * yy)}
    };
  }

  static Quaternion fromAxisAngle(int axis, double angle) {
    double s = Math.sin(angle / 2);
    double c = Math.cos(angle / 2);
    return new Quaternion((float) c, axis == 0 ? (float) s : 0, axis == 1 ? (float) s : 0, axis == 2 ? (float) s : 0, 0);
  }

  /** Rotation described by Euler angles. */
  public static Quaternion fromOrientation(Orientation o) {
    int[] ix = o.axesOrder.indices();
    double[] a = {
      o.angleUnit.toRadians((double) o.firstAngle),
      o.angleUnit.toRadians((double) o.secondAngle),
      o.angleUnit.toRadians((double) o.thirdAngle)
    };
    Quaternion q1 = fromAxisAngle(ix[0], a[0]);
    Quaternion q2 = fromAxisAngle(ix[1], a[1]);
    Quaternion q3 = fromAxisAngle(ix[2], a[2]);
    // Intrinsic: R = R1·R2·R3 (each rotation about the already rotated axes); extrinsic: R = R3·R2·R1.
    Quaternion q = o.axesReference == AxesReference.INTRINSIC ? q1.multiply(q2, 0).multiply(q3, 0) : q3.multiply(q2, 0).multiply(q1, 0);
    q.acquisitionTime = o.acquisitionTime;
    return q;
  }

  /** Euler angles of this rotation (Tait–Bryan orders only, which is what the IMU API uses). */
  public Orientation toOrientation(AxesReference reference, AxesOrder order, AngleUnit unit) {
    if (reference == AxesReference.EXTRINSIC) {
      Orientation intrinsic = toOrientation(AxesReference.INTRINSIC, order.reverse(), unit);
      return new Orientation(AxesReference.EXTRINSIC, order, unit, intrinsic.thirdAngle, intrinsic.secondAngle,
          intrinsic.firstAngle, acquisitionTime);
    }
    int[] ix = order.indices();
    int i = ix[0], j = ix[1], k = ix[2];
    if (i == k) {
      throw new UnsupportedOperationException("proper Euler orders (" + order + ") are not supported");
    }
    double[][] r = toMatrix();
    // For R = Ri(a)·Rj(b)·Rk(c) with (i, j, k) a cyclic/anticyclic permutation (Shoemake's formulas).
    double sign = ((j - i + 3) % 3 == 1) ? 1 : -1;
    double b = Math.asin(Math.max(-1, Math.min(1, sign * r[i][k])));
    double a;
    double c;
    if (Math.abs(r[i][k]) < 0.9999999) {
      a = Math.atan2(-sign * r[j][k], r[k][k]);
      c = Math.atan2(-sign * r[i][j], r[i][i]);
    } else { // gimbal lock: put everything into the first angle
      a = Math.atan2(sign * r[k][j], r[j][j]);
      c = 0;
    }
    return new Orientation(AxesReference.INTRINSIC, order, unit, (float) unit.fromRadians(a), (float) unit.fromRadians(b),
        (float) unit.fromRadians(c), acquisitionTime);
  }

  @Override
  public String toString() {
    return String.format(Locale.US, "{w=%.3f, x=%.3f, y=%.3f, z=%.3f}", w, x, y, z);
  }
}
