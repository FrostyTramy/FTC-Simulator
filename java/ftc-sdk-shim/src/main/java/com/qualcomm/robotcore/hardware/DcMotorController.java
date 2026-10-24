package com.qualcomm.robotcore.hardware;

/** The motor controller a motor is connected to (SDK 12.0 public API, subset used by OpModes). */
public interface DcMotorController extends HardwareDevice {
  /** Bulk-read data timestamp etc. are not exposed here; motors are controlled through {@link DcMotor}. */
  void resetDeviceConfigurationForOpMode(int motor);
}
