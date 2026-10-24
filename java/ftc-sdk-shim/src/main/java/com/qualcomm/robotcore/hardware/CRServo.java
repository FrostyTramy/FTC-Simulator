package com.qualcomm.robotcore.hardware;

/** A continuous-rotation servo driven with a power −1..1 (SDK 12.0 public API). */
public interface CRServo extends DcMotorSimple {
  ServoController getController();

  int getPortNumber();
}
