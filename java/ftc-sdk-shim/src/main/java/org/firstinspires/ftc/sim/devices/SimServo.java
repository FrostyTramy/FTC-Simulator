package org.firstinspires.ftc.sim.devices;

import com.qualcomm.robotcore.hardware.Servo;
import com.qualcomm.robotcore.hardware.ServoController;
import com.qualcomm.robotcore.util.Range;

/**
 * A position servo with the SDK's ServoImpl semantics: position clipped to 0..1, REVERSE mirrors it,
 * scaleRange(min, max) maps 0..1 onto a sub-range of the controller range.
 */
public final class SimServo implements Servo {
  private final SimServoController controller;
  private final int port;
  private Direction direction = Direction.FORWARD;
  private double limitMin = MIN_POSITION;
  private double limitMax = MAX_POSITION;
  private double position = Double.NaN;

  public SimServo(SimServoController controller, int port) {
    this.controller = controller;
    this.port = port;
  }

  @Override
  public ServoController getController() {
    return controller;
  }

  @Override
  public int getPortNumber() {
    return port;
  }

  @Override
  public void setDirection(Direction direction) {
    this.direction = direction;
  }

  @Override
  public Direction getDirection() {
    return direction;
  }

  @Override
  public void setPosition(double position) {
    double p = Range.clip(position, MIN_POSITION, MAX_POSITION);
    this.position = p;
    double raw = direction == Direction.REVERSE ? MAX_POSITION - p : p;
    controller.setServoPosition(port, Range.scale(raw, MIN_POSITION, MAX_POSITION, limitMin, limitMax));
  }

  /** The last position set (NaN before the first setPosition, like an unset servo port). */
  @Override
  public double getPosition() {
    return position;
  }

  @Override
  public void scaleRange(double min, double max) {
    double lo = Range.clip(min, MIN_POSITION, MAX_POSITION);
    double hi = Range.clip(max, MIN_POSITION, MAX_POSITION);
    if (lo >= hi) {
      throw new IllegalArgumentException("min must be less than max");
    }
    limitMin = lo;
    limitMax = hi;
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "Servo";
  }

  @Override
  public String getConnectionInfo() {
    return controller.getConnectionInfo() + "; port " + port;
  }

  @Override
  public int getVersion() {
    return 1;
  }

  @Override
  public void resetDeviceConfigurationForOpMode() {
    limitMin = MIN_POSITION;
    limitMax = MAX_POSITION;
    direction = Direction.FORWARD;
  }

  @Override
  public void close() {}
}
