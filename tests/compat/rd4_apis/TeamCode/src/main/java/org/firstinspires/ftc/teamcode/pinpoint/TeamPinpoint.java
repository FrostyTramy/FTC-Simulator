package org.firstinspires.ftc.teamcode.pinpoint;

import static com.qualcomm.robotcore.util.TypeConversion.byteArrayToInt;

import com.qualcomm.hardware.lynx.LynxI2cDeviceSynch;
import com.qualcomm.robotcore.hardware.I2cAddr;
import com.qualcomm.robotcore.hardware.I2cDeviceSynchDevice;
import com.qualcomm.robotcore.hardware.I2cDeviceSynchSimple;
import com.qualcomm.robotcore.hardware.configuration.annotations.DeviceProperties;
import com.qualcomm.robotcore.hardware.configuration.annotations.I2cDeviceType;
import com.qualcomm.robotcore.util.TypeConversion;
import java.nio.ByteBuffer;
import java.nio.ByteOrder;
import java.util.Arrays;

/** A team's own register-level Pinpoint driver, the way teams keep a copy of goBILDA's in TeamCode. */
@I2cDeviceType
@DeviceProperties(name = "Team Pinpoint", xmlTag = "teamPinpoint")
public class TeamPinpoint extends I2cDeviceSynchDevice<I2cDeviceSynchSimple> {
  public static final LynxI2cDeviceSynch.BusSpeed BUS_SPEED = LynxI2cDeviceSynch.BusSpeed.FAST_400K;
  private static final int DEVICE_ID = 1;
  private static final int DEVICE_CONTROL = 4;
  private static final int MM_PER_TICK = 14;
  private static final int X_POD_OFFSET = 15;
  private static final int Y_POD_OFFSET = 16;
  private static final int BULK_READ = 18;

  private int deviceStatus;
  private int loopTime;
  private int xEncoderValue;
  private int yEncoderValue;
  private float xPosition;
  private float yPosition;
  private float hOrientation;

  public TeamPinpoint(I2cDeviceSynchSimple deviceClient, boolean deviceClientIsOwned) {
    super(deviceClient, deviceClientIsOwned);
    this.deviceClient.setI2cAddress(I2cAddr.create7bit(0x31));
    super.registerArmingStateCallback(false);
  }

  @Override
  protected synchronized boolean doInitialize() {
    ((LynxI2cDeviceSynch) (deviceClient)).setBusSpeed(BUS_SPEED);
    return true;
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Other;
  }

  @Override
  public String getDeviceName() {
    return "Team Pinpoint";
  }

  private void writeInt(int register, int value) {
    deviceClient.write(register, TypeConversion.intToByteArray(value, ByteOrder.LITTLE_ENDIAN));
  }

  private void writeFloat(int register, float value) {
    deviceClient.write(register, ByteBuffer.allocate(4).order(ByteOrder.LITTLE_ENDIAN).putFloat(value).array());
  }

  private static float toFloat(byte[] bytes) {
    return ByteBuffer.wrap(bytes).order(ByteOrder.LITTLE_ENDIAN).getFloat();
  }

  public int getDeviceID() {
    return byteArrayToInt(deviceClient.read(DEVICE_ID, 4), ByteOrder.LITTLE_ENDIAN);
  }

  public void update() {
    byte[] b = deviceClient.read(BULK_READ, 40);
    deviceStatus = byteArrayToInt(Arrays.copyOfRange(b, 0, 4), ByteOrder.LITTLE_ENDIAN);
    loopTime = byteArrayToInt(Arrays.copyOfRange(b, 4, 8), ByteOrder.LITTLE_ENDIAN);
    xEncoderValue = byteArrayToInt(Arrays.copyOfRange(b, 8, 12), ByteOrder.LITTLE_ENDIAN);
    yEncoderValue = byteArrayToInt(Arrays.copyOfRange(b, 12, 16), ByteOrder.LITTLE_ENDIAN);
    xPosition = toFloat(Arrays.copyOfRange(b, 16, 20));
    yPosition = toFloat(Arrays.copyOfRange(b, 20, 24));
    hOrientation = toFloat(Arrays.copyOfRange(b, 24, 28));
  }

  public void setOffsets(double xOffsetMm, double yOffsetMm) {
    writeFloat(X_POD_OFFSET, (float) xOffsetMm);
    writeFloat(Y_POD_OFFSET, (float) yOffsetMm);
  }

  public void setTicksPerMm(double ticksPerMm) {
    writeFloat(MM_PER_TICK, (float) ticksPerMm);
  }

  public void resetPosAndIMU() {
    writeInt(DEVICE_CONTROL, 1 << 1);
  }

  public int getDeviceStatus() { return deviceStatus; }
  public int getLoopTime() { return loopTime; }
  public int getEncoderX() { return xEncoderValue; }
  public int getEncoderY() { return yEncoderValue; }
  public double getPosX() { return xPosition; }
  public double getPosY() { return yPosition; }
  public double getHeading() { return hOrientation; }
}
