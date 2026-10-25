package com.qualcomm.robotcore.hardware;

/**
 * Synchronous register access to an I2C device (SDK 12.0 public API, subset). The simulator models its sensors at the
 * driver level, so raw register access is only available where a simulated device supports it.
 */
public interface I2cDeviceSynchSimple extends HardwareDevice, I2cAddrConfig {
  byte read8(int ireg);

  byte[] read(int ireg, int creg);

  void write8(int ireg, int bVal);

  void write(int ireg, byte[] data);

  boolean isArmed();

  void setUserConfiguredName(String name);

  String getUserConfiguredName();
}
