package com.qualcomm.hardware.stmicroelectronics;

import com.qualcomm.robotcore.hardware.DistanceSensor;
import com.qualcomm.robotcore.hardware.I2cAddr;
import com.qualcomm.robotcore.hardware.I2cDeviceSynch;
import com.qualcomm.robotcore.hardware.I2cDeviceSynchDevice;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.sim.devices.SimI2cClient;
import org.firstinspires.ftc.sim.devices.SimI2cDevice;

/**
 * ST VL53L0X time-of-flight sensor (SDK 12.0 public API). getDistance is one I2C read of the latest range; beyond the
 * range the sensor reports its out-of-range code (simulated in sim/Sim.Sensors, DistanceSensorDevice).
 */
public class VL53L0X extends I2cDeviceSynchDevice<I2cDeviceSynch> implements DistanceSensor {
  public static final I2cAddr ADDRESS_I2C_DEFAULT = I2cAddr.create8bit(0x52);
  private static final int I_RANGE_MM = 0, I_MODEL_ID = 1, I_TIMEOUT = 2;
  protected final SimI2cDevice device;
  long measurement_timing_budget_us = 33_000;
  boolean did_timeout;
  boolean assume_uninitialized = true;

  public VL53L0X(I2cDeviceSynch deviceClient, boolean deviceClientIsOwned) {
    super(deviceClient, deviceClientIsOwned);
    if (!(deviceClient instanceof SimI2cClient)) {
      throw new IllegalArgumentException("VL53L0X needs a simulated I2C device from the hardware map");
    }
    device = ((SimI2cClient) deviceClient).device();
  }

  @Override
  protected boolean doInitialize() {
    return true;
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.STMicroelectronics;
  }

  @Override
  public String getDeviceName() {
    return "VL53L0X";
  }

  public byte getModelID() {
    return (byte) (int) device.read()[I_MODEL_ID];
  }

  @Override
  public double getDistance(DistanceUnit unit) {
    double[] r = device.read();
    did_timeout = r[I_TIMEOUT] != 0;
    return unit.fromMm(r[I_RANGE_MM]);
  }

  public boolean didTimeoutOccur() {
    return did_timeout;
  }

  long getMeasurementTimingBudget() {
    return measurement_timing_budget_us;
  }
}
