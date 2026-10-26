package com.qualcomm.robotcore.hardware;

/** An I2C device client with read windows in the real SDK (SDK 12.0 public API, subset). */
public interface I2cDeviceSynch extends I2cDeviceSynchSimple {
  /** Engagable: the simulated bus is always engaged. */
  default void engage() {}

  default void disengage() {}

  default boolean isEngaged() {
    return true;
  }
}
