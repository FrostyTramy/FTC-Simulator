package org.firstinspires.ftc.robotcore.external.matrices;

/** A dense matrix stored row by row (SDK 12.0 public API). */
public abstract class RowMajorMatrixF extends DenseMatrixF {
  public RowMajorMatrixF(int numRows, int numCols) {
    super(numRows, numCols);
  }

  @Override
  protected int indexFromRowCol(int row, int col) {
    return row * numCols + col;
  }

  @Override
  public VectorF toVector() {
    return new VectorF(getData().clone());
  }
}
