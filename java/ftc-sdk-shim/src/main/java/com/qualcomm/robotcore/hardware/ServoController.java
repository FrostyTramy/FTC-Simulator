package com.qualcomm.robotcore.hardware;

/** The servo controller of a hub (SDK 12.0 public API, subset). */
public interface ServoController extends HardwareDevice {
  enum PwmStatus {
    ENABLED,
    DISABLED,
    MIXED
  }

  void pwmEnable();

  void pwmDisable();

  PwmStatus getPwmStatus();

  void setServoPosition(int servo, double position);

  double getServoPosition(int servo);
}
