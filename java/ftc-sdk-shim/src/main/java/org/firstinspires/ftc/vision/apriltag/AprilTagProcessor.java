package org.firstinspires.ftc.vision.apriltag;

import java.util.ArrayList;
import org.firstinspires.ftc.robotcore.external.matrices.OpenGLMatrix;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.AxesOrder;
import org.firstinspires.ftc.robotcore.external.navigation.AxesReference;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Orientation;
import org.firstinspires.ftc.robotcore.external.navigation.Position;
import org.firstinspires.ftc.robotcore.external.navigation.YawPitchRollAngles;
import org.firstinspires.ftc.vision.VisionProcessor;

/** Finds AprilTags in camera frames and solves their poses (SDK 12.0 public API). */
public abstract class AprilTagProcessor implements VisionProcessor {
  public static final int THREADS_DEFAULT = 3;

  public static AprilTagProcessor easyCreateWithDefaults() {
    return new Builder().build();
  }

  public abstract void setDecimation(float decimation);

  public abstract void setPoseSolver(PoseSolver poseSolver);

  public abstract int getPerTagAvgPoseSolveTime();

  public abstract ArrayList<AprilTagDetection> getDetections();

  public abstract ArrayList<AprilTagDetection> getFreshDetections();

  /**
   * Builds the processor; defaults as in the SDK (36h11, the current game library, inches and degrees, camera at the
   * robot origin pointing straight up).
   */
  public static class Builder {
    private Position cameraPosition = new Position();
    private YawPitchRollAngles cameraOrientation = new YawPitchRollAngles(AngleUnit.DEGREES, 0, 0, 0, 0);
    private double fx;
    private double fy;
    private double cx;
    private double cy;
    private TagFamily tagFamily = TagFamily.TAG_36h11;
    private AprilTagLibrary tagLibrary = AprilTagGameDatabase.getCurrentGameTagLibrary();
    private DistanceUnit outputUnitsLength = DistanceUnit.INCH;
    private AngleUnit outputUnitsAngle = AngleUnit.DEGREES;
    private int threads = THREADS_DEFAULT;
    private boolean suppressCalibrationWarnings;
    private boolean drawAxes = true;
    private boolean drawCube = false;
    private boolean drawOutline = true;
    private boolean drawTagId = true;

    public Builder setCameraPose(Position position, YawPitchRollAngles orientation) {
      cameraPosition = position;
      cameraOrientation = orientation;
      return this;
    }

    public Builder setLensIntrinsics(double fx, double fy, double cx, double cy) {
      this.fx = fx;
      this.fy = fy;
      this.cx = cx;
      this.cy = cy;
      return this;
    }

    public Builder setSuppressCalibrationWarnings(boolean suppressCalibrationWarnings) {
      this.suppressCalibrationWarnings = suppressCalibrationWarnings;
      return this;
    }

    public Builder setTagFamily(TagFamily tagFamily) {
      this.tagFamily = tagFamily;
      return this;
    }

    public Builder setTagLibrary(AprilTagLibrary tagLibrary) {
      this.tagLibrary = tagLibrary;
      return this;
    }

    public Builder setOutputUnits(DistanceUnit distanceUnit, AngleUnit angleUnit) {
      outputUnitsLength = distanceUnit;
      outputUnitsAngle = angleUnit;
      return this;
    }

    public Builder setDrawAxes(boolean drawAxes) {
      this.drawAxes = drawAxes;
      return this;
    }

    public Builder setDrawCubeProjection(boolean drawCube) {
      this.drawCube = drawCube;
      return this;
    }

    public Builder setDrawTagOutline(boolean drawOutline) {
      this.drawOutline = drawOutline;
      return this;
    }

    public Builder setDrawTagID(boolean drawTagId) {
      this.drawTagId = drawTagId;
      return this;
    }

    public Builder setNumThreads(int threads) {
      this.threads = threads;
      return this;
    }

    /** The camera pose (yaw, pitch, roll as an intrinsic Z-X-Z rotation, as the SDK builds it) and the processor. */
    public AprilTagProcessor build() {
      if (tagLibrary == null) {
        throw new RuntimeException("Cannot create AprilTagProcessor without setting tag library!");
      }
      if (tagFamily == null) {
        throw new RuntimeException("Cannot create AprilTagProcessor without setting tag family!");
      }
      OpenGLMatrix cameraRotationMatrix = new Orientation(AxesReference.INTRINSIC, AxesOrder.ZXZ, AngleUnit.DEGREES,
          (float) cameraOrientation.getYaw(AngleUnit.DEGREES), (float) cameraOrientation.getPitch(AngleUnit.DEGREES),
          (float) cameraOrientation.getRoll(AngleUnit.DEGREES), cameraOrientation.getAcquisitionTime()).getRotationMatrix();
      Position p = cameraPosition.toUnit(DistanceUnit.INCH);
      OpenGLMatrix robotInCameraFrame = OpenGLMatrix.identityMatrix().translated((float) p.x, (float) p.y, (float) p.z)
          .multiplied(cameraRotationMatrix).inverted();
      return new AprilTagProcessorImpl(robotInCameraFrame, fx, fy, cx, cy, outputUnitsLength, outputUnitsAngle, tagLibrary,
          drawAxes, drawCube, drawOutline, drawTagId, tagFamily, threads, suppressCalibrationWarnings);
    }
  }

  /** Pose solvers (OpenCV solvePnP method codes). */
  public enum PoseSolver {
    APRILTAG_BUILTIN(-1),
    OPENCV_ITERATIVE(0),
    OPENCV_SOLVEPNP_EPNP(1),
    OPENCV_IPPE(6),
    OPENCV_IPPE_SQUARE(7),
    OPENCV_SQPNP(8);

    final int code;

    PoseSolver(int code) {
      this.code = code;
    }
  }

  /** Tag families of the AprilTag library. */
  public enum TagFamily {
    TAG_36h11("tag36h11"),
    TAG_25h9("tag25h9"),
    TAG_16h5("tag16h5"),
    TAG_standard41h12("tagStandard41h12");

    final String libraryName;

    TagFamily(String libraryName) {
      this.libraryName = libraryName;
    }
  }
}
