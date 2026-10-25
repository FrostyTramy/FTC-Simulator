package org.firstinspires.ftc.robotcore.internal.camera.calibration;

import java.util.Locale;
import org.firstinspires.ftc.robotcore.external.android.util.Size;

/** A camera calibration for one frame size (SDK 12.0 API). */
public class CameraCalibration extends CameraIntrinsics implements Cloneable {
  protected CameraCalibrationIdentity identity;
  protected Size size;
  protected boolean remove;
  protected final boolean isFake;
  public Size resolutionScaledFrom;

  public CameraCalibration(CameraCalibrationIdentity identity, Size size, float focalLengthX, float focalLengthY,
      float principalPointX, float principalPointY, float[] distortionCoefficients, boolean remove, boolean isFake)
      throws RuntimeException {
    super(focalLengthX, focalLengthY, principalPointX, principalPointY, distortionCoefficients);
    this.identity = identity;
    this.size = size;
    this.remove = remove;
    this.isFake = isFake;
  }

  public CameraCalibration(CameraCalibrationIdentity identity, int[] size, float[] focalLength, float[] principalPoint,
      float[] distortionCoefficients, boolean remove, boolean isFake) throws RuntimeException {
    this(identity, new Size(size[0], size[1]), focalLength[0], focalLength[1], principalPoint[0], principalPoint[1],
        distortionCoefficients, remove, isFake);
  }

  /** A placeholder for a camera without a calibration (degenerate intrinsics). */
  public static CameraCalibration forUnavailable(CameraCalibrationIdentity identity, Size size) {
    return new CameraCalibration(identity, size, 0, 0, 0, 0, new float[8], false, true);
  }

  @Override
  public String toString() {
    return String.format(Locale.US, "CameraCalibration(%s %dx%d f=%.3f,%.3f c=%.3f,%.3f)", identity, size.getWidth(),
        size.getHeight(), focalLengthX, focalLengthY, principalPointX, principalPointY);
  }

  public CameraCalibrationIdentity getIdentity() {
    return identity;
  }

  public Size getSize() {
    return size;
  }

  public boolean getRemove() {
    return remove;
  }

  public boolean isFake() {
    return isFake;
  }

  protected CameraCalibration memberwiseClone() {
    try {
      return (CameraCalibration) super.clone();
    } catch (CloneNotSupportedException e) {
      throw new RuntimeException(e);
    }
  }

  /** The same calibration for another frame size of the same aspect ratio. */
  public CameraCalibration scaledTo(Size newSize) {
    double scale = getDiagonal(newSize) / getDiagonal();
    CameraCalibration c = new CameraCalibration(identity, newSize, (float) (focalLengthX * scale),
        (float) (focalLengthY * scale), (float) (principalPointX * scale), (float) (principalPointY * scale),
        distortionCoefficients, remove, isFake);
    c.resolutionScaledFrom = size;
    return c;
  }

  public double getAspectRatio() {
    return getAspectRatio(size);
  }

  public double getDiagonal() {
    return getDiagonal(size);
  }

  public static double getDiagonal(Size size) {
    return Math.hypot(size.getWidth(), size.getHeight());
  }

  protected static double getAspectRatio(Size size) {
    return (double) size.getWidth() / size.getHeight();
  }
}
