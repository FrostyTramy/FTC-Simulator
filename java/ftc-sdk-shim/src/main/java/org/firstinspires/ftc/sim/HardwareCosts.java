package org.firstinspires.ftc.sim;

/**
 * Simulated time each SDK hardware transaction takes (spec 06 "Timing"; values from
 * config/hardware_latency.yaml, sent by the simulator in the INIT message; defaults equal the YAML).
 */
public final class HardwareCosts {
  /** One Lynx command to the Control Hub's own hub (gm0: ~2 ms over UART). */
  public final long controlHubCommandNanos;
  /** One Lynx command to an Expansion Hub (gm0: ~3 ms over USB/RS485). */
  public final long expansionHubCommandNanos;
  /** One I2C read on the Control Hub (IMU), ASSUMED 3 ms. */
  public final long controlHubI2cReadNanos;
  /** Event-loop bookkeeping per OpMode loop iteration, ASSUMED 0.5 ms. */
  public final long loopOverheadNanos;
  /** Cost of a cheap SDK call that does not touch hardware (keeps busy-wait loops progressing). */
  public final long trivialCallNanos;
  /** Driver Station gamepad packet period, ASSUMED 20 ms. */
  public final long gamepadPeriodNanos;

  public HardwareCosts(
      long controlHubCommandNanos,
      long expansionHubCommandNanos,
      long controlHubI2cReadNanos,
      long loopOverheadNanos,
      long trivialCallNanos,
      long gamepadPeriodNanos) {
    this.controlHubCommandNanos = controlHubCommandNanos;
    this.expansionHubCommandNanos = expansionHubCommandNanos;
    this.controlHubI2cReadNanos = controlHubI2cReadNanos;
    this.loopOverheadNanos = loopOverheadNanos;
    this.trivialCallNanos = trivialCallNanos;
    this.gamepadPeriodNanos = gamepadPeriodNanos;
  }

  public static HardwareCosts defaults() {
    return new HardwareCosts(2_000_000L, 3_000_000L, 3_000_000L, 500_000L, 1_000L, 20_000_000L);
  }

  /** Cost of one command to the hub with this index (0 = Control Hub, 1 = Expansion Hub). */
  public long commandNanos(int hub) {
    return hub == 0 ? controlHubCommandNanos : expansionHubCommandNanos;
  }
}
