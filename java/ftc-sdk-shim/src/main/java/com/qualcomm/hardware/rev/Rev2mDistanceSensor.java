package com.qualcomm.hardware.rev;

import com.qualcomm.hardware.stmicroelectronics.VL53L0X;
import com.qualcomm.robotcore.hardware.I2cDeviceSynch;

/** REV 2m Distance Sensor, a VL53L0X (SDK 12.0 public API). */
public class Rev2mDistanceSensor extends VL53L0X {
  public Rev2mDistanceSensor(I2cDeviceSynch deviceClient, boolean deviceClientIsOwned) {
    super(deviceClient, deviceClientIsOwned);
  }

  @Override
  public String getDeviceName() {
    return "REV 2M ToF Distance Sensor";
  }
}
