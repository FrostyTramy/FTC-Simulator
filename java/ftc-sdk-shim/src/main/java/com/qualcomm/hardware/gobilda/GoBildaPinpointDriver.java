package com.qualcomm.hardware.gobilda;

import com.qualcomm.robotcore.hardware.I2cDeviceSynchDevice;
import com.qualcomm.robotcore.hardware.I2cDeviceSynchSimple;
import java.util.EnumSet;
import java.util.Set;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Pose2D;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;
import org.firstinspires.ftc.robotcore.external.navigation.UnnormalizedAngleUnit;
import org.firstinspires.ftc.sim.SimRuntime;
import org.firstinspires.ftc.sim.devices.SimI2cClient;
import org.firstinspires.ftc.sim.devices.SimI2cDevice;

/**
 * goBILDA Pinpoint odometry computer driver (SDK 12.0 public API, signatures from javap). Behaviour follows goBILDA's
 * open-source driver (generated/sdk/vendor/GoBildaPinpointDriver_v1.java, MIT): every getter returns the values of the
 * last {@link #update()}, which is one bulk I2C read; setters are register writes; position/velocity in mm and
 * radians; the corrupt-read checks of update() (LOCAL_TEST). The device firmware itself runs in the simulator
 * (sim/Sim.Sensors/PinpointDevice.cs). Methods added in SDK 12 (bulk read scope, error detection type, quaternion,
 * pitch, roll) are implemented from their names; register numbers after BULK_READ are ASSUMED.
 */
public class GoBildaPinpointDriver extends I2cDeviceSynchDevice<I2cDeviceSynchSimple> {
  public static final byte DEFAULT_ADDRESS = 0x31;
  private static final float GOBILDA_SWINGARM_POD = 13.26291192f; // ticks per mm (GBDRV)
  private static final float GOBILDA_4_BAR_POD = 19.89436789f;

  /** Register map (GBDRV; QUATERNION_W and later ASSUMED). */
  public enum Register {
    DEVICE_ID(1, 10),
    DEVICE_VERSION(2, 11),
    DEVICE_STATUS(3, 0),
    DEVICE_CONTROL(4, -1),
    LOOP_TIME(5, 1),
    X_ENCODER_VALUE(6, 2),
    Y_ENCODER_VALUE(7, 3),
    X_POSITION(8, 4),
    Y_POSITION(9, 5),
    H_ORIENTATION(10, 6),
    X_VELOCITY(11, 7),
    Y_VELOCITY(12, 8),
    H_VELOCITY(13, 9),
    MM_PER_TICK(14, 13),
    X_POD_OFFSET(15, 14),
    Y_POD_OFFSET(16, 15),
    YAW_SCALAR(17, 12),
    BULK_READ(18, -1),
    QUATERNION_W(19, 16),
    QUATERNION_X(20, 17),
    QUATERNION_Y(21, 18),
    QUATERNION_Z(22, 19),
    PITCH(23, 20),
    ROLL(24, 21),
    SET_BULK_READ(25, -1);

    private final int bVal;
    private final int index; // value index in the simulator's snapshot (PinpointDevice), -1 = none

    Register(int bVal, int index) {
      this.bVal = bVal;
      this.index = index;
    }
  }

  /** Device status (GBDRV bit values). */
  public enum DeviceStatus {
    NOT_READY(0),
    READY(1),
    CALIBRATING(1 << 1),
    FAULT_X_POD_NOT_DETECTED(1 << 2),
    FAULT_Y_POD_NOT_DETECTED(1 << 3),
    FAULT_NO_PODS_DETECTED(1 << 2 | 1 << 3),
    FAULT_IMU_RUNAWAY(1 << 4),
    FAULT_BAD_READ(1 << 5);

    private final int status;

    DeviceStatus(int status) {
      this.status = status;
    }
  }

  public enum EncoderDirection {
    FORWARD,
    REVERSED
  }

  public enum GoBildaOdometryPods {
    goBILDA_SWINGARM_POD,
    goBILDA_4_BAR_POD
  }

  public enum ReadData {
    ONLY_UPDATE_HEADING
  }

  public enum ErrorDetectionType {
    NONE,
    CRC,
    LOCAL_TEST
  }

  private static final Set<Register> DEFAULT_SCOPE = EnumSet.of(Register.DEVICE_STATUS, Register.LOOP_TIME,
      Register.X_ENCODER_VALUE, Register.Y_ENCODER_VALUE, Register.X_POSITION, Register.Y_POSITION,
      Register.H_ORIENTATION, Register.X_VELOCITY, Register.Y_VELOCITY, Register.H_VELOCITY);

  private final SimI2cDevice device;
  private Set<Register> bulkScope = EnumSet.copyOf(DEFAULT_SCOPE);
  private ErrorDetectionType errorDetection = ErrorDetectionType.LOCAL_TEST;
  private int deviceStatus = 0;
  private int loopTime = 0;
  private int xEncoderValue = 0;
  private int yEncoderValue = 0;
  private float xPosition = 0;
  private float yPosition = 0;
  private float hOrientation = 0;
  private float xVelocity = 0;
  private float yVelocity = 0;
  private float hVelocity = 0;
  private float qw = 1;
  private float qx = 0;
  private float qy = 0;
  private float qz = 0;
  private float pitch = 0;
  private float roll = 0;

  public GoBildaPinpointDriver(I2cDeviceSynchSimple deviceClient, boolean deviceClientIsOwned) {
    super(deviceClient, deviceClientIsOwned);
    if (!(deviceClient instanceof SimI2cClient)) {
      throw new IllegalArgumentException("GoBildaPinpointDriver needs a simulated I2C device from the hardware map");
    }
    this.device = ((SimI2cClient) deviceClient).device();
    super.registerArmingStateCallback(false);
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Other;
  }

  @Override
  protected synchronized boolean doInitialize() {
    return true;
  }

  @Override
  public String getDeviceName() {
    return "goBILDA® Pinpoint Odometry Computer";
  }

  private void writeInt(Register reg, int value, double... shown) {
    device.write("write", new double[] {reg.bVal, value}, shown);
  }

  private void writeFloat(Register reg, float value) {
    device.write("write", new double[] {reg.bVal, value}, reg.index, value);
  }

  private float readRegister(Register reg) {
    return (float) device.read()[reg.index];
  }

  /** GBDRV isPositionCorrupt: keeps the old value and flags FAULT_BAD_READ for NaN, no data or a jump. */
  private float positionCheck(float oldValue, float newValue, int threshold, boolean bulkUpdate) {
    if (errorDetection != ErrorDetectionType.LOCAL_TEST) {
      return newValue;
    }
    boolean noData = bulkUpdate && (loopTime < 1);
    boolean corrupt = noData || Float.isNaN(newValue) || Math.abs(newValue - oldValue) > threshold;
    if (!corrupt) {
      return newValue;
    }
    deviceStatus = DeviceStatus.FAULT_BAD_READ.status;
    return oldValue;
  }

  /** GBDRV isVelocityCorrupt. */
  private float velocityCheck(float oldValue, float newValue, int threshold) {
    if (errorDetection != ErrorDetectionType.LOCAL_TEST) {
      return newValue;
    }
    boolean corrupt = Float.isNaN(newValue) || Math.abs(newValue) > threshold;
    if (!corrupt) {
      return newValue;
    }
    deviceStatus = DeviceStatus.FAULT_BAD_READ.status;
    return oldValue;
  }

  /** Reads new data from the device (one bulk I2C read of the registers in the bulk read scope). */
  public void update() {
    final int positionThreshold = 5000; // more than one FTC field in mm (GBDRV)
    final int headingThreshold = 120; // about 20 rotations in radians
    final int velocityThreshold = 10000; // mm/s
    final int headingVelocityThreshold = 120; // rad/s
    float oldPosX = xPosition;
    float oldPosY = yPosition;
    float oldPosH = hOrientation;
    float oldVelX = xVelocity;
    float oldVelY = yVelocity;
    float oldVelH = hVelocity;
    double[] r = device.read();
    if (bulkScope.contains(Register.DEVICE_STATUS)) {
      deviceStatus = (int) r[0];
    }
    if (bulkScope.contains(Register.LOOP_TIME)) {
      loopTime = (int) r[1];
    }
    if (bulkScope.contains(Register.X_ENCODER_VALUE)) {
      xEncoderValue = (int) r[2];
    }
    if (bulkScope.contains(Register.Y_ENCODER_VALUE)) {
      yEncoderValue = (int) r[3];
    }
    if (bulkScope.contains(Register.X_POSITION)) {
      xPosition = positionCheck(oldPosX, (float) r[4], positionThreshold, true);
    }
    if (bulkScope.contains(Register.Y_POSITION)) {
      yPosition = positionCheck(oldPosY, (float) r[5], positionThreshold, true);
    }
    if (bulkScope.contains(Register.H_ORIENTATION)) {
      hOrientation = positionCheck(oldPosH, (float) r[6], headingThreshold, true);
    }
    if (bulkScope.contains(Register.X_VELOCITY)) {
      xVelocity = velocityCheck(oldVelX, (float) r[7], velocityThreshold);
    }
    if (bulkScope.contains(Register.Y_VELOCITY)) {
      yVelocity = velocityCheck(oldVelY, (float) r[8], velocityThreshold);
    }
    if (bulkScope.contains(Register.H_VELOCITY)) {
      hVelocity = velocityCheck(oldVelH, (float) r[9], headingVelocityThreshold);
    }
    if (bulkScope.contains(Register.QUATERNION_W)) {
      qw = (float) r[16];
    }
    if (bulkScope.contains(Register.QUATERNION_X)) {
      qx = (float) r[17];
    }
    if (bulkScope.contains(Register.QUATERNION_Y)) {
      qy = (float) r[18];
    }
    if (bulkScope.contains(Register.QUATERNION_Z)) {
      qz = (float) r[19];
    }
    if (bulkScope.contains(Register.PITCH)) {
      pitch = (float) r[20];
    }
    if (bulkScope.contains(Register.ROLL)) {
      roll = (float) r[21];
    }
  }

  /** Reads only the heading (one 4-byte I2C read), GBDRV update(ReadData). */
  public void update(ReadData data) {
    if (data == ReadData.ONLY_UPDATE_HEADING) {
      final int headingThreshold = 120;
      float oldPosH = hOrientation;
      hOrientation = positionCheck(oldPosH, readRegister(Register.H_ORIENTATION), headingThreshold, false);
      if (deviceStatus == DeviceStatus.FAULT_BAD_READ.status) {
        deviceStatus = DeviceStatus.READY.status;
      }
    }
  }

  /** Chooses the registers a bulk read ({@link #update()}) fetches. */
  public void setBulkReadScope(Register... registers) {
    bulkScope = registers.length == 0 ? EnumSet.copyOf(DEFAULT_SCOPE) : EnumSet.noneOf(Register.class);
    for (Register r : registers) {
      bulkScope.add(r);
    }
    writeInt(Register.SET_BULK_READ, bulkScope.size());
  }

  public void setErrorDetectionType(ErrorDetectionType errorDetectionType) {
    errorDetection = errorDetectionType;
  }

  /**
   * Pod positions relative to the tracking point: X pod sideways (left positive), Y pod forwards (forward positive).
   */
  public void setOffsets(double xOffset, double yOffset, DistanceUnit distanceUnit) {
    writeFloat(Register.X_POD_OFFSET, (float) distanceUnit.toMm(xOffset));
    writeFloat(Register.Y_POD_OFFSET, (float) distanceUnit.toMm(yOffset));
  }

  /** Recalibrates the IMU (robot must be still, about 0.25 s). */
  public void recalibrateIMU() {
    writeInt(Register.DEVICE_CONTROL, 1 << 0, 0, DeviceStatus.CALIBRATING.status);
  }

  /** Resets the position to 0, 0, 0 and recalibrates the IMU (robot must be still, about 0.25 s). */
  public void resetPosAndIMU() {
    writeInt(Register.DEVICE_CONTROL, 1 << 1, 0, DeviceStatus.CALIBRATING.status, 4, 0, 5, 0, 6, 0);
  }

  /** X pod should count up when the robot moves forward, Y pod when it moves left. */
  public void setEncoderDirections(EncoderDirection xEncoder, EncoderDirection yEncoder) {
    if (xEncoder == EncoderDirection.FORWARD) {
      writeInt(Register.DEVICE_CONTROL, 1 << 5);
    }
    if (xEncoder == EncoderDirection.REVERSED) {
      writeInt(Register.DEVICE_CONTROL, 1 << 4);
    }
    if (yEncoder == EncoderDirection.FORWARD) {
      writeInt(Register.DEVICE_CONTROL, 1 << 3);
    }
    if (yEncoder == EncoderDirection.REVERSED) {
      writeInt(Register.DEVICE_CONTROL, 1 << 2);
    }
  }

  public void setEncoderResolution(GoBildaOdometryPods pods) {
    if (pods == GoBildaOdometryPods.goBILDA_SWINGARM_POD) {
      writeFloat(Register.MM_PER_TICK, GOBILDA_SWINGARM_POD);
    }
    if (pods == GoBildaOdometryPods.goBILDA_4_BAR_POD) {
      writeFloat(Register.MM_PER_TICK, GOBILDA_4_BAR_POD);
    }
  }

  /** Ticks per distance unit of the pods; converted with {@code distanceUnit.toMm} exactly as GBDRV does. */
  public void setEncoderResolution(double ticksPerUnit, DistanceUnit distanceUnit) {
    writeFloat(Register.MM_PER_TICK, (float) distanceUnit.toMm(ticksPerUnit));
  }

  public void setYawScalar(double yawOffset) {
    writeFloat(Register.YAW_SCALAR, (float) yawOffset);
  }

  /** Overrides the current position (x, y in mm and heading in radians registers). */
  public void setPosition(Pose2D pos) {
    writeFloat(Register.X_POSITION, (float) pos.getX(DistanceUnit.MM));
    writeFloat(Register.Y_POSITION, (float) pos.getY(DistanceUnit.MM));
    writeFloat(Register.H_ORIENTATION, (float) pos.getHeading(AngleUnit.RADIANS));
  }

  public void setPosX(double posX, DistanceUnit distanceUnit) {
    writeFloat(Register.X_POSITION, (float) distanceUnit.toMm(posX));
  }

  public void setPosY(double posY, DistanceUnit distanceUnit) {
    writeFloat(Register.Y_POSITION, (float) distanceUnit.toMm(posY));
  }

  public void setHeading(double heading, AngleUnit angleUnit) {
    writeFloat(Register.H_ORIENTATION, (float) angleUnit.toRadians(heading));
  }

  public int getDeviceID() {
    return (int) readRegister(Register.DEVICE_ID);
  }

  public int getDeviceVersion() {
    return (int) readRegister(Register.DEVICE_VERSION);
  }

  public float getYawScalar() {
    return readRegister(Register.YAW_SCALAR);
  }

  /** GBDRV lookupStatus: calibrating first, then pod faults, IMU runaway, ready, bad read. */
  public DeviceStatus getDeviceStatus() {
    int s = deviceStatus;
    if ((s & DeviceStatus.CALIBRATING.status) != 0) {
      return DeviceStatus.CALIBRATING;
    }
    boolean xPodDetected = (s & DeviceStatus.FAULT_X_POD_NOT_DETECTED.status) == 0;
    boolean yPodDetected = (s & DeviceStatus.FAULT_Y_POD_NOT_DETECTED.status) == 0;
    if (!xPodDetected && !yPodDetected) {
      return DeviceStatus.FAULT_NO_PODS_DETECTED;
    }
    if (!xPodDetected) {
      return DeviceStatus.FAULT_X_POD_NOT_DETECTED;
    }
    if (!yPodDetected) {
      return DeviceStatus.FAULT_Y_POD_NOT_DETECTED;
    }
    if ((s & DeviceStatus.FAULT_IMU_RUNAWAY.status) != 0) {
      return DeviceStatus.FAULT_IMU_RUNAWAY;
    }
    if ((s & DeviceStatus.READY.status) != 0) {
      return DeviceStatus.READY;
    }
    if ((s & DeviceStatus.FAULT_BAD_READ.status) != 0) {
      return DeviceStatus.FAULT_BAD_READ;
    }
    return DeviceStatus.NOT_READY;
  }

  /** Device loop time in microseconds. */
  public int getLoopTime() {
    return loopTime;
  }

  public double getFrequency() {
    return loopTime != 0 ? 1_000_000.0 / loopTime : 0;
  }

  public int getEncoderX() {
    return xEncoderValue;
  }

  public int getEncoderY() {
    return yEncoderValue;
  }

  public double getPosX(DistanceUnit distanceUnit) {
    return distanceUnit.fromMm(xPosition);
  }

  public double getPosY(DistanceUnit distanceUnit) {
    return distanceUnit.fromMm(yPosition);
  }

  /** Heading wrapped to ±180°. */
  public double getHeading(AngleUnit angleUnit) {
    return angleUnit.fromRadians((double) hOrientation);
  }

  /** Heading that keeps counting over several turns. */
  public double getHeading(UnnormalizedAngleUnit unnormalizedAngleUnit) {
    return unnormalizedAngleUnit.fromRadians(hOrientation);
  }

  public double getVelX(DistanceUnit distanceUnit) {
    return distanceUnit.fromMm(xVelocity);
  }

  public double getVelY(DistanceUnit distanceUnit) {
    return distanceUnit.fromMm(yVelocity);
  }

  public double getHeadingVelocity(UnnormalizedAngleUnit unnormalizedAngleUnit) {
    return unnormalizedAngleUnit.fromRadians(hVelocity);
  }

  public float getXOffset(DistanceUnit distanceUnit) {
    return (float) distanceUnit.fromMm(readRegister(Register.X_POD_OFFSET));
  }

  public float getYOffset(DistanceUnit distanceUnit) {
    return (float) distanceUnit.fromMm(readRegister(Register.Y_POD_OFFSET));
  }

  public Pose2D getPosition() {
    return new Pose2D(DistanceUnit.MM, xPosition, yPosition, AngleUnit.RADIANS, hOrientation);
  }

  /** Device orientation (Z up); read now unless the bulk read scope includes it. */
  public Quaternion getQuaternion() {
    if (!bulkScope.contains(Register.QUATERNION_W)) {
      double[] r = device.read();
      qw = (float) r[16];
      qx = (float) r[17];
      qy = (float) r[18];
      qz = (float) r[19];
    }
    return new Quaternion(qw, qx, qy, qz, SimRuntime.get().bus().inputTimeNanos());
  }

  public double getPitch(AngleUnit angleUnit) {
    if (!bulkScope.contains(Register.PITCH)) {
      pitch = readRegister(Register.PITCH);
    }
    return angleUnit.fromRadians((double) pitch);
  }

  public double getRoll(AngleUnit angleUnit) {
    if (!bulkScope.contains(Register.ROLL)) {
      roll = readRegister(Register.ROLL);
    }
    return angleUnit.fromRadians((double) roll);
  }
}
