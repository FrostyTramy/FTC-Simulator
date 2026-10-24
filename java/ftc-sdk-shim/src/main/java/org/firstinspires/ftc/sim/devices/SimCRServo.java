package org.firstinspires.ftc.sim.devices;

import com.qualcomm.robotcore.hardware.CRServo;
import com.qualcomm.robotcore.hardware.ServoController;
import com.qualcomm.robotcore.util.Range;

/**
 * A continuous-rotation servo with the SDK's CRServoImpl semantics: power −1..1 (clipped, REVERSE
 * negates) is sent as servo position (power + 1) / 2 through the normal PWM range.
 */
public final class SimCRServo implements CRServo {
  private final SimServoController controller;
  private final int port;
  private Direction direction = Direction.FORWARD;
  private double power;

  public SimCRServo(SimServoController controller, int port) {
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
  public void setPower(double power) {
    this.power = Range.clip(power, -1, 1);
    double raw = direction == Direction.REVERSE ? -this.power : this.power;
    controller.setServoPosition(port, Range.scale(raw, -1, 1, 0, 1));
  }

  @Override
  public double getPower() {
    return power;
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "Continuous Rotation Servo";
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
    direction = Direction.FORWARD;
  }

  @Override
  public void close() {
    setPower(0);
  }
}
