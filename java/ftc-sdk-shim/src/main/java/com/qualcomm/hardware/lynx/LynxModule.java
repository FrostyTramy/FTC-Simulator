package com.qualcomm.hardware.lynx;

import com.qualcomm.robotcore.hardware.HardwareDevice;
import org.firstinspires.ftc.robotcore.external.navigation.CurrentUnit;
import org.firstinspires.ftc.robotcore.external.navigation.VoltageUnit;
import org.firstinspires.ftc.sim.SimHub;
import org.firstinspires.ftc.sim.SimRuntime;

/**
 * A REV Control Hub or Expansion Hub (SDK 12.0 public API, the subset OpModes use: bulk caching and
 * hub voltage/current). Obtained with {@code hardwareMap.getAll(LynxModule.class)}.
 */
public class LynxModule implements HardwareDevice {
  public enum BulkCachingMode {
    OFF,
    MANUAL,
    AUTO
  }

  private final SimHub hub;
  private final String name;
  private final boolean parent;

  public LynxModule(SimHub hub, String name, boolean parent) {
    this.hub = hub;
    this.name = name;
    this.parent = parent;
  }

  public SimHub simHub() {
    return hub;
  }

  public BulkCachingMode getBulkCachingMode() {
    return hub.mode();
  }

  public void setBulkCachingMode(BulkCachingMode mode) {
    hub.command();
    hub.setMode(mode);
  }

  /** Discards the bulk cache: the next read of a cached value does a new bulk read. */
  public void clearBulkCache() {
    hub.clear();
  }

  /** True for the hub the Robot Controller talks to directly (the Control Hub). */
  public boolean isParent() {
    return parent;
  }

  public int getModuleAddress() {
    return parent ? 173 : 2;
  }

  public double getInputVoltage(VoltageUnit unit) {
    hub.command();
    return unit.convert(SimRuntime.get().bus().batteryVolts(), VoltageUnit.VOLTS);
  }

  public double getCurrent(CurrentUnit unit) {
    hub.command();
    double total = 0;
    for (var input : SimRuntime.get().bus().copyMotorInputs().entrySet()) {
      if (input.getKey().startsWith(hub.index() + ":")) {
        total += Math.abs(input.getValue().currentAmps);
      }
    }
    return unit.convert(total, CurrentUnit.AMPS);
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return parent ? "REV Control Hub" : "REV Expansion Hub";
  }

  @Override
  public String getConnectionInfo() {
    return name + "; module " + getModuleAddress();
  }

  @Override
  public int getVersion() {
    return 1;
  }

  @Override
  public void resetDeviceConfigurationForOpMode() {
    hub.setMode(BulkCachingMode.OFF);
  }

  @Override
  public void close() {}
}
