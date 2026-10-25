package com.qualcomm.robotcore.hardware;

/** The digital ports of a hub (SDK 12.0 public API, subset). */
public interface DigitalChannelController extends HardwareDevice {
  /** @deprecated use {@link DigitalChannel.Mode} */
  @Deprecated
  enum Mode {
    INPUT,
    OUTPUT;

    public DigitalChannel.Mode migrate() {
      return this == INPUT ? DigitalChannel.Mode.INPUT : DigitalChannel.Mode.OUTPUT;
    }
  }

  DigitalChannel.Mode getDigitalChannelMode(int channel);

  void setDigitalChannelMode(int channel, DigitalChannel.Mode mode);

  boolean getDigitalChannelState(int channel);

  void setDigitalChannelState(int channel, boolean state);
}
