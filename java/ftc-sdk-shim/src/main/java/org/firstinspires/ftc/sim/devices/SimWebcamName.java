package org.firstinspires.ftc.sim.devices;

import android.content.Context;
import android.util.Size;
import com.qualcomm.robotcore.util.SerialNumber;
import java.util.List;
import java.util.Map;
import org.firstinspires.ftc.robotcore.external.function.Consumer;
import org.firstinspires.ftc.robotcore.external.function.Continuation;
import org.firstinspires.ftc.robotcore.external.hardware.camera.CameraCharacteristics;
import org.firstinspires.ftc.robotcore.external.hardware.camera.WebcamName;
import org.firstinspires.ftc.robotcore.internal.system.Deadline;

/**
 * A simulated USB webcam: the robot YAML's camera (pose, model) rendered by the simulator. The calibration and frame
 * size of its model (config/sensors.yaml) come in the CONFIGURE parameters, like the SDK's built-in calibrations.
 */
public final class SimWebcamName implements WebcamName {
  private final String name;
  private final Map<String, Double> params;

  public SimWebcamName(String name, Map<String, Double> params) {
    this.name = name;
    this.params = Map.copyOf(params);
  }

  /** A model value (width, height, fx, fy, cx, cy, fps), or {@code fallback}. */
  public double param(String key, double fallback) {
    return params.getOrDefault(key, fallback);
  }

  public String configName() {
    return name;
  }

  @Override
  public boolean isWebcam() {
    return true;
  }

  @Override
  public boolean isCameraDirection() {
    return false;
  }

  @Override
  public boolean isSwitchable() {
    return false;
  }

  @Override
  public boolean isUnknown() {
    return false;
  }

  @Override
  public void asyncRequestCameraPermission(Context context, Deadline deadline,
      Continuation<? extends Consumer<Boolean>> continuation) {
    continuation.dispatch(target -> target.accept(true));
  }

  @Override
  public boolean requestCameraPermission(Deadline deadline) {
    return true;
  }

  @Override
  public CameraCharacteristics getCameraCharacteristics() {
    Size size = new Size((int) param("width", 640), (int) param("height", 480));
    int fps = (int) param("fps", 30);
    return new CameraCharacteristics() {
      @Override
      public List<Size> getSizes() {
        return List.of(size);
      }

      @Override
      public Size getDefaultSize() {
        return size;
      }

      @Override
      public int getMaxFramesPerSecond(Size s) {
        return fps;
      }
    };
  }

  @Override
  public SerialNumber getSerialNumber() {
    return SerialNumber.fromString("Webcam:" + name);
  }

  @Override
  public String getUsbDeviceNameIfAttached() {
    return "/dev/bus/usb/001/" + Math.abs(name.hashCode() % 100);
  }

  @Override
  public boolean isAttached() {
    return true;
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Other;
  }

  @Override
  public String getDeviceName() {
    return "Webcam";
  }

  @Override
  public String getConnectionInfo() {
    return "USB " + name;
  }

  @Override
  public int getVersion() {
    return 1;
  }

  @Override
  public void resetDeviceConfigurationForOpMode() {}

  @Override
  public void close() {}
}
