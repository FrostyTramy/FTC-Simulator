package com.qualcomm.robotcore.hardware;

/** Motor controller loop algorithm (SDK 12.0 public API). */
public enum MotorControlAlgorithm {
  Unknown,
  LegacyPID,
  PIDF;

  public static MotorControlAlgorithm fromByte(byte b) {
    return b == 1 ? LegacyPID : b == 2 ? PIDF : Unknown;
  }
}
