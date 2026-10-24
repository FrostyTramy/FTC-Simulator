package org.firstinspires.ftc.sim.devices;

import com.qualcomm.robotcore.hardware.VoltageSensor;
import org.firstinspires.ftc.sim.SimHub;
import org.firstinspires.ftc.sim.SimRuntime;

/** Battery voltage as measured by a hub: the simulated bus voltage, one hub command per read. */
public final class SimVoltageSensor implements VoltageSensor {
  private final SimHub hub;
  private final String name;

  public SimVoltageSensor(SimHub hub, String name) {
    this.hub = hub;
    this.name = name;
  }

  @Override
  public double getVoltage() {
    hub.command();
    return SimRuntime.get().bus().batteryVolts();
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "Voltage Sensor";
  }

  @Override
  public String getConnectionInfo() {
    return name;
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
