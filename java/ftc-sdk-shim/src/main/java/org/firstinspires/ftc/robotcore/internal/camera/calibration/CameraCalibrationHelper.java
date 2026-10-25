package org.firstinspires.ftc.robotcore.internal.camera.calibration;

import java.util.ArrayList;
import java.util.List;
import org.firstinspires.ftc.robotcore.external.android.util.Size;

/**
 * Finds the built-in calibration of a camera (SDK 12.0 API). In the simulator every webcam model carries its
 * calibrations (config/sensors.yaml), registered here when the camera is configured.
 */
public class CameraCalibrationHelper {
  public static final String TAG = "CameraCalibrationHelper";
  protected static CameraCalibrationHelper instance;
  private final List<CameraCalibration> calibrations = new ArrayList<>();

  protected CameraCalibrationHelper() {}

  public static synchronized CameraCalibrationHelper getInstance() {
    if (instance == null) {
      instance = new CameraCalibrationHelper();
    }
    return instance;
  }

  /** Simulator: makes a camera's built-in calibration known. */
  public synchronized void register(CameraCalibration calibration) {
    calibrations.removeIf(c -> c.getIdentity().equals(calibration.getIdentity()) && c.getSize().equals(calibration.getSize()));
    calibrations.add(calibration);
  }

  /**
   * The calibration for a size: exact, else one of the same aspect ratio scaled, else a placeholder telling the
   * aspect ratio does not match, else a degenerate "unavailable" calibration (as the SDK does).
   */
  public synchronized CameraCalibration getCalibration(CameraCalibrationIdentity identity, int width, int height) {
    Size size = new Size(width, height);
    List<CameraCalibration> mine = getCalibrations(identity);
    for (CameraCalibration c : mine) {
      if (c.getSize().equals(size)) {
        return c;
      }
    }
    for (CameraCalibration c : mine) {
      if (Math.abs(c.getAspectRatio() - (double) width / height) < 1e-3) {
        return c.scaledTo(size);
      }
    }
    if (!mine.isEmpty()) {
      return new PlaceholderCalibratedAspectRatioMismatch(identity, size);
    }
    return CameraCalibration.forUnavailable(identity, size);
  }

  public synchronized List<CameraCalibration> getCalibrations(CameraCalibrationIdentity identity) {
    List<CameraCalibration> out = new ArrayList<>();
    for (CameraCalibration c : calibrations) {
      if (c.getIdentity().equals(identity)) {
        out.add(c);
      }
    }
    return out;
  }
}
