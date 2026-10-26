package com.qualcomm.hardware.limelightvision;

/** Limelight device status (SDK 12.0 public API, getters). */
public class LLStatus {
  private final String name;
  private final int pipelineIndex;
  private final String pipelineType;
  private final double fps;

  public LLStatus(String name, int pipelineIndex, String pipelineType, double fps) {
    this.name = name;
    this.pipelineIndex = pipelineIndex;
    this.pipelineType = pipelineType;
    this.fps = fps;
  }

  public String getName() { return name; }
  public int getPipelineIndex() { return pipelineIndex; }
  public String getPipelineType() { return pipelineType; }
  public double getFps() { return fps; }
  public double getCpu() { return 0; }
  public double getRam() { return 0; }
  public double getTemp() { return 0; }
  public int getCid() { return 0; }
  public double getFinalYaw() { return 0; }
  public int getSnapshotMode() { return 0; }

  @Override
  public String toString() {
    return "LLStatus{name=" + name + ", pipeline=" + pipelineIndex + ", fps=" + fps + "}";
  }
}
