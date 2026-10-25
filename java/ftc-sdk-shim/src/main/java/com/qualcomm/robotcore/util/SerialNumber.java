package com.qualcomm.robotcore.util;

/** A device serial number (SDK 12.0 public API, subset). */
public class SerialNumber {
  protected final String serialNumberString;

  protected SerialNumber(String serialNumberString) {
    this.serialNumberString = serialNumberString;
  }

  public static SerialNumber fromString(String serialNumberString) {
    return new SerialNumber(serialNumberString);
  }

  public boolean isWebcam() {
    return serialNumberString.startsWith("Webcam:");
  }

  public String getString() {
    return serialNumberString;
  }

  @Override
  public boolean equals(Object o) {
    return o instanceof SerialNumber && ((SerialNumber) o).serialNumberString.equals(serialNumberString);
  }

  @Override
  public int hashCode() {
    return serialNumberString.hashCode();
  }

  @Override
  public String toString() {
    return serialNumberString;
  }
}
