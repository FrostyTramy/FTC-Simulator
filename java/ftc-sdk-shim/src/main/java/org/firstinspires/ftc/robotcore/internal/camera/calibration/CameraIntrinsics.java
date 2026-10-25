package org.firstinspires.ftc.robotcore.internal.camera.calibration;

/** Pinhole intrinsics in pixels and distortion coefficients (SDK 12.0 API). */
public class CameraIntrinsics {
  public float focalLengthX;
  public float focalLengthY;
  public float principalPointX;
  public float principalPointY;
  public final float[] distortionCoefficients;

  public CameraIntrinsics(float focalLengthX, float focalLengthY, float principalPointX, float principalPointY,
      float[] distortionCoefficients) throws RuntimeException {
    if (distortionCoefficients == null || distortionCoefficients.length != 8) {
      throw new IllegalArgumentException("distortionCoefficients must have 8 elements");
    }
    this.focalLengthX = focalLengthX;
    this.focalLengthY = focalLengthY;
    this.principalPointX = principalPointX;
    this.principalPointY = principalPointY;
    this.distortionCoefficients = distortionCoefficients.clone();
  }

  /** fx, fy, cx, cy, then the 8 distortion coefficients. */
  public float[] toArray() {
    float[] a = new float[12];
    a[0] = focalLengthX;
    a[1] = focalLengthY;
    a[2] = principalPointX;
    a[3] = principalPointY;
    System.arraycopy(distortionCoefficients, 0, a, 4, 8);
    return a;
  }

  public boolean isDegenerate() {
    return focalLengthX <= 0 || focalLengthY <= 0 || principalPointX <= 0 || principalPointY <= 0;
  }
}
