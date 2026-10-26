package com.qualcomm.hardware.limelightvision;

import com.qualcomm.robotcore.hardware.HardwareDevice;

/**
 * Limelight 3A smart camera (SDK 12.0 public API). D-039: the device is in the hardware map under its configured name
 * and takes every call team code makes (pipelines, polling, robot orientation for MegaTag 2), but the simulator does
 * not render the Limelight's camera yet: every result reports no target ({@link LLResult#isValid()} false), as a
 * Limelight that sees nothing does.
 */
public class Limelight3A implements HardwareDevice {
  private final String name;
  private volatile boolean running;
  private volatile int pipeline;
  private volatile int pollRateHz = 100;
  private volatile double robotYaw;

  public Limelight3A(String name) {
    this.name = name;
  }

  public void setPollRateHz(int rateHz) { pollRateHz = Math.max(1, Math.min(250, rateHz)); }
  public boolean isRunning() { return running; }
  public boolean isConnected() { return true; }
  public void start() { running = true; }
  public void pause() { running = false; }
  public void stop() { running = false; }
  public void shutdown() { running = false; }

  public boolean pipelineSwitch(int index) {
    if (index < 0 || index > 9) {
      return false;
    }
    pipeline = index;
    return true;
  }

  public boolean reloadPipeline() { return true; }

  /** The latest result: no target in the simulator (see the class comment). */
  public LLResult getLatestResult() {
    return LLResult.noTarget(pipeline, org.firstinspires.ftc.sim.SimRuntime.get().nowNanos() / 1_000_000);   // simulated clock (determinism)
  }

  public LLStatus getStatus() {
    return new LLStatus(name, pipeline, "pipe_fiducial", running ? pollRateHz : 0);
  }

  public long getTimeSinceLastUpdate() { return running ? 1000 / pollRateHz : Long.MAX_VALUE; }
  public boolean updateRobotOrientation(double yaw) { robotYaw = yaw; return true; }
  public boolean updatePythonInputs(double[] inputs) { return true; }
  public boolean updatePythonInputs(double a, double b, double c, double d, double e, double f, double g, double h) { return true; }
  public boolean captureSnapshot(String snapshotName) { return true; }
  public boolean deleteSnapshots() { return true; }
  public boolean deleteSnapshot(String snapshotName) { return true; }
  public boolean uploadPipeline(String jsonString, Integer index) { return true; }
  public boolean uploadPython(String pythonString, Integer index) { return true; }

  @Override public Manufacturer getManufacturer() { return Manufacturer.LimelightVision; }
  @Override public String getDeviceName() { return "Limelight 3A"; }
  @Override public String getConnectionInfo() { return "USB (simulated, no camera image)"; }
  @Override public int getVersion() { return 1; }
  @Override public void resetDeviceConfigurationForOpMode() {}
  @Override public void close() { running = false; }
}
