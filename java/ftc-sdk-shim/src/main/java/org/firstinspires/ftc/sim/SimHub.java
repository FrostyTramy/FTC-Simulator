package org.firstinspires.ftc.sim;

import com.qualcomm.hardware.lynx.LynxModule;
import java.util.HashSet;
import java.util.Map;
import java.util.Set;
import java.util.function.Function;

/**
 * Bulk-read cache of one REV hub (spec 06 "Bulk caching"). A bulk read snapshots all motor inputs of
 * the hub at once. OFF: every read is its own transaction on live data. AUTO: the first read fills the
 * cache, reading a value a second time triggers a new bulk read. MANUAL: the snapshot stays until
 * {@code clearBulkCache()}, so data goes stale if the user forgets, exactly as on the real hub.
 */
public final class SimHub {
  private final int index;
  private final SimRuntime runtime;
  private LynxModule.BulkCachingMode mode = LynxModule.BulkCachingMode.OFF;
  private Map<String, DeviceBus.MotorInput> snapshot;
  private Map<String, double[]> sensorSnapshot;
  private final Set<String> taken = new HashSet<>();

  public SimHub(int index, SimRuntime runtime) {
    this.index = index;
    this.runtime = runtime;
  }

  public int index() {
    return index;
  }

  public LynxModule.BulkCachingMode mode() {
    return mode;
  }

  public void setMode(LynxModule.BulkCachingMode mode) {
    this.mode = mode;
    clear();
  }

  /** LynxModule.clearBulkCache(): the next read performs a new bulk read. */
  public void clear() {
    snapshot = null;
    sensorSnapshot = null;
    taken.clear();
  }

  /** One hub command (writes, uncached reads). */
  public void command() {
    runtime.consume(runtime.costs().commandNanos(index));
  }

  /**
   * Reads one bulk-data value. {@code key} identifies the value (e.g. "port0.position") for the AUTO
   * "read twice" rule; {@code extract} picks it from a set of motor inputs.
   */
  public <T> T read(String key, Function<Map<String, DeviceBus.MotorInput>, T> extract) {
    switch (mode) {
      case OFF:
        command();
        return extract.apply(runtime.bus().copyMotorInputs());
      case AUTO:
        if (snapshot == null || taken.contains(key)) {
          refill();
        }
        taken.add(key);
        return extract.apply(snapshot);
      default: // MANUAL
        if (snapshot == null) {
          refill();
        }
        return extract.apply(snapshot);
    }
  }

  /**
   * Reads the value of a digital input wired to a simulated sensor; part of the same bulk data as the motor inputs,
   * so the caching mode applies the same way.
   */
  public double[] readSensor(String sensorName) {
    String key = "sensor:" + sensorName;
    switch (mode) {
      case OFF:
        command();
        return runtime.bus().sensorInput(sensorName);
      case AUTO:
        if (snapshot == null || taken.contains(key)) {
          refill();
        }
        taken.add(key);
        return sensorSnapshot.get(sensorName);
      default: // MANUAL
        if (snapshot == null) {
          refill();
        }
        return sensorSnapshot.get(sensorName);
    }
  }

  private void refill() {
    command(); // one bulk read transaction
    snapshot = runtime.bus().copyMotorInputs();
    sensorSnapshot = runtime.bus().copySensorInputs();
    taken.clear();
  }
}
