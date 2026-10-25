package com.qualcomm.hardware.rev;

import com.qualcomm.hardware.broadcom.BroadcomColorSensorImpl;
import com.qualcomm.robotcore.hardware.ColorRangeSensor;
import com.qualcomm.robotcore.hardware.DistanceSensor;
import com.qualcomm.robotcore.hardware.I2cDeviceSynchSimple;
import com.qualcomm.robotcore.hardware.OpticalDistanceSensor;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;

/**
 * REV Color Sensor V3 (SDK 12.0 public API). The distance comes from the proximity reading and saturates a few
 * centimetres out (simulated in sim/Sim.Sensors, max_distance ASSUMED).
 */
public class RevColorSensorV3 extends BroadcomColorSensorImpl
    implements DistanceSensor, OpticalDistanceSensor, ColorRangeSensor {
  protected static final double apiLevelMin = 0.0;
  protected static final double apiLevelMax = 1.0;
  private static final double RAW_OPTICAL_MAX = 2047;

  public RevColorSensorV3(I2cDeviceSynchSimple deviceClient, boolean deviceClientIsOwned) {
    super(deviceClient, deviceClientIsOwned);
  }

  @Override
  public String getDeviceName() {
    return "REV Color Sensor v3";
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Broadcom;
  }

  @Override
  public double getLightDetected() {
    return Math.max(apiLevelMin, Math.min(apiLevelMax, getRawLightDetected() / getRawLightDetectedMax()));
  }

  @Override
  public double getRawLightDetected() {
    return rawOptical();
  }

  @Override
  public double getRawLightDetectedMax() {
    return RAW_OPTICAL_MAX;
  }

  @Override
  public String status() {
    return String.format("%s on %s", getDeviceName(), getConnectionInfo());
  }

  @Override
  public double getDistance(DistanceUnit unit) {
    return unit.fromCm(device.read()[I_DISTANCE_CM]);
  }

  /** Inches for a raw proximity reading (inverse of the simulated proximity curve). */
  protected double inFromOptical(int rawOptical) {
    double maxCm = 6.0;
    double cm = maxCm * (1 - Math.sqrt(Math.max(0, Math.min(1, rawOptical / RAW_OPTICAL_MAX))));
    return DistanceUnit.INCH.fromCm(cm);
  }

  public int rawOptical() {
    return (int) device.read()[I_RAW_OPTICAL];
  }
}
