package com.qualcomm.hardware.rev;

import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.AxesOrder;
import org.firstinspires.ftc.robotcore.external.navigation.AxesReference;
import org.firstinspires.ftc.robotcore.external.navigation.Orientation;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;

/**
 * Mounting of a REV Control/Expansion Hub on the robot (SDK 12.0 public API). Robot frame: +X right,
 * +Y forward, +Z up. Hub frame: +Z = the logo side, +Y = the USB-port side, so LOGO UP + USB FORWARD is
 * the identity (REV/FTC docs, "IMU orientation"). Logo and USB directions must be perpendicular.
 */
public class RevHubOrientationOnRobot extends RevImuOrientationOnRobot {
  public enum LogoFacingDirection {
    UP,
    DOWN,
    FORWARD,
    BACKWARD,
    LEFT,
    RIGHT
  }

  public enum UsbFacingDirection {
    UP,
    DOWN,
    FORWARD,
    BACKWARD,
    LEFT,
    RIGHT
  }

  public RevHubOrientationOnRobot(LogoFacingDirection logoFacingDirection, UsbFacingDirection usbFacingDirection) {
    super(friendlyApiToQuaternion(logoFacingDirection, usbFacingDirection));
  }

  public RevHubOrientationOnRobot(Orientation rotation) {
    super(Quaternion.fromOrientation(rotation));
  }

  public RevHubOrientationOnRobot(Quaternion rotation) {
    super(rotation);
  }

  /** Rotation built from three intrinsic rotations Z, Y, X (degrees). */
  public static Orientation zyxOrientation(double z, double y, double x) {
    return new Orientation(AxesReference.INTRINSIC, AxesOrder.ZYX, AngleUnit.DEGREES, (float) z, (float) y, (float) x, 0);
  }

  /** Rotation built from three intrinsic rotations X, Y, Z (degrees). */
  public static Orientation xyzOrientation(double x, double y, double z) {
    return new Orientation(AxesReference.INTRINSIC, AxesOrder.XYZ, AngleUnit.DEGREES, (float) x, (float) y, (float) z, 0);
  }

  private static double[] direction(String name) {
    switch (name) {
      case "UP":
        return new double[] {0, 0, 1};
      case "DOWN":
        return new double[] {0, 0, -1};
      case "FORWARD":
        return new double[] {0, 1, 0};
      case "BACKWARD":
        return new double[] {0, -1, 0};
      case "LEFT":
        return new double[] {-1, 0, 0};
      default: // RIGHT
        return new double[] {1, 0, 0};
    }
  }

  static Quaternion friendlyApiToQuaternion(LogoFacingDirection logo, UsbFacingDirection usb) {
    double[] z = direction(logo.name());
    double[] y = direction(usb.name());
    double dot = z[0] * y[0] + z[1] * y[1] + z[2] * y[2];
    if (dot != 0) {
      throw new IllegalArgumentException("The specified REV Hub orientation is invalid: the logo and the USB ports cannot face "
          + (dot > 0 ? "the same direction" : "opposite directions"));
    }
    double[] x = {y[1] * z[2] - y[2] * z[1], y[2] * z[0] - y[0] * z[2], y[0] * z[1] - y[1] * z[0]};
    // Rotation matrix with the hub axes (in robot coordinates) as columns → quaternion.
    double m00 = x[0], m01 = y[0], m02 = z[0];
    double m10 = x[1], m11 = y[1], m12 = z[1];
    double m20 = x[2], m21 = y[2], m22 = z[2];
    double trace = m00 + m11 + m22;
    double w, qx, qy, qz;
    if (trace > 0) {
      double s = Math.sqrt(trace + 1) * 2;
      w = 0.25 * s;
      qx = (m21 - m12) / s;
      qy = (m02 - m20) / s;
      qz = (m10 - m01) / s;
    } else if (m00 > m11 && m00 > m22) {
      double s = Math.sqrt(1 + m00 - m11 - m22) * 2;
      w = (m21 - m12) / s;
      qx = 0.25 * s;
      qy = (m01 + m10) / s;
      qz = (m02 + m20) / s;
    } else if (m11 > m22) {
      double s = Math.sqrt(1 + m11 - m00 - m22) * 2;
      w = (m02 - m20) / s;
      qx = (m01 + m10) / s;
      qy = 0.25 * s;
      qz = (m12 + m21) / s;
    } else {
      double s = Math.sqrt(1 + m22 - m00 - m11) * 2;
      w = (m10 - m01) / s;
      qx = (m02 + m20) / s;
      qy = (m12 + m21) / s;
      qz = 0.25 * s;
    }
    return new Quaternion((float) w, (float) qx, (float) qy, (float) qz, 0);
  }
}
