package org.firstinspires.ftc.robotcore.external.matrices;

import java.util.Locale;

/** A vector of floats (SDK 12.0 public API). */
public class VectorF {
  protected float[] data;

  public static VectorF length(int length) {
    return new VectorF(new float[length]);
  }

  /** Wraps {@code data} (not copied, as in the SDK). */
  public VectorF(float[] data) {
    this.data = data;
  }

  public VectorF(float x) {
    this(new float[] {x});
  }

  public VectorF(float x, float y) {
    this(new float[] {x, y});
  }

  public VectorF(float x, float y, float z) {
    this(new float[] {x, y, z});
  }

  public VectorF(float x, float y, float z, float w) {
    this(new float[] {x, y, z, w});
  }

  public float[] getData() {
    return data;
  }

  public int length() {
    return data.length;
  }

  public float get(int index) {
    return data[index];
  }

  public void put(int index, float value) {
    data[index] = value;
  }

  @Override
  public String toString() {
    StringBuilder s = new StringBuilder("{");
    for (int i = 0; i < data.length; i++) {
      s.append(String.format(Locale.US, "%s%.2f", i == 0 ? "" : " ", data[i]));
    }
    return s.append("}").toString();
  }

  /** A 3D vector: as is for length 3, divided by w for a homogeneous 4-vector. */
  public VectorF normalized3D() {
    if (data.length == 3) {
      return new VectorF(data[0], data[1], data[2]);
    }
    if (data.length == 4) {
      return new VectorF(data[0] / data[3], data[1] / data[3], data[2] / data[3]);
    }
    throw dimensionsError();
  }

  public float magnitude() {
    return (float) Math.sqrt(dotProduct(this));
  }

  public float dotProduct(VectorF him) {
    if (him.length() != length()) {
      throw dimensionsError(him.length());
    }
    float sum = 0;
    for (int i = 0; i < data.length; i++) {
      sum += data[i] * him.data[i];
    }
    return sum;
  }

  /** This vector as a column times {@code him} (a 1-row matrix): the outer product. */
  public MatrixF multiplied(MatrixF him) {
    return asColumn().multiplied(him);
  }

  public MatrixF added(MatrixF addend) {
    return asColumn().added(addend);
  }

  public VectorF added(VectorF addend) {
    if (addend.length() != length()) {
      throw dimensionsError(addend.length());
    }
    VectorF r = VectorF.length(length());
    for (int i = 0; i < data.length; i++) {
      r.data[i] = data[i] + addend.data[i];
    }
    return r;
  }

  public void add(VectorF addend) {
    VectorF r = added(addend);
    System.arraycopy(r.data, 0, data, 0, data.length);
  }

  public MatrixF subtracted(MatrixF subtrahend) {
    return asColumn().subtracted(subtrahend);
  }

  public VectorF subtracted(VectorF subtrahend) {
    return added(subtrahend.multiplied(-1f));
  }

  public void subtract(VectorF subtrahend) {
    add(subtrahend.multiplied(-1f));
  }

  public VectorF multiplied(float scale) {
    VectorF r = VectorF.length(length());
    for (int i = 0; i < data.length; i++) {
      r.data[i] = data[i] * scale;
    }
    return r;
  }

  public void multiply(float scale) {
    for (int i = 0; i < data.length; i++) {
      data[i] *= scale;
    }
  }

  protected RuntimeException dimensionsError() {
    return dimensionsError(data.length);
  }

  protected static RuntimeException dimensionsError(int length) {
    return new IllegalArgumentException(String.format(Locale.US, "vector dimensions are incorrect: length=%d", length));
  }

  private MatrixF asColumn() {
    return new GeneralMatrixF(data.length, 1, data.clone());
  }
}
