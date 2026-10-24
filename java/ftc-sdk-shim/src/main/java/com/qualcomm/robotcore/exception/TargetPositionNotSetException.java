package com.qualcomm.robotcore.exception;

/** Thrown when a motor is switched to RUN_TO_POSITION before setTargetPosition() was called (SDK 12.0). */
public class TargetPositionNotSetException extends RuntimeException {
  public TargetPositionNotSetException() {
    super("Failed to enable motor. You must set a target position before switching to RUN_TO_POSITION mode");
  }
}
