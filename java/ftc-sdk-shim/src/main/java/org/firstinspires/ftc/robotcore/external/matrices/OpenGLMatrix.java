package org.firstinspires.ftc.robotcore.external.matrices;

import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.AxesOrder;
import org.firstinspires.ftc.robotcore.external.navigation.AxesReference;
import org.firstinspires.ftc.robotcore.external.navigation.Orientation;

/**
 * A 4×4 homogeneous transform stored column by column like OpenGL (SDK 12.0 public API). As with Android's
 * android.opengl.Matrix, {@code translated}, {@code rotated}, {@code scaled} and {@code multiplied} post-multiply:
 * {@code m.translated(t)} = m·T.
 */
public class OpenGLMatrix extends ColumnMajorMatrixF {
  float[] data;

  public OpenGLMatrix() {
    super(4, 4);
    data = new float[16];
    for (int i = 0; i < 4; i++) {
      data[i * 5] = 1f;
    }
  }

  /** Column-major 16 values (kept, not copied). */
  public OpenGLMatrix(float[] data) {
    super(4, 4);
    if (data.length != 16) {
      throw dimensionsError(4, 4);
    }
    this.data = data;
  }

  /** A 3×3 rotation (upper left, rest identity) or a 4×4 matrix. */
  public OpenGLMatrix(MatrixF him) {
    this();
    if (him.numRows() > 4 || him.numCols() > 4) {
      throw dimensionsError(him.numRows(), him.numCols());
    }
    for (int r = 0; r < him.numRows(); r++) {
      for (int c = 0; c < him.numCols(); c++) {
        put(r, c, him.get(r, c));
      }
    }
  }

  @Override
  public MatrixF emptyMatrix(int numRows, int numCols) {
    if (numRows == 4 && numCols == 4) {
      return new OpenGLMatrix(new float[16]);
    }
    return new GeneralMatrixF(numRows, numCols);
  }

  /** Rotation by {@code angle} about the axis (dx, dy, dz), counter-clockwise looking down the axis. */
  public static OpenGLMatrix rotation(AngleUnit angleUnit, float angle, float dx, float dy, float dz) {
    double a = angleUnit.toRadians((double) angle);
    double len = Math.sqrt(dx * dx + dy * dy + dz * dz);
    double x = dx / len;
    double y = dy / len;
    double z = dz / len;
    double c = Math.cos(a);
    double s = Math.sin(a);
    double t = 1 - c;
    OpenGLMatrix m = new OpenGLMatrix();
    m.put(0, 0, (float) (t * x * x + c));
    m.put(0, 1, (float) (t * x * y - s * z));
    m.put(0, 2, (float) (t * x * z + s * y));
    m.put(1, 0, (float) (t * x * y + s * z));
    m.put(1, 1, (float) (t * y * y + c));
    m.put(1, 2, (float) (t * y * z - s * x));
    m.put(2, 0, (float) (t * x * z - s * y));
    m.put(2, 1, (float) (t * y * z + s * x));
    m.put(2, 2, (float) (t * z * z + c));
    return m;
  }

  public static OpenGLMatrix rotation(AxesReference axesReference, AxesOrder axesOrder, AngleUnit angleUnit, float first,
      float second, float third) {
    return Orientation.getRotationMatrix(axesReference, axesOrder, angleUnit, first, second, third);
  }

  public static OpenGLMatrix translation(float dx, float dy, float dz) {
    OpenGLMatrix m = new OpenGLMatrix();
    m.put(0, 3, dx);
    m.put(1, 3, dy);
    m.put(2, 3, dz);
    return m;
  }

  public static OpenGLMatrix identityMatrix() {
    return new OpenGLMatrix();
  }

  @Override
  public float[] getData() {
    return data;
  }

  public void scale(float scaleX, float scaleY, float scaleZ) {
    multiply(scaledMatrix(scaleX, scaleY, scaleZ));
  }

  public void scale(float scale) {
    scale(scale, scale, scale);
  }

  public void translate(float dx, float dy, float dz) {
    multiply(translation(dx, dy, dz));
  }

  public void rotate(AngleUnit angleUnit, float angle, float dx, float dy, float dz) {
    multiply(rotation(angleUnit, angle, dx, dy, dz));
  }

  public void rotate(AxesReference axesReference, AxesOrder axesOrder, AngleUnit angleUnit, float first, float second,
      float third) {
    multiply(rotation(axesReference, axesOrder, angleUnit, first, second, third));
  }

  public OpenGLMatrix scaled(float scaleX, float scaleY, float scaleZ) {
    return multiplied(scaledMatrix(scaleX, scaleY, scaleZ));
  }

  public OpenGLMatrix scaled(float scale) {
    return scaled(scale, scale, scale);
  }

  public OpenGLMatrix translated(float dx, float dy, float dz) {
    return multiplied(translation(dx, dy, dz));
  }

  public OpenGLMatrix rotated(AngleUnit angleUnit, float angle, float dx, float dy, float dz) {
    return multiplied(rotation(angleUnit, angle, dx, dy, dz));
  }

  public OpenGLMatrix rotated(AxesReference axesReference, AxesOrder axesOrder, AngleUnit angleUnit, float first,
      float second, float third) {
    return multiplied(rotation(axesReference, axesOrder, angleUnit, first, second, third));
  }

  @Override
  public OpenGLMatrix inverted() {
    MatrixF inverse = super.inverted();
    return inverse instanceof OpenGLMatrix ? (OpenGLMatrix) inverse : new OpenGLMatrix(inverse);
  }

  @Override
  public OpenGLMatrix transposed() {
    OpenGLMatrix t = new OpenGLMatrix(new float[16]);
    for (int r = 0; r < 4; r++) {
      for (int c = 0; c < 4; c++) {
        t.put(c, r, get(r, c));
      }
    }
    return t;
  }

  public OpenGLMatrix multiplied(OpenGLMatrix him) {
    OpenGLMatrix result = new OpenGLMatrix(new float[16]);
    for (int r = 0; r < 4; r++) {
      for (int c = 0; c < 4; c++) {
        float sum = 0;
        for (int k = 0; k < 4; k++) {
          sum += get(r, k) * him.get(k, c);
        }
        result.put(r, c, sum);
      }
    }
    return result;
  }

  @Override
  public MatrixF multiplied(MatrixF him) {
    if (him instanceof OpenGLMatrix) {
      return multiplied((OpenGLMatrix) him);
    }
    return super.multiplied(him);
  }

  public void multiply(OpenGLMatrix him) {
    System.arraycopy(multiplied(him).data, 0, data, 0, 16);
  }

  @Override
  public void multiply(MatrixF him) {
    if (him instanceof OpenGLMatrix) {
      multiply((OpenGLMatrix) him);
    } else {
      super.multiply(him);
    }
  }

  private static OpenGLMatrix scaledMatrix(float sx, float sy, float sz) {
    OpenGLMatrix m = new OpenGLMatrix();
    m.put(0, 0, sx);
    m.put(1, 1, sy);
    m.put(2, 2, sz);
    return m;
  }
}
