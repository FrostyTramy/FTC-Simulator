package com.qualcomm.hardware.broadcom;

import com.qualcomm.robotcore.hardware.I2cAddr;
import com.qualcomm.robotcore.hardware.I2cAddrConfig;
import com.qualcomm.robotcore.hardware.I2cDeviceSynchDevice;
import com.qualcomm.robotcore.hardware.I2cDeviceSynchSimple;
import com.qualcomm.robotcore.hardware.Light;
import com.qualcomm.robotcore.hardware.NormalizedRGBA;
import org.firstinspires.ftc.sim.devices.SimI2cClient;
import org.firstinspires.ftc.sim.devices.SimI2cDevice;

/**
 * Driver base of the Broadcom colour sensors (SDK 12.0 public API, subset). Each colour call is one I2C read of the
 * colour channels; the normalized colours are the raw counts times the software gain over the 16-bit full scale
 * (ASSUMED scale). The sensor itself is simulated in sim/Sim.Sensors (ColorSensorDevice); value indexes below.
 */
public abstract class BroadcomColorSensorImpl extends I2cDeviceSynchDevice<I2cDeviceSynchSimple>
    implements BroadcomColorSensor, I2cAddrConfig, Light {
  public static final String TAG = "BroadcomColorSensorImpl";
  protected static final int I_RED = 0, I_GREEN = 1, I_BLUE = 2, I_ALPHA = 3, I_DISTANCE_CM = 4, I_RAW_OPTICAL = 5;
  protected static final int I_DEVICE_ID = 6, I_LED = 7;
  private static final float FULL_SCALE = 65535f;

  protected final SimI2cDevice device;
  NormalizedRGBA colors = new NormalizedRGBA();
  int red;
  int green;
  int blue;
  int alpha;
  float softwareGain = 1f;

  protected BroadcomColorSensorImpl(I2cDeviceSynchSimple deviceClient, boolean deviceClientIsOwned) {
    super(deviceClient, deviceClientIsOwned);
    if (!(deviceClient instanceof SimI2cClient)) {
      throw new IllegalArgumentException("colour sensors need a simulated I2C device from the hardware map");
    }
    device = ((SimI2cClient) deviceClient).device();
  }

  @Override
  protected boolean doInitialize() {
    return true;
  }

  private void updateColors() {
    double[] r = device.read();
    red = (int) r[I_RED];
    green = (int) r[I_GREEN];
    blue = (int) r[I_BLUE];
    alpha = (int) r[I_ALPHA];
  }

  @Override
  public synchronized int red() {
    updateColors();
    return red;
  }

  @Override
  public synchronized int green() {
    updateColors();
    return green;
  }

  @Override
  public synchronized int blue() {
    updateColors();
    return blue;
  }

  @Override
  public synchronized int alpha() {
    updateColors();
    return alpha;
  }

  /** Android Color.argb of the raw counts, as the SDK builds it (no clipping to 8 bits). */
  @Override
  public synchronized int argb() {
    updateColors();
    return (alpha << 24) | (red << 16) | (green << 8) | blue;
  }

  @Override
  public void setGain(float newGain) {
    softwareGain = newGain;
  }

  @Override
  public float getGain() {
    return softwareGain;
  }

  @Override
  public NormalizedRGBA getNormalizedColors() {
    updateColors();
    colors.red = clip(red * softwareGain / FULL_SCALE);
    colors.green = clip(green * softwareGain / FULL_SCALE);
    colors.blue = clip(blue * softwareGain / FULL_SCALE);
    colors.alpha = clip(alpha * softwareGain / FULL_SCALE);
    return colors;
  }

  private static float clip(float v) {
    return Math.max(0f, Math.min(1f, v));
  }

  @Override
  public synchronized void enableLed(boolean enable) {
    device.write("led", new double[] {enable ? 1 : 0}, I_LED, enable ? 1 : 0);
  }

  @Override
  public boolean isLightOn() {
    return device.values()[I_LED] != 0;
  }

  @Override
  public byte getDeviceID() {
    return (byte) (int) device.read()[I_DEVICE_ID];
  }

  @Override
  public synchronized I2cAddr getI2cAddress() {
    return deviceClient.getI2cAddress();
  }

  @Override
  public synchronized void setI2cAddress(I2cAddr newAddress) {
    deviceClient.setI2cAddress(newAddress);
  }

  @Override
  public String getDeviceName() {
    return "Broadcom I2C Color Sensor";
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Broadcom;
  }
}
