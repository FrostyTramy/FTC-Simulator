package org.firstinspires.ftc.robotcore.external.hardware.camera;

import android.util.Size;
import java.util.List;

/** Formats and sizes a camera supports (SDK 12.0 public API, subset). */
public interface CameraCharacteristics {
  /** Frame sizes the camera can stream. */
  List<Size> getSizes();

  /** Default frame size. */
  Size getDefaultSize();

  /** Highest frame rate for a size, frames per second. */
  int getMaxFramesPerSecond(Size size);
}
