package org.firstinspires.ftc.sim.devices;

import com.qualcomm.robotcore.hardware.DcMotorController;

/** The motor controller of one hub (identity only; motors are controlled through their DcMotor). */
public final class SimMotorController implements DcMotorController {
  private final String name;

  public SimMotorController(String name) {
    this.name = name;
  }

  @Override
  public void resetDeviceConfigurationForOpMode(int motor) {}

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "Motor Controller";
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
