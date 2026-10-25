package org.firstinspires.ftc.sim.devices;

import com.qualcomm.robotcore.hardware.DigitalChannel;
import com.qualcomm.robotcore.hardware.DigitalChannelController;

/** One digital port configured as a "Digital Device" (DigitalChannel). */
public final class SimDigitalChannel implements DigitalChannel {
  private final SimDigitalController controller;
  private final int channel;

  public SimDigitalChannel(SimDigitalController controller, int channel) {
    this.controller = controller;
    this.channel = channel;
  }

  @Override
  public Mode getMode() {
    return controller.getDigitalChannelMode(channel);
  }

  @Override
  public void setMode(Mode mode) {
    controller.setDigitalChannelMode(channel, mode);
  }

  @Override
  @Deprecated
  public void setMode(DigitalChannelController.Mode mode) {
    setMode(mode.migrate());
  }

  @Override
  public boolean getState() {
    return controller.getDigitalChannelState(channel);
  }

  @Override
  public void setState(boolean state) {
    controller.setDigitalChannelState(channel, state);
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "Digital Channel";
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
