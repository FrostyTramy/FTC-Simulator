package org.firstinspires.ftc.sim.devices;

import java.util.HashMap;
import java.util.Map;
import org.firstinspires.ftc.sim.DeviceBus;
import org.firstinspires.ftc.sim.SimRuntime;

/**
 * A simulated I2C sensor as its SDK driver sees it (spec 09). Every read or write is one I2C transaction on its hub
 * (time from config/hardware_latency.yaml). Reads return the device's latest values from the simulator (DeviceBus
 * sensor input; layout per device type in sim/Sim.Sensors); writes go to the simulator in the next STATE. A register
 * the driver wrote reads back as written until the simulator has reported a step that includes the write (two TICKs:
 * a write during tick k is applied after step k and first measured in the inputs of tick k+2).
 */
public final class SimI2cDevice {
  private final String name;
  private final int hub;
  private final int bus;
  private final int size;
  private final Map<Integer, double[]> written = new HashMap<>(); // snapshot index -> {value, tick of the write}

  public SimI2cDevice(String name, int hub, int bus, int size) {
    this.name = name;
    this.hub = hub;
    this.bus = bus;
    this.size = size;
  }

  public String name() {
    return name;
  }

  public int hub() {
    return hub;
  }

  public int bus() {
    return bus;
  }

  /** One read transaction: the device's values now. */
  public synchronized double[] read() {
    SimRuntime rt = SimRuntime.get();
    rt.consume(rt.costs().i2cReadNanos(hub));
    return values();
  }

  /** The values without a bus transaction (zeros before the simulator's first report). */
  public synchronized double[] values() {
    DeviceBus b = SimRuntime.get().bus();
    double[] v = b.sensorInput(name);
    if (v == null || v.length < size) {
      double[] padded = new double[size];
      if (v != null) {
        System.arraycopy(v, 0, padded, 0, v.length);
      }
      v = padded;
    }
    long now = b.ticks();
    written.entrySet().removeIf(e -> now - (long) e.getValue()[1] >= 2);
    for (Map.Entry<Integer, double[]> e : written.entrySet()) {
      v[e.getKey()] = e.getValue()[0];
    }
    return v;
  }

  /**
   * One write transaction: sends {@code command(args)} to the simulator. {@code shown} lists (snapshot index, value)
   * pairs the device holds from now on.
   */
  public synchronized void write(String command, double[] args, double... shown) {
    SimRuntime rt = SimRuntime.get();
    rt.consume(rt.costs().i2cWriteNanos(hub));
    rt.bus().sensorCommand(name, command, args);
    long now = rt.bus().ticks();
    for (int i = 0; i + 1 < shown.length; i += 2) {
      written.put((int) shown[i], new double[] {shown[i + 1], now});
    }
  }

  /** "I2C bus N" connection text like the SDK's. */
  public String connectionInfo() {
    return (hub == 0 ? "Control Hub" : "Expansion Hub 2") + "; I2C port " + bus;
  }
}
