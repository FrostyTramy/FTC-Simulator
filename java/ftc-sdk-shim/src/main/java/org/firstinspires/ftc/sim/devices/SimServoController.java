package org.firstinspires.ftc.sim.devices;

import com.qualcomm.robotcore.hardware.ServoController;
import java.util.HashMap;
import java.util.Map;
import org.firstinspires.ftc.sim.DeviceBus;
import org.firstinspires.ftc.sim.SimHub;
import org.firstinspires.ftc.sim.SimRuntime;

/**
 * The servo controller of one hub: turns servo positions 0..1 into PWM pulses with the SDK's default
 * PwmRange (600–2400 µs, config/servos.yaml sdk_servo) and enables/disables the PWM outputs.
 */
public final class SimServoController implements ServoController {
  public static final double PULSE_LOWER_US = 600;
  public static final double PULSE_UPPER_US = 2400;

  private final SimHub hub;
  private final String name;
  private final Map<Integer, Double> positions = new HashMap<>();
  private boolean pwmEnabled = true;

  public SimServoController(SimHub hub, String name) {
    this.hub = hub;
    this.name = name;
  }

  private DeviceBus.ServoCommand command(int port) {
    return SimRuntime.get().bus().servoCommand(hub.index() + ":" + port);
  }

  @Override
  public void pwmEnable() {
    pwmEnabled = true;
    for (int port : positions.keySet()) {
      command(port).pwmEnabled = true;
    }
    hub.command();
  }

  @Override
  public void pwmDisable() {
    pwmEnabled = false;
    for (int port : positions.keySet()) {
      command(port).pwmEnabled = false;
    }
    hub.command();
  }

  @Override
  public PwmStatus getPwmStatus() {
    return pwmEnabled ? PwmStatus.ENABLED : PwmStatus.DISABLED;
  }

  /** Raw servo position 0..1 (after the Servo's direction and scaleRange). Skips unchanged writes. */
  @Override
  public void setServoPosition(int servo, double position) {
    double p = Math.max(0, Math.min(1, position));
    Double last = positions.put(servo, p);
    DeviceBus.ServoCommand c = command(servo);
    boolean wasDisabled = !c.pwmEnabled;
    c.pulseUs = PULSE_LOWER_US + p * (PULSE_UPPER_US - PULSE_LOWER_US);
    c.pwmEnabled = true; // setting a position enables the PWM output, as on the hub
    pwmEnabled = true;
    if (last == null || last != p || wasDisabled) {
      hub.command();
    }
  }

  @Override
  public double getServoPosition(int servo) {
    Double p = positions.get(servo);
    return p == null ? Double.NaN : p;
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "Servo Controller";
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
