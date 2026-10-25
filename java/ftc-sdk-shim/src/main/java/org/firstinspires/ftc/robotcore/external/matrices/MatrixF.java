package org.firstinspires.ftc.robotcore.external.matrices;

import java.util.Locale;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.AxesOrder;
import org.firstinspires.ftc.robotcore.external.navigation.AxesReference;
import org.firstinspires.ftc.robotcore.external.navigation.Orientation;

/**
 * A matrix of floats (SDK 12.0 public API). Products are ordinary matrix products ({@code a.multiplied(b)} = a·b);
 * {@link #transform} and {@link #getTranslation} treat 4×4 matrices as homogeneous transforms.
 */
public abstract class MatrixF {
  protected int numRows;
  protected int numCols;

  public MatrixF(int numRows, int numCols) {
    if (numRows <= 0 || numCols <= 0) {
      throw dimensionsError(numRows, numCols);
    }
    this.numRows = numRows;
    this.numCols = numCols;
  }

  public SliceMatrixF slice(int row, int col, int numRows, int numCols) {
    return new SliceMatrixF(this, row, col, numRows, numCols);
  }

  public SliceMatrixF slice(int numRows, int numCols) {
    return slice(0, 0, numRows, numCols);
  }

  public static MatrixF identityMatrix(int dim) {
    return diagonalMatrix(dim, 1f);
  }

  public static MatrixF diagonalMatrix(int dim, float scale) {
    GeneralMatrixF m = new GeneralMatrixF(dim, dim);
    for (int i = 0; i < dim; i++) {
      m.put(i, i, scale);
    }
    return m;
  }

  public static MatrixF diagonalMatrix(VectorF vector) {
    int dim = vector.length();
    GeneralMatrixF m = new GeneralMatrixF(dim, dim);
    for (int i = 0; i < dim; i++) {
      m.put(i, i, vector.get(i));
    }
    return m;
  }

  /** A new zero matrix of the same kind as this one. */
  public abstract MatrixF emptyMatrix(int numRows, int numCols);

  public int numRows() {
    return numRows;
  }

  public int numCols() {
    return numCols;
  }

  public abstract float get(int row, int col);

  public abstract void put(int row, int col, float value);

  public VectorF getRow(int row) {
    VectorF v = VectorF.length(numCols);
    for (int c = 0; c < numCols; c++) {
      v.put(c, get(row, c));
    }
    return v;
  }

  public VectorF getColumn(int col) {
    VectorF v = VectorF.length(numRows);
    for (int r = 0; r < numRows; r++) {
      v.put(r, get(r, col));
    }
    return v;
  }

  @Override
  public String toString() {
    StringBuilder s = new StringBuilder();
    for (int r = 0; r < numRows; r++) {
      s.append(r == 0 ? "{" : " ");
      for (int c = 0; c < numCols; c++) {
        s.append(String.format(Locale.US, "%s%7.3f", c == 0 ? "" : " ", get(r, c)));
      }
      s.append(r == numRows - 1 ? "}" : "\n");
    }
    return s.toString();
  }

  /** M·v; a 3-vector against a 4×4 matrix is taken as a point (w = 1) and the result divided by w. */
  public VectorF transform(VectorF vector) {
    return multiplied(adaptHomogeneous(vector)).normalized3D();
  }

  protected VectorF adaptHomogeneous(VectorF vector) {
    if (numCols == 4 && vector.length() == 3) {
      return new VectorF(vector.get(0), vector.get(1), vector.get(2), 1f);
    }
    return vector;
  }

  public String formatAsTransform() {
    return formatAsTransform(AxesReference.EXTRINSIC, AxesOrder.XYZ, AngleUnit.DEGREES);
  }

  public String formatAsTransform(AxesReference reference, AxesOrder order, AngleUnit unit) {
    Orientation o = Orientation.getOrientation(this, reference, order, unit);
    VectorF t = getTranslation();
    return String.format(Locale.US, "%s %s", o, t);
  }

  public MatrixF transposed() {
    MatrixF t = emptyMatrix(numCols, numRows);
    for (int r = 0; r < numRows; r++) {
      for (int c = 0; c < numCols; c++) {
        t.put(c, r, get(r, c));
      }
    }
    return t;
  }

  /** this = this·m. */
  public void multiply(MatrixF m) {
    MatrixF p = multiplied(m);
    copyFrom(p);
  }

  public MatrixF multiplied(MatrixF him) {
    if (numCols != him.numRows) {
      throw dimensionsError(him.numRows, him.numCols);
    }
    MatrixF result = emptyMatrix(numRows, him.numCols);
    for (int r = 0; r < numRows; r++) {
      for (int c = 0; c < him.numCols; c++) {
        float sum = 0;
        for (int k = 0; k < numCols; k++) {
          sum += get(r, k) * him.get(k, c);
        }
        result.put(r, c, sum);
      }
    }
    return result;
  }

  public MatrixF multiplied(float scale) {
    MatrixF result = emptyMatrix(numRows, numCols);
    for (int r = 0; r < numRows; r++) {
      for (int c = 0; c < numCols; c++) {
        result.put(r, c, get(r, c) * scale);
      }
    }
    return result;
  }

  public void multiply(float scale) {
    copyFrom(multiplied(scale));
  }

  public VectorF multiplied(VectorF him) {
    if (numCols != him.length()) {
      throw dimensionsError(him.length(), 1);
    }
    VectorF result = VectorF.length(numRows);
    for (int r = 0; r < numRows; r++) {
      float sum = 0;
      for (int k = 0; k < numCols; k++) {
        sum += get(r, k) * him.get(k);
      }
      result.put(r, sum);
    }
    return result;
  }

  public void multiply(VectorF him) {
    VectorF v = multiplied(him);
    for (int r = 0; r < v.length(); r++) {
      put(r, 0, v.get(r));
    }
  }

  public VectorF multiplied(float[] him) {
    return multiplied(new VectorF(him));
  }

  public void multiply(float[] him) {
    multiply(new VectorF(him));
  }

  /** The matrix as a vector (one column or one row). */
  public VectorF toVector() {
    if (numCols == 1) {
      return getColumn(0);
    }
    if (numRows == 1) {
      return getRow(0);
    }
    throw dimensionsError();
  }

  public MatrixF added(MatrixF addend) {
    checkSame(addend);
    MatrixF result = emptyMatrix(numRows, numCols);
    for (int r = 0; r < numRows; r++) {
      for (int c = 0; c < numCols; c++) {
        result.put(r, c, get(r, c) + addend.get(r, c));
      }
    }
    return result;
  }

  public void add(MatrixF addend) {
    copyFrom(added(addend));
  }

  public MatrixF subtracted(MatrixF subtrahend) {
    return added(subtrahend.multiplied(-1f));
  }

  public void subtract(MatrixF subtrahend) {
    copyFrom(subtracted(subtrahend));
  }

  public MatrixF added(VectorF addend) {
    return added(columnOf(addend));
  }

  public MatrixF added(float[] addend) {
    return added(new VectorF(addend));
  }

  public MatrixF subtracted(VectorF subtrahend) {
    return subtracted(columnOf(subtrahend));
  }

  public MatrixF subtracted(float[] subtrahend) {
    return subtracted(new VectorF(subtrahend));
  }

  public void add(VectorF addend) {
    add(columnOf(addend));
  }

  public void add(float[] addend) {
    add(new VectorF(addend));
  }

  public void subtract(VectorF subtrahend) {
    subtract(columnOf(subtrahend));
  }

  public void subtract(float[] subtrahend) {
    subtract(new VectorF(subtrahend));
  }

  /** Translation part of a homogeneous transform: the last column divided by its last element. */
  public VectorF getTranslation() {
    return getColumn(3).normalized3D();
  }

  protected RuntimeException dimensionsError() {
    return dimensionsError(numRows, numCols);
  }

  protected static RuntimeException dimensionsError(int numRows, int numCols) {
    return new IllegalArgumentException(String.format(Locale.US, "matrix dimensions are incorrect: rows=%d cols=%d", numRows,
        numCols));
  }

  /** Inverse by Gauss-Jordan elimination with partial pivoting (square, invertible matrices). */
  public MatrixF inverted() {
    if (numRows != numCols) {
      throw dimensionsError();
    }
    int n = numRows;
    double[][] a = new double[n][2 * n];
    for (int r = 0; r < n; r++) {
      for (int c = 0; c < n; c++) {
        a[r][c] = get(r, c);
      }
      a[r][n + r] = 1;
    }
    for (int col = 0; col < n; col++) {
      int pivot = col;
      for (int r = col + 1; r < n; r++) {
        if (Math.abs(a[r][col]) > Math.abs(a[pivot][col])) {
          pivot = r;
        }
      }
      if (Math.abs(a[pivot][col]) < 1e-12) {
        throw new IllegalArgumentException("matrix is not invertible");
      }
      double[] tmp = a[col];
      a[col] = a[pivot];
      a[pivot] = tmp;
      double d = a[col][col];
      for (int c = 0; c < 2 * n; c++) {
        a[col][c] /= d;
      }
      for (int r = 0; r < n; r++) {
        if (r != col && a[r][col] != 0) {
          double f = a[r][col];
          for (int c = 0; c < 2 * n; c++) {
            a[r][c] -= f * a[col][c];
          }
        }
      }
    }
    MatrixF result = emptyMatrix(n, n);
    for (int r = 0; r < n; r++) {
      for (int c = 0; c < n; c++) {
        result.put(r, c, (float) a[r][n + c]);
      }
    }
    return result;
  }

  private MatrixF columnOf(VectorF v) {
    GeneralMatrixF m = new GeneralMatrixF(v.length(), 1);
    for (int i = 0; i < v.length(); i++) {
      m.put(i, 0, v.get(i));
    }
    return m;
  }

  private void checkSame(MatrixF him) {
    if (numRows != him.numRows || numCols != him.numCols) {
      throw dimensionsError(him.numRows, him.numCols);
    }
  }

  private void copyFrom(MatrixF m) {
    if (m.numRows != numRows || m.numCols != numCols) {
      throw dimensionsError(m.numRows, m.numCols);
    }
    for (int r = 0; r < numRows; r++) {
      for (int c = 0; c < numCols; c++) {
        put(r, c, m.get(r, c));
      }
    }
  }
}
