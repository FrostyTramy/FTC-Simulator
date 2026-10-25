package org.firstinspires.ftc.sim.devices;

import com.qualcomm.robotcore.hardware.DigitalChannel;
import com.qualcomm.robotcore.hardware.DigitalChannelController;
import java.util.HashMap;
import java.util.Map;
import org.firstinspires.ftc.sim.SimHub;

/**
 * The digital ports of one hub. A port wired to a simulated contact sensor reads its state through the bulk data of
 * the hub (the bulk-cache mode applies, like motor encoders); an input with nothing attached reads high (pull-up).
 */
public final class SimDigitalController implements DigitalChannelController {
  private final SimHub hub;
  private final String hubName;
  private final Map<Integer, String> sensors = new HashMap<>();
  private final Map<Integer, DigitalChannel.Mode> modes = new HashMap<>();
  private final Map<Integer, Boolean> outputs = new HashMap<>();

  public SimDigitalController(SimHub hub, String hubName) {
    this.hub = hub;
    this.hubName = hubName;
  }

  /** Wires a simulated sensor (snapshot value 1 = pressed / contact) to a port. */
  public void attach(int channel, String sensorName) {
    sensors.put(channel, sensorName);
  }

  @Override
  public DigitalChannel.Mode getDigitalChannelMode(int channel) {
    return modes.getOrDefault(channel, DigitalChannel.Mode.INPUT);
  }

  @Override
  public void setDigitalChannelMode(int channel, DigitalChannel.Mode mode) {
    hub.command();
    modes.put(channel, mode);
  }

  @Override
  public boolean getDigitalChannelState(int channel) {
    if (getDigitalChannelMode(channel) == DigitalChannel.Mode.OUTPUT) {
      return outputs.getOrDefault(channel, false);
    }
    String sensor = sensors.get(channel);
    if (sensor == null) {
      hub.command();
      return true;
    }
    double[] v = hub.readSensor(sensor);
    return v == null || v.length == 0 || v[0] == 0; // a pressed button pulls the input low
  }

  @Override
  public void setDigitalChannelState(int channel, boolean state) {
    hub.command();
    if (getDigitalChannelMode(channel) == DigitalChannel.Mode.OUTPUT) {
      outputs.put(channel, state);
    }
  }

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "Lynx Digital Controller";
  }

  @Override
  public String getConnectionInfo() {
    return hubName;
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
