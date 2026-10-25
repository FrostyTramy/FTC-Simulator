package org.firstinspires.ftc.robotcore.external.matrices;

/** A rectangular window onto another matrix (SDK 12.0 public API); writes go through to it. */
public class SliceMatrixF extends MatrixF {
  protected MatrixF matrix;
  protected int row;
  protected int col;

  public SliceMatrixF(MatrixF matrix, int row, int col, int numRows, int numCols) {
    super(numRows, numCols);
    if (row + numRows > matrix.numRows || col + numCols > matrix.numCols) {
      throw dimensionsError(numRows, numCols);
    }
    this.matrix = matrix;
    this.row = row;
    this.col = col;
  }

  @Override
  public float get(int r, int c) {
    return matrix.get(row + r, col + c);
  }

  @Override
  public void put(int r, int c, float value) {
    matrix.put(row + r, col + c, value);
  }

  @Override
  public MatrixF emptyMatrix(int numRows, int numCols) {
    return matrix.emptyMatrix(numRows, numCols);
  }
}
