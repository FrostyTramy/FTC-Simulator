package com.qualcomm.robotcore.hardware;

/** A colour sensor reporting channels scaled to 0..1 (SDK 12.0 public API). */
public interface NormalizedColorSensor extends HardwareDevice {
  NormalizedRGBA getNormalizedColors();

  float getGain();

  void setGain(float newGain);
}
