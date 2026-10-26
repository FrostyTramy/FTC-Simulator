package com.bylazar.telemetry;

import java.util.ArrayList;
import java.util.List;
import org.firstinspires.ftc.robotcore.external.Telemetry;

/**
 * Panels telemetry (com.bylazar, D-039): lines and key/value data a team sends to the Panels web page, and with
 * {@link #update(Telemetry)} also to the Driver Station. The simulator has no Panels page yet, so the data reaches the
 * Driver Station telemetry only through {@link #update(Telemetry)}, as on the robot.
 */
public class TelemetryManager {
  private final List<String> lines = new ArrayList<>();

  /** Each argument on its own line (Panels' debug view). */
  public void debug(Object... data) {
    for (Object d : data) {
      lines.add(String.valueOf(d));
    }
  }

  public void addData(String key, Object value) {
    lines.add(key + ": " + value);
  }

  public void addLine(String line) {
    lines.add(line);
  }

  /** Sends the data to Panels and clears it. */
  public void update() {
    lines.clear();
  }

  /** Sends the data to Panels and to the Driver Station telemetry, then clears it. */
  public void update(Telemetry telemetry) {
    if (telemetry != null) {
      for (String line : lines) {
        telemetry.addLine(line);
      }
      telemetry.update();
    }
    lines.clear();
  }
}
