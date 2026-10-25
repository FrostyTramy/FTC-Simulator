package org.firstinspires.ftc.vision.apriltag;

import android.graphics.Canvas;
import android.util.Log;
import com.qualcomm.robotcore.util.MovingStatistics;
import com.qualcomm.robotcore.util.RobotLog;
import java.util.ArrayList;
import java.util.List;
import org.firstinspires.ftc.robotcore.external.matrices.GeneralMatrixF;
import org.firstinspires.ftc.robotcore.external.matrices.MatrixF;
import org.firstinspires.ftc.robotcore.external.matrices.OpenGLMatrix;
import org.firstinspires.ftc.robotcore.external.matrices.VectorF;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.AxesOrder;
import org.firstinspires.ftc.robotcore.external.navigation.AxesReference;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Orientation;
import org.firstinspires.ftc.robotcore.external.navigation.Pose3D;
import org.firstinspires.ftc.robotcore.external.navigation.Position;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;
import org.firstinspires.ftc.robotcore.external.navigation.YawPitchRollAngles;
import org.firstinspires.ftc.robotcore.internal.camera.calibration.CameraCalibration;
import org.firstinspires.ftc.robotcore.internal.camera.calibration.CameraCalibrationHelper;
import org.firstinspires.ftc.robotcore.internal.camera.calibration.PlaceholderCalibratedAspectRatioMismatch;
import org.firstinspires.ftc.sim.SimRuntime;
import org.firstinspires.ftc.sim.vision.NativeAprilTagUser;
import org.firstinspires.ftc.sim.vision.SimFrame;
import org.firstinspires.ftc.sim.vision.SimVision;
import org.opencv.calib3d.Calib3d;
import org.opencv.core.CvType;
import org.opencv.core.Mat;
import org.opencv.core.MatOfDouble;
import org.opencv.core.MatOfPoint2f;
import org.opencv.core.MatOfPoint3f;
import org.opencv.core.Point;
import org.opencv.core.Point3;

/**
 * The SDK 12.0 AprilTag processor (behaviour of Vision-12.0.0 AprilTagProcessorImpl): tags of a cluster are solved
 * together (all corners in one solvePnP), single tags one by one, poses with OpenCV solvePnP, the robot pose through
 * the camera pose given to the builder. The only difference: the AprilTag 3 detection runs in the simulator on the
 * rendered frame (the same native library), and its results arrive with the frame. Consequences: a new decimation
 * applies to frames captured after it is set; the APRILTAG_BUILTIN pose solver is approximated with OpenCV
 * IPPE_SQUARE.
 */
public class AprilTagProcessorImpl extends AprilTagProcessor implements NativeAprilTagUser {
  public static final String TAG = "AprilTagProcessorImpl";
  private ArrayList<AprilTagDetection> detections = new ArrayList<>();
  private ArrayList<AprilTagDetection> detectionsUpdate = new ArrayList<>();
  private final Object detectionsUpdateSync = new Object();
  private final boolean drawAxes;
  private final boolean drawCube;
  private final boolean drawOutline;
  private final boolean drawTagID;
  private Mat cameraMatrix;
  private double fx;
  private double fy;
  private double cx;
  private double cy;
  private final boolean suppressCalibrationWarnings;
  private final AprilTagLibrary tagLibrary;
  private final TagFamily tagFamily;
  private float decimation = 3.0f; // the SDK creates its native detector with decimation 3
  private boolean needToSetDecimation;
  private final Object decimationSync = new Object();
  private AprilTagCanvasAnnotator canvasAnnotator;
  private final DistanceUnit outputUnitsLength;
  private final AngleUnit outputUnitsAngle;
  private volatile PoseSolver poseSolver = PoseSolver.OPENCV_ITERATIVE;
  private final OpenGLMatrix robotInCameraFrame;
  private final MovingStatistics solveTime = new MovingStatistics(50);
  private final Object drawSync = new Object();
  private boolean builtinWarned;

  public AprilTagProcessorImpl(OpenGLMatrix robotInCameraFrame, double fx, double fy, double cx, double cy,
      DistanceUnit outputUnitsLength, AngleUnit outputUnitsAngle, AprilTagLibrary tagLibrary, boolean drawAxes,
      boolean drawCube, boolean drawOutline, boolean drawTagID, TagFamily tagFamily, int threads,
      boolean suppressCalibrationWarnings) {
    this.robotInCameraFrame = robotInCameraFrame;
    this.fx = fx;
    this.fy = fy;
    this.cx = cx;
    this.cy = cy;
    this.tagLibrary = tagLibrary;
    this.outputUnitsLength = outputUnitsLength;
    this.outputUnitsAngle = outputUnitsAngle;
    this.suppressCalibrationWarnings = suppressCalibrationWarnings;
    this.drawAxes = drawAxes;
    this.drawCube = drawCube;
    this.drawOutline = drawOutline;
    this.drawTagID = drawTagID;
    this.tagFamily = tagFamily;
  }

  @Override
  public float requestedDecimation() {
    synchronized (decimationSync) {
      return decimation;
    }
  }

  @Override
  public String requestedFamily() {
    return tagFamily.libraryName;
  }

  @Override
  public void init(int width, int height, CameraCalibration calibration) {
    if (fx != 0.0 && fy != 0.0 && cx != 0.0 && cy != 0.0) {
      Log.d(TAG, String.format("User provided their own camera calibration fx=%7.3f fy=%7.3f cx=%7.3f cy=%7.3f", fx, fy,
          cx, cy));
    } else if (calibration != null && !calibration.isDegenerate()) {
      fx = calibration.focalLengthX;
      fy = calibration.focalLengthY;
      cx = calibration.principalPointX;
      cy = calibration.principalPointY;
      if (calibration.resolutionScaledFrom != null) {
        String msg = String.format("Camera has not been calibrated for [%dx%d]; applying a scaled calibration from [%dx%d].",
            width, height, calibration.resolutionScaledFrom.getWidth(), calibration.resolutionScaledFrom.getHeight());
        Log.d(TAG, msg);
        if (!suppressCalibrationWarnings) {
          RobotLog.addGlobalWarningMessage(msg);
        }
      }
    } else {
      if (calibration instanceof PlaceholderCalibratedAspectRatioMismatch) {
        StringBuilder supported = new StringBuilder();
        for (CameraCalibration cal : CameraCalibrationHelper.getInstance().getCalibrations(calibration.getIdentity())) {
          supported.append(String.format("[%dx%d],", cal.getSize().getWidth(), cal.getSize().getHeight()));
        }
        String msg = String.format("Camera has not been calibrated for [%dx%d]. Pose estimates will likely be inaccurate. "
            + "However, there are built in calibrations for resolutions: %s", width, height, supported);
        Log.d(TAG, msg);
        if (!suppressCalibrationWarnings) {
          RobotLog.addGlobalWarningMessage(msg);
        }
      } else {
        String warning = "User did not provide a camera calibration, nor was a built-in calibration found for this camera. "
            + "Pose estimates will likely be inaccurate.";
        Log.d(TAG, warning);
        if (!suppressCalibrationWarnings) {
          RobotLog.addGlobalWarningMessage(warning);
        }
      }
      fx = 578.272;
      fy = 578.272;
      cx = width / 2;
      cy = height / 2;
    }
    constructMatrix();
    canvasAnnotator = new AprilTagCanvasAnnotator(cameraMatrix);
  }

  @Override
  public Object processFrame(Mat input, long captureTimeNanos) {
    SimFrame frame = SimVision.processingFrame();
    synchronized (decimationSync) {
      if (needToSetDecimation && frame != null) {
        SimRuntime.get().bus().sensorCommand(frame.camera(), "apriltag", 1, decimation, familyCode());
        needToSetDecimation = false;
      }
    }
    detections = runAprilTagDetectorForMultipleTagSizes(frame, captureTimeNanos);
    synchronized (detectionsUpdateSync) {
      detectionsUpdate = detections;
    }
    return detections;
  }

  private double familyCode() {
    switch (tagFamily) {
      case TAG_25h9:
        return 1;
      case TAG_16h5:
        return 2;
      case TAG_standard41h12:
        return 3;
      default:
        return 0;
    }
  }

  private AprilTagPoseRaw doClusterSolve(TagClusterNotebook n, PoseSolver solver) {
    final int cornersPerTag = 4;
    Point[] imagePts = new Point[n.detections.size() * cornersPerTag];
    Point3[] idealProjectionPts = new Point3[imagePts.length];
    for (int tag = 0; tag < n.detections.size(); tag++) {
      PreliminaryTagDetection det = n.detections.get(tag);
      AprilTagClusterMemberMetadata metadata = n.cluster.getMemberMetadata(det.id);
      double tagsize = outputUnitsLength.fromUnit(n.cluster.distanceUnit, metadata.tagsize);
      double offsetX = outputUnitsLength.fromUnit(n.cluster.distanceUnit, metadata.positionInClusterPlane.get(0));
      double offsetY = outputUnitsLength.fromUnit(n.cluster.distanceUnit, metadata.positionInClusterPlane.get(1));
      double offsetZ = outputUnitsLength.fromUnit(n.cluster.distanceUnit, metadata.positionInClusterPlane.get(2));
      imagePts[tag * 4] = det.corners[0];
      imagePts[tag * 4 + 1] = det.corners[1];
      imagePts[tag * 4 + 2] = det.corners[2];
      imagePts[tag * 4 + 3] = det.corners[3];
      idealProjectionPts[tag * 4] = new Point3(-tagsize / 2.0 + offsetX, tagsize / 2.0 + offsetY, offsetZ);
      idealProjectionPts[tag * 4 + 1] = new Point3(tagsize / 2.0 + offsetX, tagsize / 2.0 + offsetY, offsetZ);
      idealProjectionPts[tag * 4 + 2] = new Point3(tagsize / 2.0 + offsetX, -tagsize / 2.0 + offsetY, offsetZ);
      idealProjectionPts[tag * 4 + 3] = new Point3(-tagsize / 2.0 + offsetX, -tagsize / 2.0 + offsetY, offsetZ);
    }
    Pose opencvPose = poseFromNGrt4Pts(imagePts, idealProjectionPts, cameraMatrix, solverCode(solver));
    return rawPose(opencvPose);
  }

  private AprilTagPoseRaw doSingleTagPoseSolve(AprilTagMetadata metadata, PoseSolver solver, Point[] cornerPts) {
    long startSolveTime = System.currentTimeMillis();
    Pose opencvPose = poseFromTrapezoid(cornerPts, cameraMatrix, outputUnitsLength.fromUnit(metadata.distanceUnit,
        metadata.tagsize), solverCode(solver));
    AprilTagPoseRaw rawPose = rawPose(opencvPose);
    solveTime.add(System.currentTimeMillis() - startSolveTime);
    return rawPose;
  }

  /** OpenCV method code; APRILTAG_BUILTIN (the native library's solver) is approximated with IPPE_SQUARE. */
  private int solverCode(PoseSolver solver) {
    if (solver == PoseSolver.APRILTAG_BUILTIN) {
      if (!builtinWarned) {
        builtinWarned = true;
        RobotLog.ww(TAG, "simulator: APRILTAG_BUILTIN pose solver approximated with OpenCV IPPE_SQUARE");
      }
      return PoseSolver.OPENCV_IPPE_SQUARE.code;
    }
    return solver.code;
  }

  private static AprilTagPoseRaw rawPose(Pose opencvPose) {
    Mat r = new Mat(3, 3, CvType.CV_32F);
    Calib3d.Rodrigues(opencvPose.rvec, r);
    float[] tmp = new float[9];
    r.get(0, 0, tmp);
    return new AprilTagPoseRaw(opencvPose.tvec.get(0, 0)[0], opencvPose.tvec.get(1, 0)[0], opencvPose.tvec.get(2, 0)[0],
        new GeneralMatrixF(3, 3, tmp));
  }

  private AprilTagPoseFtc rawPoseToFtcPose(AprilTagPoseRaw rawPose) {
    Orientation rot = Orientation.getOrientation(rawPose.R, AxesReference.INTRINSIC, AxesOrder.YXZ, outputUnitsAngle);
    return new AprilTagPoseFtc(rawPose.x, rawPose.z, -rawPose.y, -rot.firstAngle, rot.secondAngle, rot.thirdAngle,
        Math.hypot(rawPose.x, rawPose.z), outputUnitsAngle.fromUnit(AngleUnit.RADIANS, Math.atan2(-rawPose.x, rawPose.z)),
        outputUnitsAngle.fromUnit(AngleUnit.RADIANS, Math.atan2(-rawPose.y, rawPose.z)));
  }

  private PreliminaryFrameFindings segmentFrameFindings(List<SimFrame.NativeDetection> natives) {
    PreliminaryFrameFindings findings = new PreliminaryFrameFindings();
    for (SimFrame.NativeDetection d : natives) {
      Point[] corners = new Point[4];
      for (int p = 0; p < 4; p++) {
        corners[p] = new Point(d.corners()[p * 2], d.corners()[p * 2 + 1]);
      }
      PreliminaryTagDetection prelim = new PreliminaryTagDetection(d.id(), new Point(d.centerX(), d.centerY()), corners, d);
      AprilTagClusterMetadata cluster = tagLibrary.lookupCluster(prelim.id);
      if (cluster != null) {
        findings.noteClusterTag(prelim, cluster);
      } else {
        findings.noteSingleTag(prelim);
      }
    }
    return findings;
  }

  ArrayList<AprilTagDetection> runAprilTagDetectorForMultipleTagSizes(SimFrame frame, long captureTimeNanos) {
    if (frame == null || frame.detections() == null || cameraMatrix == null) {
      return new ArrayList<>();
    }
    ArrayList<AprilTagDetection> result = new ArrayList<>(frame.detections().size());
    PreliminaryFrameFindings prelimFindings = segmentFrameFindings(frame.detections());
    for (TagClusterNotebook n : prelimFindings.clusterNotebooks) {
      AprilTagPoseRaw rawPose = doClusterSolve(n, poseSolver);
      AprilTagPoseFtc ftcPose = rawPoseToFtcPose(rawPose);
      Pose3D robotPose = computeRobotPose(rawPose, n.cluster.fieldPosition, n.cluster.fieldOrientation, captureTimeNanos);
      int percentClusterFound = Math.round((float) n.detections.size() / (float) n.cluster.clusterMembers.size() * 100f);
      result.add(new AprilTagClusterDetection(percentClusterFound, n.cluster, outputUnitsLength, ftcPose, rawPose,
          robotPose, captureTimeNanos));
    }
    for (PreliminaryTagDetection det : prelimFindings.singleTags) {
      AprilTagPoseRaw rawPose = null;
      AprilTagPoseFtc ftcPose = null;
      Pose3D robotPose = null;
      AprilTagMetadata metadata = tagLibrary.lookupTag(det.id);
      if (metadata != null) {
        rawPose = doSingleTagPoseSolve(metadata, poseSolver, det.corners);
        ftcPose = rawPoseToFtcPose(rawPose);
        robotPose = computeRobotPose(rawPose, metadata.fieldPosition, metadata.fieldOrientation, captureTimeNanos);
      }
      result.add(new AprilTagSingleDetection(det.id, det.nativeDetection.hamming(), det.nativeDetection.decisionMargin(),
          det.center, det.corners, metadata, ftcPose, rawPose, robotPose, captureTimeNanos, outputUnitsLength));
    }
    return result;
  }

  private Pose3D computeRobotPose(AprilTagPoseRaw rawPose, VectorF fieldPosition, Quaternion fieldOrientation,
      long acquisitionTime) {
    float tagInFieldX = fieldPosition.get(0);
    float tagInFieldY = fieldPosition.get(1);
    float tagInFieldZ = fieldPosition.get(2);
    OpenGLMatrix tagInFieldR = new OpenGLMatrix(fieldOrientation.toMatrix());
    OpenGLMatrix tagInFieldFrame = OpenGLMatrix.identityMatrix().translated(tagInFieldX, tagInFieldY, tagInFieldZ)
        .multiplied(tagInFieldR);
    float tagInCameraX = (float) DistanceUnit.INCH.fromUnit(outputUnitsLength, rawPose.x);
    float tagInCameraY = (float) DistanceUnit.INCH.fromUnit(outputUnitsLength, rawPose.y);
    float tagInCameraZ = (float) DistanceUnit.INCH.fromUnit(outputUnitsLength, rawPose.z);
    OpenGLMatrix tagInCameraR = new OpenGLMatrix(rawPose.R);
    OpenGLMatrix cameraInTagFrame = OpenGLMatrix.identityMatrix().translated(tagInCameraX, tagInCameraY, tagInCameraZ)
        .multiplied(tagInCameraR).inverted();
    OpenGLMatrix robotInFieldFrame = tagInFieldFrame.multiplied(cameraInTagFrame).multiplied(robotInCameraFrame);
    VectorF robotInFieldTranslation = robotInFieldFrame.getTranslation();
    Position robotPosition = new Position(DistanceUnit.INCH, robotInFieldTranslation.get(0), robotInFieldTranslation.get(1),
        robotInFieldTranslation.get(2), acquisitionTime).toUnit(outputUnitsLength);
    Orientation robotInFieldOrientation = Orientation.getOrientation(robotInFieldFrame, AxesReference.INTRINSIC,
        AxesOrder.ZXY, outputUnitsAngle);
    YawPitchRollAngles robotOrientation = new YawPitchRollAngles(outputUnitsAngle, robotInFieldOrientation.firstAngle,
        robotInFieldOrientation.secondAngle, robotInFieldOrientation.thirdAngle, acquisitionTime);
    return new Pose3D(robotPosition, robotOrientation);
  }

  @Override
  public void onDrawFrame(Canvas canvas, int onscreenWidth, int onscreenHeight, float scaleBmpPxToCanvasPx,
      float scaleCanvasDensity, Object userContext) {
    synchronized (drawSync) {
      if ((drawAxes || drawCube || drawOutline || drawTagID) && userContext != null && canvasAnnotator != null) {
        canvasAnnotator.noteDrawParams(scaleBmpPxToCanvasPx, scaleCanvasDensity);
        @SuppressWarnings("unchecked")
        ArrayList<AprilTagDetection> dets = (ArrayList<AprilTagDetection>) userContext;
        for (AprilTagDetection detection : dets) {
          if (drawTagID) {
            canvasAnnotator.drawTagID(detection, canvas);
          }
          if (detection.rawPose == null) {
            continue;
          }
          if (drawOutline) {
            canvasAnnotator.drawOutlineMarker(detection, canvas);
          }
          if (drawAxes) {
            canvasAnnotator.drawAxisMarker(detection, canvas);
          }
          if (drawCube) {
            canvasAnnotator.draw3dRectMarker(detection, canvas);
          }
        }
      }
    }
  }

  @Override
  public void setDecimation(float decimation) {
    synchronized (decimationSync) {
      this.decimation = decimation;
      needToSetDecimation = true;
    }
  }

  @Override
  public void setPoseSolver(PoseSolver poseSolver) {
    this.poseSolver = poseSolver;
  }

  @Override
  public int getPerTagAvgPoseSolveTime() {
    return (int) Math.round(solveTime.getMean());
  }

  @Override
  public ArrayList<AprilTagDetection> getDetections() {
    return detections;
  }

  @Override
  public ArrayList<AprilTagDetection> getFreshDetections() {
    synchronized (detectionsUpdateSync) {
      ArrayList<AprilTagDetection> ret = detectionsUpdate;
      detectionsUpdate = null;
      return ret;
    }
  }

  void constructMatrix() {
    cameraMatrix = new Mat(3, 3, CvType.CV_32FC1);
    cameraMatrix.put(0, 0, fx);
    cameraMatrix.put(0, 1, 0.0);
    cameraMatrix.put(0, 2, cx);
    cameraMatrix.put(1, 0, 0.0);
    cameraMatrix.put(1, 1, fy);
    cameraMatrix.put(1, 2, cy);
    cameraMatrix.put(2, 0, 0.0);
    cameraMatrix.put(2, 1, 0.0);
    cameraMatrix.put(2, 2, 1.0);
  }

  static Pose poseFromTrapezoid(Point[] points, Mat cameraMatrix, double tagsize, int solveMethod) {
    MatOfPoint2f points2d = new MatOfPoint2f(points);
    Point3[] arrayPoints3d = new Point3[] {
      new Point3(-tagsize / 2.0, tagsize / 2.0, 0.0),
      new Point3(tagsize / 2.0, tagsize / 2.0, 0.0),
      new Point3(tagsize / 2.0, -tagsize / 2.0, 0.0),
      new Point3(-tagsize / 2.0, -tagsize / 2.0, 0.0)
    };
    MatOfPoint3f points3d = new MatOfPoint3f(arrayPoints3d);
    Pose pose = new Pose();
    Calib3d.solvePnP(points3d, points2d, cameraMatrix, new MatOfDouble(), pose.rvec, pose.tvec, false, solveMethod);
    return pose;
  }

  static Pose poseFromNGrt4Pts(Point[] imagePts, Point3[] idealSpacialProjectedPts, Mat cameraMatrix, int solveMethod) {
    MatOfPoint2f points2d = new MatOfPoint2f(imagePts);
    MatOfPoint3f points3d = new MatOfPoint3f(idealSpacialProjectedPts);
    Pose pose = new Pose();
    Calib3d.solvePnP(points3d, points2d, cameraMatrix, new MatOfDouble(), pose.rvec, pose.tvec, false, solveMethod);
    return pose;
  }

  /** Detections of the frame that belong to one cluster. */
  static class TagClusterNotebook {
    private final AprilTagClusterMetadata cluster;
    private final ArrayList<PreliminaryTagDetection> detections = new ArrayList<>();

    TagClusterNotebook(AprilTagClusterMetadata cluster) {
      this.cluster = cluster;
    }

    boolean isFor(AprilTagClusterMetadata c) {
      return cluster == c;
    }

    void note(PreliminaryTagDetection det) {
      detections.add(det);
    }
  }

  /** One native detection before pose solving. */
  static class PreliminaryTagDetection {
    final int id;
    final Point center;
    final Point[] corners;
    final SimFrame.NativeDetection nativeDetection;

    PreliminaryTagDetection(int id, Point center, Point[] corners, SimFrame.NativeDetection nativeDetection) {
      this.id = id;
      this.center = center;
      this.corners = corners;
      this.nativeDetection = nativeDetection;
    }
  }

  /** OpenCV rotation and translation vectors. */
  static class Pose {
    Mat rvec;
    Mat tvec;

    Pose() {
      rvec = new Mat(3, 1, CvType.CV_32F);
      tvec = new Mat(3, 1, CvType.CV_32F);
    }

    Pose(Mat rvec, Mat tvec) {
      this.rvec = rvec;
      this.tvec = tvec;
    }
  }

  /** The frame's detections split into cluster members and single tags. */
  static class PreliminaryFrameFindings {
    final ArrayList<TagClusterNotebook> clusterNotebooks = new ArrayList<>();
    final ArrayList<PreliminaryTagDetection> singleTags = new ArrayList<>();

    void noteSingleTag(PreliminaryTagDetection detection) {
      singleTags.add(detection);
    }

    void noteClusterTag(PreliminaryTagDetection detection, AprilTagClusterMetadata parentCluster) {
      for (TagClusterNotebook notebook : clusterNotebooks) {
        if (notebook.isFor(parentCluster)) {
          notebook.note(detection);
          return;
        }
      }
      TagClusterNotebook n = new TagClusterNotebook(parentCluster);
      n.note(detection);
      clusterNotebooks.add(n);
    }
  }
}
