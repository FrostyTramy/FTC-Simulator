package com.qualcomm.hardware.sparkfun;

import com.qualcomm.robotcore.hardware.I2cDeviceSynch;
import com.qualcomm.robotcore.hardware.I2cDeviceSynchDevice;
import java.util.Locale;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.sim.SimRuntime;
import org.firstinspires.ftc.sim.devices.SimI2cClient;
import org.firstinspires.ftc.sim.devices.SimI2cDevice;

/**
 * SparkFun Optical Tracking Odometry Sensor driver (SDK 12.0 public API; signatures and constants from javap, behaviour
 * of SparkFun's open-source library): poses in int16 registers scaled by the constants below, units chosen with
 * setLinearUnit/setAngularUnit (default inches and degrees), IMU calibration and self test polled with short delays.
 * The sensor firmware runs in the simulator (sim/Sim.Sensors/OtosDevice.cs).
 */
public class SparkFunOTOS extends I2cDeviceSynchDevice<I2cDeviceSynch> {
  public static final byte DEFAULT_ADDRESS = 0x17;
  public static final double MIN_SCALAR = 0.872;
  public static final double MAX_SCALAR = 1.127;
  protected static final byte REG_PRODUCT_ID = 0x00;
  protected static final byte REG_HW_VERSION = 0x01;
  protected static final byte REG_FW_VERSION = 0x02;
  protected static final byte REG_SCALAR_LINEAR = 0x04;
  protected static final byte REG_SCALAR_ANGULAR = 0x05;
  protected static final byte REG_IMU_CALIB = 0x06;
  protected static final byte REG_RESET = 0x07;
  protected static final byte REG_SIGNAL_PROCESS = 0x0E;
  protected static final byte REG_SELF_TEST = 0x0F;
  protected static final byte REG_OFF_XL = 0x10;
  protected static final byte REG_STATUS = 0x1F;
  protected static final byte REG_POS_XL = 0x20;
  protected static final byte REG_VEL_XL = 0x26;
  protected static final byte REG_ACC_XL = 0x2C;
  protected static final byte REG_POS_STD_XL = 0x32;
  protected static final byte REG_VEL_STD_XL = 0x38;
  protected static final byte REG_ACC_STD_XL = 0x3E;
  protected static final byte PRODUCT_ID = 0x5F;
  protected static final double RADIAN_TO_DEGREE = 180.0 / Math.PI;
  protected static final double DEGREE_TO_RADIAN = Math.PI / 180.0;
  protected static final double METER_TO_INT16 = 32768.0 / 10.0;
  protected static final double INT16_TO_METER = 1.0 / METER_TO_INT16;
  protected static final double MPS_TO_INT16 = 32768.0 / 5.0;
  protected static final double INT16_TO_MPS = 1.0 / MPS_TO_INT16;
  protected static final double MPSS_TO_INT16 = 32768.0 / (16.0 * 9.80665);
  protected static final double INT16_TO_MPSS = 1.0 / MPSS_TO_INT16;
  protected static final double RAD_TO_INT16 = 32768.0 / Math.PI;
  protected static final double INT16_TO_RAD = 1.0 / RAD_TO_INT16;
  protected static final double RPS_TO_INT16 = 32768.0 / (2000.0 * DEGREE_TO_RADIAN);
  protected static final double INT16_TO_RPS = 1.0 / RPS_TO_INT16;
  protected static final double RPSS_TO_INT16 = 32768.0 / (Math.PI * 1000.0);
  protected static final double INT16_TO_RPSS = 1.0 / RPSS_TO_INT16;

  // Value indexes in the simulator's snapshot (OtosDevice).
  private static final int I_PRODUCT = 0, I_HW = 1, I_FW = 2, I_LINEAR = 3, I_ANGULAR = 4, I_IMU = 5, I_SIGNAL = 6;
  private static final int I_SELF_TEST = 7, I_OFFSET = 8, I_STATUS = 11, I_POS = 12, I_VEL = 15, I_ACC = 18;
  private static final int I_POS_STD = 21, I_VEL_STD = 24, I_ACC_STD = 27;

  protected DistanceUnit _distanceUnit = DistanceUnit.INCH;
  protected AngleUnit _angularUnit = AngleUnit.DEGREES;
  private final SimI2cDevice device;

  /** A 2D pose (or velocity, acceleration) in the units set on the sensor. */
  public static class Pose2D {
    public double x;
    public double y;
    public double h;

    public Pose2D() {}

    public Pose2D(double x, double y, double h) {
      this.x = x;
      this.y = y;
      this.h = h;
    }

    public void set(Pose2D pose) {
      x = pose.x;
      y = pose.y;
      h = pose.h;
    }

    @Override
    public String toString() {
      return String.format(Locale.US, "(%.3f, %.3f, %.3f)", x, y, h);
    }
  }

  /** Status register: bit 0 tilt warning, bit 1 optical tracking warning, bit 6 PAA error, bit 7 LSM error. */
  public static class Status {
    public boolean warnTiltAngle;
    public boolean warnOpticalTracking;
    public boolean errorPaa;
    public boolean errorLsm;

    public Status() {}

    public Status(byte value) {
      set(value);
    }

    public void set(byte value) {
      warnTiltAngle = (value & 0x01) != 0;
      warnOpticalTracking = (value & 0x02) != 0;
      errorPaa = (value & 0x40) != 0;
      errorLsm = (value & 0x80) != 0;
    }

    public byte get() {
      return (byte) ((warnTiltAngle ? 0x01 : 0) | (warnOpticalTracking ? 0x02 : 0) | (errorPaa ? 0x40 : 0) | (errorLsm ? 0x80 : 0));
    }
  }

  /** Signal processing switches: lookup table, accelerometer, rotation, variance. */
  public static class SignalProcessConfig {
    public boolean enLut;
    public boolean enAcc;
    public boolean enRot;
    public boolean enVar;

    public SignalProcessConfig() {}

    public SignalProcessConfig(byte value) {
      set(value);
    }

    public void set(byte value) {
      enLut = (value & 0x01) != 0;
      enAcc = (value & 0x02) != 0;
      enRot = (value & 0x04) != 0;
      enVar = (value & 0x08) != 0;
    }

    public byte get() {
      return (byte) ((enLut ? 0x01 : 0) | (enAcc ? 0x02 : 0) | (enRot ? 0x04 : 0) | (enVar ? 0x08 : 0));
    }
  }

  /** Hardware or firmware version: major in the high nibble, minor in the low nibble. */
  public static class Version {
    public byte minor;
    public byte major;

    public Version() {}

    public Version(byte value) {
      set(value);
    }

    public void set(byte value) {
      minor = (byte) (value & 0x0F);
      major = (byte) ((value >> 4) & 0x0F);
    }

    public byte get() {
      return (byte) ((major << 4) | (minor & 0x0F));
    }
  }

  /** Self test register: start, in progress, pass, fail. */
  public static class SelfTestConfig {
    public boolean start;
    public boolean inProgress;
    public boolean pass;
    public boolean fail;

    public SelfTestConfig() {}

    public SelfTestConfig(byte value) {
      set(value);
    }

    public void set(byte value) {
      start = (value & 0x01) != 0;
      inProgress = (value & 0x02) != 0;
      pass = (value & 0x04) != 0;
      fail = (value & 0x08) != 0;
    }

    public byte get() {
      return (byte) ((start ? 0x01 : 0) | (inProgress ? 0x02 : 0) | (pass ? 0x04 : 0) | (fail ? 0x08 : 0));
    }
  }

  public SparkFunOTOS(I2cDeviceSynch deviceClient) {
    super(deviceClient, true);
    if (!(deviceClient instanceof SimI2cClient)) {
      throw new IllegalArgumentException("SparkFunOTOS needs a simulated I2C device from the hardware map");
    }
    device = ((SimI2cClient) deviceClient).device();
    super.registerArmingStateCallback(false);
  }

  @Override
  protected boolean doInitialize() {
    return true;
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.SparkFun;
  }

  @Override
  public String getDeviceName() {
    return "SparkFun Qwiic Optical Tracking Odometry Sensor";
  }

  /** True if the sensor answers with the right product id. */
  public boolean begin() {
    return isConnected();
  }

  public boolean isConnected() {
    return (int) device.read()[I_PRODUCT] == PRODUCT_ID;
  }

  public void getVersionInfo(Version hwVersion, Version fwVersion) {
    double[] r = device.read();
    hwVersion.set((byte) (int) r[I_HW]);
    fwVersion.set((byte) (int) r[I_FW]);
  }

  /** Runs the self test and waits for its result (polls up to 10 times, 5 ms apart). */
  public boolean selfTest() {
    SelfTestConfig config = new SelfTestConfig();
    config.start = true;
    device.write("write", new double[] {REG_SELF_TEST, config.get()}, I_SELF_TEST, 0x02);
    for (int i = 0; i < 10; i++) {
      delayMs(5);
      config.set((byte) (int) device.read()[I_SELF_TEST]);
      if (!config.inProgress) {
        break;
      }
    }
    return config.pass;
  }

  public boolean calibrateImu() {
    return calibrateImu(255, true);
  }

  /** Starts an IMU calibration of {@code numSamples} (about 2.4 ms each); optionally waits until it is done. */
  public boolean calibrateImu(int numSamples, boolean waitUntilDone) {
    device.write("write", new double[] {REG_IMU_CALIB, numSamples & 0xFF}, I_IMU, numSamples & 0xFF);
    delayMs(3);
    if (!waitUntilDone) {
      return true;
    }
    for (int i = numSamples; i > 0; i--) {
      if ((int) device.read()[I_IMU] == 0) {
        return true;
      }
      delayMs(3);
    }
    return false;
  }

  /** Samples left in the running IMU calibration (0 = done). */
  public int getImuCalibrationProgress() {
    return (int) device.read()[I_IMU];
  }

  public DistanceUnit getLinearUnit() {
    return _distanceUnit;
  }

  public void setLinearUnit(DistanceUnit unit) {
    _distanceUnit = unit;
  }

  public AngleUnit getAngularUnit() {
    return _angularUnit;
  }

  public void setAngularUnit(AngleUnit unit) {
    _angularUnit = unit;
  }

  public double getLinearScalar() {
    return 1.0 + (byte) (int) device.read()[I_LINEAR] * 0.001;
  }

  /** Corrects the linear scale; false (nothing written) outside MIN_SCALAR..MAX_SCALAR. */
  public boolean setLinearScalar(double scalar) {
    if (scalar < MIN_SCALAR || scalar > MAX_SCALAR) {
      return false;
    }
    byte raw = (byte) Math.round((scalar - 1.0) * 1000);
    device.write("write", new double[] {REG_SCALAR_LINEAR, raw}, I_LINEAR, raw);
    return true;
  }

  public double getAngularScalar() {
    return 1.0 + (byte) (int) device.read()[I_ANGULAR] * 0.001;
  }

  public boolean setAngularScalar(double scalar) {
    if (scalar < MIN_SCALAR || scalar > MAX_SCALAR) {
      return false;
    }
    byte raw = (byte) Math.round((scalar - 1.0) * 1000);
    device.write("write", new double[] {REG_SCALAR_ANGULAR, raw}, I_ANGULAR, raw);
    return true;
  }

  /** Resets the tracking: the robot is at (0, 0, 0) from now on. */
  public void resetTracking() {
    device.write("write", new double[] {REG_RESET, 0x01}, I_POS, 0, I_POS + 1, 0, I_POS + 2, 0, I_VEL, 0, I_VEL + 1, 0,
        I_VEL + 2, 0);
  }

  public SignalProcessConfig getSignalProcessConfig() {
    return new SignalProcessConfig((byte) (int) device.read()[I_SIGNAL]);
  }

  public void setSignalProcessConfig(SignalProcessConfig config) {
    device.write("write", new double[] {REG_SIGNAL_PROCESS, config.get() & 0xFF}, I_SIGNAL, config.get() & 0xFF);
  }

  public Status getStatus() {
    return new Status((byte) (int) device.read()[I_STATUS]);
  }

  public Pose2D getOffset() {
    return readPoseRegs(REG_OFF_XL, INT16_TO_METER, INT16_TO_RAD);
  }

  /** Where the sensor sits relative to the robot centre (x right, y forward, h counter-clockwise). */
  public void setOffset(Pose2D pose) {
    writePoseRegs(REG_OFF_XL, pose, METER_TO_INT16, RAD_TO_INT16);
  }

  public Pose2D getPosition() {
    return readPoseRegs(REG_POS_XL, INT16_TO_METER, INT16_TO_RAD);
  }

  public void setPosition(Pose2D pose) {
    writePoseRegs(REG_POS_XL, pose, METER_TO_INT16, RAD_TO_INT16);
  }

  public Pose2D getVelocity() {
    return readPoseRegs(REG_VEL_XL, INT16_TO_MPS, INT16_TO_RPS);
  }

  public Pose2D getAcceleration() {
    return readPoseRegs(REG_ACC_XL, INT16_TO_MPSS, INT16_TO_RPSS);
  }

  public Pose2D getPositionStdDev() {
    return readPoseRegs(REG_POS_STD_XL, INT16_TO_METER, INT16_TO_RAD);
  }

  public Pose2D getVelocityStdDev() {
    return readPoseRegs(REG_VEL_STD_XL, INT16_TO_MPS, INT16_TO_RPS);
  }

  public Pose2D getAccelerationStdDev() {
    return readPoseRegs(REG_ACC_STD_XL, INT16_TO_MPSS, INT16_TO_RPSS);
  }

  /** Position, velocity and acceleration in one 18-byte read. */
  public void getPosVelAcc(Pose2D pos, Pose2D vel, Pose2D acc) {
    double[] r = device.read();
    pos.set(regsToPose(bytes(r, I_POS), INT16_TO_METER, INT16_TO_RAD));
    vel.set(regsToPose(bytes(r, I_VEL), INT16_TO_MPS, INT16_TO_RPS));
    acc.set(regsToPose(bytes(r, I_ACC), INT16_TO_MPSS, INT16_TO_RPSS));
  }

  public void getPosVelAccStdDev(Pose2D pos, Pose2D vel, Pose2D acc) {
    double[] r = device.read();
    pos.set(regsToPose(bytes(r, I_POS_STD), INT16_TO_METER, INT16_TO_RAD));
    vel.set(regsToPose(bytes(r, I_VEL_STD), INT16_TO_MPS, INT16_TO_RPS));
    acc.set(regsToPose(bytes(r, I_ACC_STD), INT16_TO_MPSS, INT16_TO_RPSS));
  }

  public void getPosVelAccAndStdDev(Pose2D pos, Pose2D vel, Pose2D acc, Pose2D posStdDev, Pose2D velStdDev,
      Pose2D accStdDev) {
    double[] r = device.read();
    pos.set(regsToPose(bytes(r, I_POS), INT16_TO_METER, INT16_TO_RAD));
    vel.set(regsToPose(bytes(r, I_VEL), INT16_TO_MPS, INT16_TO_RPS));
    acc.set(regsToPose(bytes(r, I_ACC), INT16_TO_MPSS, INT16_TO_RPSS));
    posStdDev.set(regsToPose(bytes(r, I_POS_STD), INT16_TO_METER, INT16_TO_RAD));
    velStdDev.set(regsToPose(bytes(r, I_VEL_STD), INT16_TO_MPS, INT16_TO_RPS));
    accStdDev.set(regsToPose(bytes(r, I_ACC_STD), INT16_TO_MPSS, INT16_TO_RPSS));
  }

  protected Pose2D readPoseRegs(byte reg, double rawToXY, double rawToH) {
    return regsToPose(bytes(device.read(), index(reg)), rawToXY, rawToH);
  }

  protected void writePoseRegs(byte reg, Pose2D pose, double xyToRaw, double hToRaw) {
    byte[] raw = new byte[6];
    poseToRegs(raw, pose, xyToRaw, hToRaw);
    short x = (short) ((raw[1] << 8) | (raw[0] & 0xFF));
    short y = (short) ((raw[3] << 8) | (raw[2] & 0xFF));
    short h = (short) ((raw[5] << 8) | (raw[4] & 0xFF));
    int i = index(reg);
    device.write("write", new double[] {reg, x, y, h}, i, x, i + 1, y, i + 2, h);
  }

  protected Pose2D regsToPose(byte[] rawData, double rawToXY, double rawToH) {
    short rawX = (short) ((rawData[1] << 8) | (rawData[0] & 0xFF));
    short rawY = (short) ((rawData[3] << 8) | (rawData[2] & 0xFF));
    short rawH = (short) ((rawData[5] << 8) | (rawData[4] & 0xFF));
    Pose2D pose = new Pose2D();
    pose.x = _distanceUnit.fromMeters(rawX * rawToXY);
    pose.y = _distanceUnit.fromMeters(rawY * rawToXY);
    pose.h = _angularUnit.fromRadians(rawH * rawToH);
    return pose;
  }

  protected void poseToRegs(byte[] rawData, Pose2D pose, double xyToRaw, double hToRaw) {
    short rawX = (short) (_distanceUnit.toMeters(pose.x) * xyToRaw);
    short rawY = (short) (_distanceUnit.toMeters(pose.y) * xyToRaw);
    short rawH = (short) (_angularUnit.toRadians(pose.h) * hToRaw);
    rawData[0] = (byte) (rawX & 0xFF);
    rawData[1] = (byte) ((rawX >> 8) & 0xFF);
    rawData[2] = (byte) (rawY & 0xFF);
    rawData[3] = (byte) ((rawY >> 8) & 0xFF);
    rawData[4] = (byte) (rawH & 0xFF);
    rawData[5] = (byte) ((rawH >> 8) & 0xFF);
  }

  /** Little-endian bytes of the three int16 values starting at a snapshot index. */
  private static byte[] bytes(double[] values, int index) {
    byte[] raw = new byte[6];
    for (int i = 0; i < 3; i++) {
      short v = (short) (int) values[index + i];
      raw[2 * i] = (byte) (v & 0xFF);
      raw[2 * i + 1] = (byte) ((v >> 8) & 0xFF);
    }
    return raw;
  }

  private static int index(byte reg) {
    switch (reg) {
      case REG_OFF_XL:
        return I_OFFSET;
      case REG_POS_XL:
        return I_POS;
      case REG_VEL_XL:
        return I_VEL;
      case REG_ACC_XL:
        return I_ACC;
      case REG_POS_STD_XL:
        return I_POS_STD;
      case REG_VEL_STD_XL:
        return I_VEL_STD;
      case REG_ACC_STD_XL:
        return I_ACC_STD;
      default:
        throw new IllegalArgumentException("not a pose register: " + reg);
    }
  }

  /** The library's short waits, as virtual time on the OpMode thread. */
  private static void delayMs(int ms) {
    SimRuntime rt = SimRuntime.get();
    try {
      rt.sleepUntil(rt.nowNanos() + ms * 1_000_000L);
    } catch (InterruptedException e) {
      Thread.currentThread().interrupt();
    }
  }
}
