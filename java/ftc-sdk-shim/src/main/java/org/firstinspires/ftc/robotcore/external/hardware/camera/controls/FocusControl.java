package org.firstinspires.ftc.robotcore.external.hardware.camera.controls;

/** Webcam focus (SDK 12.0 public API). */
public interface FocusControl extends CameraControl {
  double unknownFocusLength = -1.0;

  enum Mode {
    Unknown,
    Auto,
    ContinuousAuto,
    Macro,
    Infinity,
    Fixed
  }

  Mode getMode();

  boolean setMode(Mode mode);

  boolean isModeSupported(Mode mode);

  double getMinFocusLength();

  double getMaxFocusLength();

  double getFocusLength();

  boolean setFocusLength(double focusLength);

  boolean isFocusLengthSupported();
}
