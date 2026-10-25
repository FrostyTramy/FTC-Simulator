package org.firstinspires.ftc.robotcore.external.matrices;

/** A dense matrix stored column by column, like OpenGL (SDK 12.0 public API). */
public abstract class ColumnMajorMatrixF extends DenseMatrixF {
  public ColumnMajorMatrixF(int numRows, int numCols) {
    super(numRows, numCols);
  }

  @Override
  protected int indexFromRowCol(int row, int col) {
    return col * numRows + row;
  }

  @Override
  public VectorF toVector() {
    return new VectorF(getData().clone());
  }
}
