package org.firstinspires.ftc.robotcore.external.hardware.camera.controls;

/** Webcam pan, tilt and zoom (SDK 12.0 public API). */
public interface PtzControl extends CameraControl {
  class PanTiltHolder {
    public int pan;
    public int tilt;

    public PanTiltHolder() {}
  }

  PanTiltHolder getPanTilt();

  boolean setPanTilt(PanTiltHolder panTiltHolder);

  PanTiltHolder getMinPanTilt();

  PanTiltHolder getMaxPanTilt();

  int getZoom();

  boolean setZoom(int zoom);

  int getMinZoom();

  int getMaxZoom();
}
