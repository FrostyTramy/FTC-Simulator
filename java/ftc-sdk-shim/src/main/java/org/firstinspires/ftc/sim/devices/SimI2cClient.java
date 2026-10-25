package org.firstinspires.ftc.sim.devices;

import com.qualcomm.robotcore.hardware.I2cAddr;
import com.qualcomm.robotcore.hardware.I2cDeviceSynch;

/**
 * The I2C client a simulated sensor driver is built on. Drivers talk to their {@link SimI2cDevice}; raw register access
 * is not simulated (a team's own I2C driver gets a clear error instead of made-up data).
 */
public final class SimI2cClient implements I2cDeviceSynch {
  private final SimI2cDevice device;
  private I2cAddr address;
  private String userName;

  public SimI2cClient(SimI2cDevice device, I2cAddr address) {
    this.device = device;
    this.address = address;
    this.userName = device.name();
  }

  public SimI2cDevice device() {
    return device;
  }

  private static UnsupportedOperationException noRegisters() {
    return new UnsupportedOperationException("raw I2C register access is not simulated; use the device's SDK driver class");
  }

  @Override
  public byte read8(int ireg) {
    throw noRegisters();
  }

  @Override
  public byte[] read(int ireg, int creg) {
    throw noRegisters();
  }

  @Override
  public void write8(int ireg, int bVal) {
    throw noRegisters();
  }

  @Override
  public void write(int ireg, byte[] data) {
    throw noRegisters();
  }

  @Override
  public boolean isArmed() {
    return true;
  }

  @Override
  public void setUserConfiguredName(String name) {
    userName = name;
  }

  @Override
  public String getUserConfiguredName() {
    return userName;
  }

  @Override
  public I2cAddr getI2cAddress() {
    return address;
  }

  @Override
  public void setI2cAddress(I2cAddr newAddress) {
    address = newAddress;
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "Lynx I2C Device";
  }

  @Override
  public String getConnectionInfo() {
    return device.connectionInfo();
  }

  @Override
  public int getVersion() {
    return 1;
  }

  @Override
  public void resetDeviceConfigurationForOpMode() {}

  @Override
  public void close() {}
}
