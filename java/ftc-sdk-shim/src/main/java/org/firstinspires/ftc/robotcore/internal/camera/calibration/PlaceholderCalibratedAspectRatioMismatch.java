package org.firstinspires.ftc.robotcore.internal.camera.calibration;

import org.firstinspires.ftc.robotcore.external.android.util.Size;

/** Placeholder when the camera has calibrations, but none for the requested aspect ratio (SDK 12.0 API). */
public class PlaceholderCalibratedAspectRatioMismatch extends CameraCalibration {
  public PlaceholderCalibratedAspectRatioMismatch(CameraCalibrationIdentity identity, Size size) {
    super(identity, size, 0, 0, 0, 0, new float[8], false, true);
  }
}
