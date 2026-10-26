package com.qualcomm.robotcore.hardware;

/**
 * An analog input port of a REV hub, 0 to 3.3 V (SDK 12.0 public API). D-039: configured in the robot YAML as a sensor
 * of type analog; the simulator does not drive a voltage into it yet, so it reads its configured idle voltage
 * (params.volts, default 0).
 */
public class AnalogInput implements HardwareDevice {
  private final String name;
  private final org.firstinspires.ftc.sim.SimHub hub;
  private final int port;
  private final double idleVolts;

  public AnalogInput(String name, org.firstinspires.ftc.sim.SimHub hub, int port, double idleVolts) {
    this.name = name;
    this.hub = hub;
    this.port = port;
    this.idleVolts = idleVolts;
  }

  /** The voltage on the port, V: part of the hub's bulk data, so the bulk caching mode applies. */
  public double getVoltage() {
    double[] v = hub.readSensor(name);
    return v != null && v.length > 0 ? v[0] : idleVolts;
  }

  public double getMaxVoltage() {
    return 3.3;
  }

  @Override public Manufacturer getManufacturer() { return Manufacturer.Lynx; }
  @Override public String getDeviceName() { return "Analog Input"; }
  @Override public String getConnectionInfo() { return (hub.index() == 0 ? "Control Hub" : "Expansion Hub 2") + "; analog port " + port; }
  @Override public int getVersion() { return 1; }
  @Override public void resetDeviceConfigurationForOpMode() {}
  @Override public void close() {}
}
