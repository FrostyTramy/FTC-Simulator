package com.bylazar.telemetry;

/** Panels telemetry plugin (com.bylazar, D-039): {@code PanelsTelemetry.INSTANCE.getTelemetry()} as in Kotlin. */
public final class PanelsTelemetry {
  public static final PanelsTelemetry INSTANCE = new PanelsTelemetry();

  private final TelemetryManager telemetry = new TelemetryManager();

  private PanelsTelemetry() {}

  public TelemetryManager getTelemetry() {
    return telemetry;
  }
}
