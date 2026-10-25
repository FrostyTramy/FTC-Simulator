package com.qualcomm.robotcore.hardware;

/** A touch sensor (SDK 12.0 public API). */
public interface TouchSensor extends HardwareDevice {
  double getValue();

  boolean isPressed();
}
