package com.qualcomm.hardware.limelightvision;

import java.util.List;
import org.firstinspires.ftc.robotcore.external.navigation.Pose3D;

/** Result entries of a Limelight pipeline (SDK 12.0 public API, getters only). */
public class LLResultTypes {
  /** One AprilTag seen by a fiducial pipeline. */
  public static class FiducialResult {
    private final int fiducialId;
    private final String family;
    private final double targetXDegrees;
    private final double targetYDegrees;
    private final double targetArea;
    private final Pose3D robotPoseFieldSpace;
    private final Pose3D robotPoseTargetSpace;
    private final Pose3D cameraPoseTargetSpace;
    private final Pose3D targetPoseCameraSpace;
    private final Pose3D targetPoseRobotSpace;

    public FiducialResult(int fiducialId, String family, double targetXDegrees, double targetYDegrees, double targetArea,
        Pose3D robotPoseFieldSpace, Pose3D robotPoseTargetSpace, Pose3D cameraPoseTargetSpace,
        Pose3D targetPoseCameraSpace, Pose3D targetPoseRobotSpace) {
      this.fiducialId = fiducialId;
      this.family = family;
      this.targetXDegrees = targetXDegrees;
      this.targetYDegrees = targetYDegrees;
      this.targetArea = targetArea;
      this.robotPoseFieldSpace = robotPoseFieldSpace;
      this.robotPoseTargetSpace = robotPoseTargetSpace;
      this.cameraPoseTargetSpace = cameraPoseTargetSpace;
      this.targetPoseCameraSpace = targetPoseCameraSpace;
      this.targetPoseRobotSpace = targetPoseRobotSpace;
    }

    public int getFiducialId() { return fiducialId; }
    public String getFamily() { return family; }
    public double getTargetXDegrees() { return targetXDegrees; }
    public double getTargetYDegrees() { return targetYDegrees; }
    public double getTargetXDegreesNoCrosshair() { return targetXDegrees; }
    public double getTargetYDegreesNoCrosshair() { return targetYDegrees; }
    public double getTargetArea() { return targetArea; }
    public Pose3D getRobotPoseFieldSpace() { return robotPoseFieldSpace; }
    public Pose3D getRobotPoseTargetSpace() { return robotPoseTargetSpace; }
    public Pose3D getCameraPoseTargetSpace() { return cameraPoseTargetSpace; }
    public Pose3D getTargetPoseCameraSpace() { return targetPoseCameraSpace; }
    public Pose3D getTargetPoseRobotSpace() { return targetPoseRobotSpace; }
  }

  /** One colour blob of a colour pipeline. */
  public static class ColorResult {
    private final double targetXDegrees;
    private final double targetYDegrees;
    private final double targetArea;
    private final List<List<Double>> targetCorners;

    public ColorResult(double targetXDegrees, double targetYDegrees, double targetArea, List<List<Double>> targetCorners) {
      this.targetXDegrees = targetXDegrees;
      this.targetYDegrees = targetYDegrees;
      this.targetArea = targetArea;
      this.targetCorners = targetCorners;
    }

    public double getTargetXDegrees() { return targetXDegrees; }
    public double getTargetYDegrees() { return targetYDegrees; }
    public double getTargetXDegreesNoCrosshair() { return targetXDegrees; }
    public double getTargetYDegreesNoCrosshair() { return targetYDegrees; }
    public double getTargetArea() { return targetArea; }
    public List<List<Double>> getTargetCorners() { return targetCorners; }
  }

  /** One object of a neural detector pipeline. */
  public static class DetectorResult {
    private final String className;
    private final int classId;
    private final double confidence;
    private final double targetXDegrees;
    private final double targetYDegrees;
    private final double targetArea;

    public DetectorResult(String className, int classId, double confidence, double targetXDegrees, double targetYDegrees, double targetArea) {
      this.className = className;
      this.classId = classId;
      this.confidence = confidence;
      this.targetXDegrees = targetXDegrees;
      this.targetYDegrees = targetYDegrees;
      this.targetArea = targetArea;
    }

    public String getClassName() { return className; }
    public int getClassId() { return classId; }
    public double getConfidence() { return confidence; }
    public double getTargetXDegrees() { return targetXDegrees; }
    public double getTargetYDegrees() { return targetYDegrees; }
    public double getTargetArea() { return targetArea; }
  }

  /** The class of a neural classifier pipeline. */
  public static class ClassifierResult {
    private final String className;
    private final int classId;
    private final double confidence;

    public ClassifierResult(String className, int classId, double confidence) {
      this.className = className;
      this.classId = classId;
      this.confidence = confidence;
    }

    public String getClassName() { return className; }
    public int getClassId() { return classId; }
    public double getConfidence() { return confidence; }
  }

  /** One barcode of a barcode pipeline. */
  public static class BarcodeResult {
    private final String family;
    private final String data;

    public BarcodeResult(String family, String data) {
      this.family = family;
      this.data = data;
    }

    public String getFamily() { return family; }
    public String getData() { return data; }
  }
}
