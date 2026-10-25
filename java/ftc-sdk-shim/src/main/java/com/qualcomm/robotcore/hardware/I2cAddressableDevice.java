package com.qualcomm.robotcore.hardware;

/** A device with an I2C address (SDK 12.0 public API). */
public interface I2cAddressableDevice {
  I2cAddr getI2cAddress();
}
