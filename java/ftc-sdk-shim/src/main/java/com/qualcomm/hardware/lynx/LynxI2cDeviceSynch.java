package com.qualcomm.hardware.lynx;

import com.qualcomm.robotcore.hardware.I2cDeviceSynch;

/**
 * An I2C device on a REV hub's I2C bus (SDK 12.0 public API, subset; a class in the SDK). The simulator's I2C clients
 * are these, so drivers can cast their client to set the bus speed. The bus speed does not change the simulated I2C
 * timing (config/hardware_latency.yaml).
 */
public interface LynxI2cDeviceSynch extends I2cDeviceSynch {
  enum BusSpeed {
    STANDARD_100K,
    FAST_400K,
    FASTPLUS_1M,
    HIGH_3_4M
  }

  void setBusSpeed(BusSpeed speed);
}
