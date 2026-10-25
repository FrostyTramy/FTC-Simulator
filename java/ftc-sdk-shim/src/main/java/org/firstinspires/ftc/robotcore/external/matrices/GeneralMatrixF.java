package org.firstinspires.ftc.robotcore.external.matrices;

/** A row-major matrix of any size (SDK 12.0 public API). */
public class GeneralMatrixF extends RowMajorMatrixF {
  float[] data;

  public GeneralMatrixF(int numRows, int numCols) {
    super(numRows, numCols);
    data = new float[numRows * numCols];
  }

  /** {@code data} row by row (kept, not copied, as in the SDK). */
  public GeneralMatrixF(int numRows, int numCols, float[] data) {
    super(numRows, numCols);
    if (data.length != numRows * numCols) {
      throw dimensionsError(numRows, numCols);
    }
    this.data = data;
  }

  @Override
  public float[] getData() {
    return data;
  }

  @Override
  public MatrixF emptyMatrix(int numRows, int numCols) {
    return new GeneralMatrixF(numRows, numCols);
  }

  @Override
  public GeneralMatrixF transposed() {
    GeneralMatrixF t = new GeneralMatrixF(numCols, numRows);
    for (int r = 0; r < numRows; r++) {
      for (int c = 0; c < numCols; c++) {
        t.put(c, r, get(r, c));
      }
    }
    return t;
  }
}
