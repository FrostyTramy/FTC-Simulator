package com.qualcomm.robotcore.hardware;

/** A device whose I2C address can be changed (SDK 12.0 public API). */
public interface I2cAddrConfig extends I2cAddressableDevice {
  void setI2cAddress(I2cAddr newAddress);
}
