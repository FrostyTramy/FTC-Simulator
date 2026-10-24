package com.qualcomm.robotcore.hardware;

/** Battery voltage of a hub (SDK 12.0 public API). */
public interface VoltageSensor extends HardwareDevice {
  double getVoltage();
}
