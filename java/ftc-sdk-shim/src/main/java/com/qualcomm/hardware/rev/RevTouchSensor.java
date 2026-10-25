package com.qualcomm.hardware.rev;

import com.qualcomm.robotcore.hardware.DigitalChannelController;
import com.qualcomm.robotcore.hardware.TouchSensor;

/** REV Touch Sensor on a digital port (SDK 12.0 public API): the input reads low while the button is pressed. */
public class RevTouchSensor implements TouchSensor {
  private final DigitalChannelController controller;
  private final int channel;

  public RevTouchSensor(DigitalChannelController controller, int channel) {
    this.controller = controller;
    this.channel = channel;
  }

  @Override
  public double getValue() {
    return isPressed() ? 1.0 : 0.0;
  }

  @Override
  public boolean isPressed() {
    return !controller.getDigitalChannelState(channel);
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "REV Touch Sensor";
  }

  @Override
  public String getConnectionInfo() {
    return controller.getConnectionInfo() + "; digital channel " + channel;
  }

  @Override
  public int getVersion() {
    return 1;
  }

  @Override
  public void resetDeviceConfigurationForOpMode() {}

  @Override
  public void close() {}
}
