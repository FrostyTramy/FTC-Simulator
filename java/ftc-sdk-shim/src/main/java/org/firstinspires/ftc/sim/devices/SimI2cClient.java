package org.firstinspires.ftc.sim.devices;

import com.qualcomm.hardware.lynx.LynxI2cDeviceSynch;
import com.qualcomm.robotcore.hardware.I2cAddr;

/**
 * The I2C client a simulated sensor driver is built on. The simulator's drivers talk to their {@link SimI2cDevice};
 * raw register access works where the device has a {@link RegisterMap} (a team's own copy of the goBILDA Pinpoint
 * driver, D-039); elsewhere a team's own I2C driver gets a clear error instead of made-up data.
 */
public final class SimI2cClient implements LynxI2cDeviceSynch {
  private final SimI2cDevice device;
  private final RegisterMap registers;
  private I2cAddr address;
  private String userName;
  private BusSpeed busSpeed = BusSpeed.STANDARD_100K;

  public SimI2cClient(SimI2cDevice device, I2cAddr address) {
    this(device, address, null);
  }

  public SimI2cClient(SimI2cDevice device, I2cAddr address, RegisterMap registers) {
    this.device = device;
    this.address = address;
    this.registers = registers;
    this.userName = device.name();
  }

  @Override
  public void setBusSpeed(BusSpeed speed) {
    busSpeed = speed;
  }

  public BusSpeed busSpeed() {
    return busSpeed;
  }

  public SimI2cDevice device() {
    return device;
  }

  private static UnsupportedOperationException noRegisters() {
    return new UnsupportedOperationException("raw I2C register access is not simulated; use the device's SDK driver class");
  }

  @Override
  public byte read8(int ireg) {
    return read(ireg, 1)[0];
  }

  @Override
  public byte[] read(int ireg, int creg) {
    if (registers == null) {
      throw noRegisters();
    }
    return registers.read(device, ireg, creg);
  }

  @Override
  public void write8(int ireg, int bVal) {
    write(ireg, new byte[] {(byte) bVal});
  }

  @Override
  public void write(int ireg, byte[] data) {
    if (registers == null) {
      throw noRegisters();
    }
    registers.write(device, ireg, data);
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
