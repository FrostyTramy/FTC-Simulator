package com.qualcomm.robotcore.hardware;

/** A colour sensor reporting raw channel counts (SDK 12.0 public API). */
public interface ColorSensor extends HardwareDevice {
  int red();

  int green();

  int blue();

  int alpha();

  int argb();

  void enableLed(boolean enable);

  void setI2cAddress(I2cAddr newAddress);

  I2cAddr getI2cAddress();
}
