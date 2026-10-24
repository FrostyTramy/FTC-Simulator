package com.qualcomm.robotcore.hardware;

/** A device driven with a power −1..1 (motors, CR servos), SDK 12.0 public API. */
public interface DcMotorSimple extends HardwareDevice {
  enum Direction {
    FORWARD,
    REVERSE;

    public Direction inverted() {
      return this == FORWARD ? REVERSE : FORWARD;
    }
  }

  void setDirection(Direction direction);

  Direction getDirection();

  void setPower(double power);

  double getPower();
}
