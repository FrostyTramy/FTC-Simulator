package com.qualcomm.robotcore.hardware;

/** One digital port (SDK 12.0 public API). */
public interface DigitalChannel extends HardwareDevice {
  enum Mode {
    INPUT,
    OUTPUT
  }

  Mode getMode();

  void setMode(Mode mode);

  boolean getState();

  void setState(boolean state);

  /** @deprecated use {@link #setMode(Mode)} */
  @Deprecated
  void setMode(DigitalChannelController.Mode mode);
}
