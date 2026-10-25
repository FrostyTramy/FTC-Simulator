package org.firstinspires.ftc.robotcore.external.navigation;

import java.util.Locale;
import org.firstinspires.ftc.robotcore.external.matrices.GeneralMatrixF;
import org.firstinspires.ftc.robotcore.external.matrices.MatrixF;

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

  /** Rotation matrix of this quaternion (3×3). */
  public MatrixF toMatrix() {
    double[][] r = rotation();
    float[] data = new float[9];
    for (int i = 0; i < 3; i++) {
      for (int j = 0; j < 3; j++) {
        data[i * 3 + j] = (float) r[i][j];
      }
    }
    return new GeneralMatrixF(3, 3, data);
  }

  /** The quaternion of the rotation in the upper-left 3×3 of {@code m}. */
  public static Quaternion fromMatrix(MatrixF m, long acquisitionTime) {
    double m00 = m.get(0, 0), m01 = m.get(0, 1), m02 = m.get(0, 2);
    double m10 = m.get(1, 0), m11 = m.get(1, 1), m12 = m.get(1, 2);
    double m20 = m.get(2, 0), m21 = m.get(2, 1), m22 = m.get(2, 2);
    double trace = m00 + m11 + m22;
    double w, x, y, z;
    if (trace > 0) {
      double s = Math.sqrt(trace + 1) * 2;
      w = s / 4;
      x = (m21 - m12) / s;
      y = (m02 - m20) / s;
      z = (m10 - m01) / s;
    } else if (m00 > m11 && m00 > m22) {
      double s = Math.sqrt(1 + m00 - m11 - m22) * 2;
      w = (m21 - m12) / s;
      x = s / 4;
      y = (m01 + m10) / s;
      z = (m02 + m20) / s;
    } else if (m11 > m22) {
      double s = Math.sqrt(1 + m11 - m00 - m22) * 2;
      w = (m02 - m20) / s;
      x = (m01 + m10) / s;
      y = s / 4;
      z = (m12 + m21) / s;
    } else {
      double s = Math.sqrt(1 + m22 - m00 - m11) * 2;
      w = (m10 - m01) / s;
      x = (m02 + m20) / s;
      y = (m12 + m21) / s;
      z = s / 4;
    }
    return new Quaternion((float) w, (float) x, (float) y, (float) z, acquisitionTime);
  }

  /** Rotation matrix (row-major 3×3) of this quaternion. */
  double[][] rotation() {
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

  /** Euler angles of this rotation in any of the 24 conventions (principal solution). */
  public Orientation toOrientation(AxesReference reference, AxesOrder order, AngleUnit unit) {
    double[] a = EulerMath.angles(rotation(), reference, order, false);
    return new Orientation(reference, order, unit, (float) unit.fromRadians(a[0]), (float) unit.fromRadians(a[1]),
        (float) unit.fromRadians(a[2]), acquisitionTime);
  }

  @Override
  public String toString() {
    return String.format(Locale.US, "{w=%.3f, x=%.3f, y=%.3f, z=%.3f}", w, x, y, z);
  }
}
