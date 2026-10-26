package com.qualcomm.hardware.limelightvision;

import java.util.ArrayList;
import java.util.List;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Pose3D;
import org.firstinspires.ftc.robotcore.external.navigation.Position;
import org.firstinspires.ftc.robotcore.external.navigation.YawPitchRollAngles;

/** One Limelight result (SDK 12.0 public API, getters). */
public class LLResult {
  private final boolean valid;
  private final double tx;
  private final double ty;
  private final double ta;
  private final int pipelineIndex;
  private final long controlHubTimeStampMs;
  private final Pose3D botpose;
  private final Pose3D botposeMt2;
  private final List<LLResultTypes.FiducialResult> fiducials;

  public LLResult(boolean valid, double tx, double ty, double ta, int pipelineIndex, long controlHubTimeStampMs, Pose3D botpose,
      Pose3D botposeMt2, List<LLResultTypes.FiducialResult> fiducials) {
    this.valid = valid;
    this.tx = tx;
    this.ty = ty;
    this.ta = ta;
    this.pipelineIndex = pipelineIndex;
    this.controlHubTimeStampMs = controlHubTimeStampMs;
    this.botpose = botpose;
    this.botposeMt2 = botposeMt2;
    this.fiducials = fiducials;
  }

  /** A result without a target (what a Limelight that sees nothing reports). */
  static LLResult noTarget(int pipelineIndex, long nowMs) {
    Pose3D zero = new Pose3D(new Position(DistanceUnit.METER, 0, 0, 0, 0), new YawPitchRollAngles(AngleUnit.DEGREES, 0, 0, 0, 0));
    return new LLResult(false, 0, 0, 0, pipelineIndex, nowMs, zero, zero, new ArrayList<>());
  }

  /** A target was found and the data is usable. */
  public boolean isValid() { return valid; }
  public double getTx() { return tx; }
  public double getTy() { return ty; }
  public double getTa() { return ta; }
  public double getTxNC() { return tx; }
  public double getTyNC() { return ty; }
  public int getPipelineIndex() { return pipelineIndex; }
  public Pose3D getBotpose() { return botpose; }
  public Pose3D getBotpose_MT2() { return botposeMt2; }
  public int getBotposeTagCount() { return valid ? fiducials.size() : 0; }
  public double getBotposeSpan() { return 0; }
  public double getBotposeAvgDist() { return 0; }
  public double getBotposeAvgArea() { return valid ? ta : 0; }
  public double[] getStddevMt1() { return new double[6]; }
  public double[] getStddevMt2() { return new double[6]; }
  public double getCaptureLatency() { return 0; }
  public double getTargetingLatency() { return 0; }
  public double getParseLatency() { return 0; }
  public double getTimestamp() { return controlHubTimeStampMs / 1000.0; }
  public long getControlHubTimeStamp() { return controlHubTimeStampMs; }
  /** Milliseconds since this result was produced. */
  public long getStaleness() { return 0; }
  public double[] getPythonOutput() { return new double[0]; }
  public List<LLResultTypes.FiducialResult> getFiducialResults() { return fiducials; }
  public List<LLResultTypes.ColorResult> getColorResults() { return new ArrayList<>(); }
  public List<LLResultTypes.DetectorResult> getDetectorResults() { return new ArrayList<>(); }
  public List<LLResultTypes.ClassifierResult> getClassifierResults() { return new ArrayList<>(); }
  public List<LLResultTypes.BarcodeResult> getBarcodeResults() { return new ArrayList<>(); }

  @Override
  public String toString() {
    return "LLResult{valid=" + valid + ", tx=" + tx + ", ty=" + ty + ", ta=" + ta + ", pipeline=" + pipelineIndex + "}";
  }
}
