package org.firstinspires.ftc.sim.devices;

import com.qualcomm.hardware.rev.RevHubOrientationOnRobot;
import com.qualcomm.robotcore.hardware.IMU;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.AngularVelocity;
import org.firstinspires.ftc.robotcore.external.navigation.AxesOrder;
import org.firstinspires.ftc.robotcore.external.navigation.AxesReference;
import org.firstinspires.ftc.robotcore.external.navigation.Orientation;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;
import org.firstinspires.ftc.robotcore.external.navigation.YawPitchRollAngles;
import org.firstinspires.ftc.sim.SimRuntime;

/**
 * The hub's IMU (spec 06 "IMU", spec 09). The simulator reports the physical orientation of the IMU
 * frame (= hub frame: +Z logo, +Y USB) in a Z-up world and its rates about the hub axes. Robot readings
 * use the mounting the user passed to initialize(): robot = hub ⊗ mount⁻¹. A wrong mounting in user code
 * therefore gives wrong angles, exactly like on the real robot. Yaw is zeroed by resetYaw().
 */
public final class SimImu implements IMU {
  private final String name;
  private Quaternion mount = new RevHubOrientationOnRobot(
      RevHubOrientationOnRobot.LogoFacingDirection.UP, RevHubOrientationOnRobot.UsbFacingDirection.FORWARD)
      .imuCoordinateSystemOrientationFromPerspectiveOfRobot();
  private double yawOffsetRadians;

  public SimImu(String name) {
    this.name = name;
  }

  private void i2cRead() {
    SimRuntime rt = SimRuntime.get();
    rt.consume(rt.costs().controlHubI2cReadNanos);
  }

  @Override
  public boolean initialize(Parameters parameters) {
    mount = parameters.imuOrientationOnRobot.imuCoordinateSystemOrientationFromPerspectiveOfRobot().normalized();
    yawOffsetRadians = 0;
    i2cRead();
    return true;
  }

  private Quaternion robotOrientationRaw() {
    double[] q = SimRuntime.get().bus().imuQuaternion();
    Quaternion hub = new Quaternion((float) q[0], (float) q[1], (float) q[2], (float) q[3], 0);
    return hub.multiply(mount.inverse(), SimRuntime.get().bus().inputTimeNanos());
  }

  @Override
  public void resetYaw() {
    i2cRead();
    Orientation o = robotOrientationRaw().toOrientation(AxesReference.INTRINSIC, AxesOrder.ZXY, AngleUnit.RADIANS);
    yawOffsetRadians = o.firstAngle;
  }

  @Override
  public Quaternion getRobotOrientationAsQuaternion() {
    i2cRead();
    Quaternion unYaw = new Quaternion((float) Math.cos(-yawOffsetRadians / 2), 0, 0, (float) Math.sin(-yawOffsetRadians / 2), 0);
    Quaternion q = unYaw.multiply(robotOrientationRaw(), SimRuntime.get().bus().inputTimeNanos());
    return q;
  }

  @Override
  public YawPitchRollAngles getRobotYawPitchRollAngles() {
    Quaternion q = getRobotOrientationAsQuaternion();
    Orientation o = q.toOrientation(AxesReference.INTRINSIC, AxesOrder.ZXY, AngleUnit.DEGREES);
    return new YawPitchRollAngles(AngleUnit.DEGREES, o.firstAngle, o.secondAngle, o.thirdAngle, q.acquisitionTime);
  }

  @Override
  public Orientation getRobotOrientation(AxesReference reference, AxesOrder order, AngleUnit angleUnit) {
    return getRobotOrientationAsQuaternion().toOrientation(reference, order, angleUnit);
  }

  @Override
  public AngularVelocity getRobotAngularVelocity(AngleUnit angleUnit) {
    i2cRead();
    double[] w = SimRuntime.get().bus().imuAngularVelocity();
    double[] r = rotate(mount, w);
    AngularVelocity rad = new AngularVelocity(AngleUnit.RADIANS.getUnnormalized(), (float) r[0], (float) r[1], (float) r[2],
        SimRuntime.get().bus().inputTimeNanos());
    return rad.toAngleUnit(angleUnit);
  }

  /** Rotates a vector by a unit quaternion: v' = q v q*. */
  static double[] rotate(Quaternion q, double[] v) {
    double w = q.w, x = q.x, y = q.y, z = q.z;
    double[][] m = {
      {1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y)},
      {2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x)},
      {2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y)}
    };
    return new double[] {
      m[0][0] * v[0] + m[0][1] * v[1] + m[0][2] * v[2],
      m[1][0] * v[0] + m[1][1] * v[1] + m[1][2] * v[2],
      m[2][0] * v[0] + m[2][1] * v[1] + m[2][2] * v[2]
    };
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "REV Internal IMU (BHI260AP)";
  }

  @Override
  public String getConnectionInfo() {
    return name + "; I2C bus 0";
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
