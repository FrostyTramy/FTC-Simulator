package com.qualcomm.robotcore.hardware;

/** A sensor that measures reflected light (SDK 12.0 public API). */
public interface LightSensor extends HardwareDevice {
  double getLightDetected();

  double getRawLightDetected();

  double getRawLightDetectedMax();

  void enableLed(boolean enable);

  String status();
}
