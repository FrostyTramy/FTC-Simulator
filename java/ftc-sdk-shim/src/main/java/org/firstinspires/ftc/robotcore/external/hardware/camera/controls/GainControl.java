package org.firstinspires.ftc.robotcore.external.hardware.camera.controls;

/** Webcam gain (SDK 12.0 public API). */
public interface GainControl extends CameraControl {
  int getMinGain();

  int getMaxGain();

  int getGain();

  boolean setGain(int gain);
}
