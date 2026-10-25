package com.qualcomm.hardware.broadcom;

import com.qualcomm.robotcore.hardware.ColorSensor;
import com.qualcomm.robotcore.hardware.I2cAddr;
import com.qualcomm.robotcore.hardware.NormalizedColorSensor;

/** Broadcom APDS-9151 colour/proximity sensor (SDK 12.0 public API, subset: no raw register access). */
public interface BroadcomColorSensor extends ColorSensor, NormalizedColorSensor {
  I2cAddr BROADCOM_APDS9151_ADDRESS = I2cAddr.create7bit(0x52);
  byte BROADCOM_APDS9151_ID = (byte) 0xC2;

  byte getDeviceID();
}
