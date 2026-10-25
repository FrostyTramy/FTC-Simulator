package org.firstinspires.ftc.robotcore.external.navigation;

import java.util.Locale;
import org.firstinspires.ftc.robotcore.external.matrices.MatrixF;
import org.firstinspires.ftc.robotcore.external.matrices.OpenGLMatrix;

/** Three Euler angles with their reference, order and unit (SDK 12.0 public API, subset). */
public class Orientation {
  /** Which of the two angle triples that describe one rotation {@link #getOrientation} returns. */
  public enum AngleSet {
    THEONE,
    THEOTHER
  }

  public AxesReference axesReference;
  public AxesOrder axesOrder;
  public AngleUnit angleUnit;
  public float firstAngle;
  public float secondAngle;
  public float thirdAngle;
  public long acquisitionTime;

  public Orientation() {
    this(AxesReference.EXTRINSIC, AxesOrder.XYZ, AngleUnit.RADIANS, 0, 0, 0, 0);
  }

  public Orientation(AxesReference axesReference, AxesOrder axesOrder, AngleUnit angleUnit, float firstAngle,
      float secondAngle, float thirdAngle, long acquisitionTime) {
    this.axesReference = axesReference;
    this.axesOrder = axesOrder;
    this.angleUnit = angleUnit;
    this.firstAngle = firstAngle;
    this.secondAngle = secondAngle;
    this.thirdAngle = thirdAngle;
    this.acquisitionTime = acquisitionTime;
  }

  public Orientation toAngleUnit(AngleUnit unit) {
    if (unit == angleUnit) {
      return this;
    }
    return new Orientation(axesReference, axesOrder, unit, unit.fromUnit(angleUnit, firstAngle),
        unit.fromUnit(angleUnit, secondAngle), unit.fromUnit(angleUnit, thirdAngle), acquisitionTime);
  }

  /** Same rotation, expressed with another reference/order/unit. */
  public Orientation toAxesOrder(AxesReference reference, AxesOrder order, AngleUnit unit) {
    return Quaternion.fromOrientation(this).toOrientation(reference, order, unit);
  }

  /** The rotation as a 4×4 transform. */
  public OpenGLMatrix getRotationMatrix() {
    return getRotationMatrix(axesReference, axesOrder, angleUnit, firstAngle, secondAngle, thirdAngle);
  }

  public static OpenGLMatrix getRotationMatrix(AxesReference axesReference, AxesOrder axesOrder, AngleUnit unit,
      float firstAngle, float secondAngle, float thirdAngle) {
    double[][] r = EulerMath.rotation(axesReference, axesOrder, unit.toRadians((double) firstAngle),
        unit.toRadians((double) secondAngle), unit.toRadians((double) thirdAngle));
    OpenGLMatrix m = new OpenGLMatrix();
    for (int i = 0; i < 3; i++) {
      for (int j = 0; j < 3; j++) {
        m.put(i, j, (float) r[i][j]);
      }
    }
    return m;
  }

  /** The angles of the rotation in the upper-left 3×3 of {@code rot}. */
  public static Orientation getOrientation(MatrixF rot, AxesReference axesReference, AxesOrder axesOrder, AngleUnit unit) {
    return getOrientation(rot, axesReference, axesOrder, unit, AngleSet.THEONE);
  }

  public static Orientation getOrientation(MatrixF rot, AxesReference axesReference, AxesOrder axesOrder, AngleUnit unit,
      AngleSet angleSet) {
    double[][] r = new double[3][3];
    for (int i = 0; i < 3; i++) {
      for (int j = 0; j < 3; j++) {
        r[i][j] = rot.get(i, j);
      }
    }
    double[] a = EulerMath.angles(r, axesReference, axesOrder, angleSet == AngleSet.THEOTHER);
    return new Orientation(axesReference, axesOrder, unit, (float) unit.fromRadians(a[0]), (float) unit.fromRadians(a[1]),
        (float) unit.fromRadians(a[2]), 0);
  }

  @Override
  public String toString() {
    String u = angleUnit == AngleUnit.DEGREES ? "°" : "rad";
    return String.format(Locale.US, "{%s %s %.1f%s %.1f%s %.1f%s}", axesReference, axesOrder, firstAngle, u, secondAngle, u,
        thirdAngle, u);
  }
}
