package org.firstinspires.ftc.robotcore.external.matrices;

/** A matrix backed by one float array (SDK 12.0 public API). */
public abstract class DenseMatrixF extends MatrixF {
  protected DenseMatrixF(int numRows, int numCols) {
    super(numRows, numCols);
  }

  @Override
  public float get(int row, int col) {
    return getData()[indexFromRowCol(row, col)];
  }

  @Override
  public void put(int row, int col, float value) {
    getData()[indexFromRowCol(row, col)] = value;
  }

  public abstract float[] getData();

  protected abstract int indexFromRowCol(int row, int col);
}
