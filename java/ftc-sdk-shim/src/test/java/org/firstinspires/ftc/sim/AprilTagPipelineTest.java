package org.firstinspires.ftc.sim;

import static org.junit.jupiter.api.Assertions.assertEquals;
import static org.junit.jupiter.api.Assertions.assertInstanceOf;
import static org.junit.jupiter.api.Assertions.assertNotNull;
import static org.junit.jupiter.api.Assertions.assertTrue;

import java.util.ArrayList;
import java.util.List;
import org.firstinspires.ftc.robotcore.external.android.util.Size;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.DistanceUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Position;
import org.firstinspires.ftc.robotcore.external.navigation.Quaternion;
import org.firstinspires.ftc.robotcore.external.navigation.YawPitchRollAngles;
import org.firstinspires.ftc.robotcore.internal.camera.calibration.CameraCalibration;
import org.firstinspires.ftc.sim.vision.SimFrame;
import org.firstinspires.ftc.sim.vision.SimVision;
import org.firstinspires.ftc.vision.apriltag.AprilTagClusterDetection;
import org.firstinspires.ftc.vision.apriltag.AprilTagDetection;
import org.firstinspires.ftc.vision.apriltag.AprilTagGameDatabase;
import org.firstinspires.ftc.vision.apriltag.AprilTagProcessor;
import org.firstinspires.ftc.vision.apriltag.AprilTagSingleDetection;
import org.junit.jupiter.api.BeforeAll;
import org.junit.jupiter.api.Test;
import org.opencv.core.Mat;

/**
 * AprilTagProcessor end to end on synthetic detections: tag corners projected through a pinhole camera from a known
 * pose must give back that pose (raw/ftc pose of a BIOBUZZ cluster, field robot pose from a single library tag).
 * Ground truth uses plain 4×4 double math independent of the shim's matrix classes.
 */
class AprilTagPipelineTest {
  private static final double FX = 622.001;
  private static final double FY = 622.001;
  private static final double CX = 319.803;
  private static final double CY = 241.251;

  @BeforeAll
  static void loadOpenCv() {
    System.load(System.getProperty("ftcsim.opencv.native"));
    ShimTestSupport.freshRuntime();
  }

  // ---------------------------------------------------------------- plain rigid transforms (row-major 4×4)

  private static double[][] translation(double x, double y, double z) {
    return new double[][] {{1, 0, 0, x}, {0, 1, 0, y}, {0, 0, 1, z}, {0, 0, 0, 1}};
  }

  private static double[][] rotation(int axis, double degrees) {
    double c = Math.cos(Math.toRadians(degrees));
    double s = Math.sin(Math.toRadians(degrees));
    switch (axis) {
      case 0:
        return new double[][] {{1, 0, 0, 0}, {0, c, -s, 0}, {0, s, c, 0}, {0, 0, 0, 1}};
      case 1:
        return new double[][] {{c, 0, s, 0}, {0, 1, 0, 0}, {-s, 0, c, 0}, {0, 0, 0, 1}};
      default:
        return new double[][] {{c, -s, 0, 0}, {s, c, 0, 0}, {0, 0, 1, 0}, {0, 0, 0, 1}};
    }
  }

  private static double[][] quaternion(double w, double x, double y, double z) {
    return new double[][] {
      {1 - 2 * (y * y + z * z), 2 * (x * y - w * z), 2 * (x * z + w * y), 0},
      {2 * (x * y + w * z), 1 - 2 * (x * x + z * z), 2 * (y * z - w * x), 0},
      {2 * (x * z - w * y), 2 * (y * z + w * x), 1 - 2 * (x * x + y * y), 0},
      {0, 0, 0, 1}
    };
  }

  private static double[][] mul(double[][] a, double[][] b) {
    double[][] r = new double[4][4];
    for (int i = 0; i < 4; i++) {
      for (int j = 0; j < 4; j++) {
        for (int k = 0; k < 4; k++) {
          r[i][j] += a[i][k] * b[k][j];
        }
      }
    }
    return r;
  }

  /** Inverse of a rigid transform. */
  private static double[][] inverse(double[][] m) {
    double[][] r = new double[4][4];
    for (int i = 0; i < 3; i++) {
      for (int j = 0; j < 3; j++) {
        r[i][j] = m[j][i];
      }
      r[i][3] = -(m[0][i] * m[0][3] + m[1][i] * m[1][3] + m[2][i] * m[2][3]);
    }
    r[3][3] = 1;
    return r;
  }

  private static double[] apply(double[][] m, double x, double y, double z) {
    return new double[] {m[0][0] * x + m[0][1] * y + m[0][2] * z + m[0][3], m[1][0] * x + m[1][1] * y + m[1][2] * z + m[1][3],
        m[2][0] * x + m[2][1] * y + m[2][2] * z + m[2][3]};
  }

  /** The 4 corners of a tag of size s centred at (ox, oy, oz) of its frame, in the order AprilTag reports them. */
  private static SimFrame.NativeDetection project(int id, double[][] cameraFromTagFrame, double s, double ox, double oy,
      double oz) {
    double[][] corners = {{-s / 2 + ox, s / 2 + oy, oz}, {s / 2 + ox, s / 2 + oy, oz}, {s / 2 + ox, -s / 2 + oy, oz},
      {-s / 2 + ox, -s / 2 + oy, oz}};
    double[] px = new double[8];
    double cu = 0;
    double cv = 0;
    for (int i = 0; i < 4; i++) {
      double[] p = apply(cameraFromTagFrame, corners[i][0], corners[i][1], corners[i][2]);
      assertTrue(p[2] > 0, "tag corner behind the camera");
      px[2 * i] = FX * p[0] / p[2] + CX;
      px[2 * i + 1] = FY * p[1] / p[2] + CY;
      cu += px[2 * i] / 4;
      cv += px[2 * i + 1] / 4;
    }
    return new SimFrame.NativeDetection(id, 0, 60f, cu, cv, px);
  }

  private static List<AprilTagDetection> run(AprilTagProcessor processor, List<SimFrame.NativeDetection> natives) {
    processor.init(640, 480, new CameraCalibration(() -> false, new Size(640, 480), (float) FX, (float) FY, (float) CX,
        (float) CY, new float[8], false, false));
    SimFrame frame = new SimFrame("Webcam 1", 1_000_000L, 0, 640, 480, natives);
    SimVision.runWithFrame(frame, () -> processor.processFrame(new Mat(), frame.captureNanos()));
    return processor.getDetections();
  }

  @Test
  void biobuzz_cluster_pose_comes_back_from_its_projected_tags() {
    // Camera in the cluster frame (x right, y down, z into the tags): 40 in in front, turned 10° about y.
    double[][] clusterFromCamera = mul(translation(2, -1, -40), rotation(1, 10));
    double[][] cameraFromCluster = inverse(clusterFromCamera);
    List<SimFrame.NativeDetection> natives = new ArrayList<>();
    double[] offsets = {-6.5, -2.75, 2.75, 6.5};
    for (int i = 0; i < 4; i++) {
      natives.add(project(34 + i, cameraFromCluster, 3.25, offsets[i], 7.1874, -5.622));
    }
    AprilTagProcessor processor = new AprilTagProcessor.Builder()
        .setTagLibrary(AprilTagGameDatabase.getBioBuzzTagLibrary())
        .setCameraPose(new Position(DistanceUnit.INCH, 0, 0, 10, 0), new YawPitchRollAngles(AngleUnit.DEGREES, 0, -90, 0, 0))
        .build();
    List<AprilTagDetection> detections = run(processor, natives);
    assertEquals(1, detections.size());
    AprilTagClusterDetection cluster = assertInstanceOf(AprilTagClusterDetection.class, detections.get(0));
    assertEquals("RED AUDIENCE", cluster.metadata.name);
    assertEquals(100, cluster.percentClusterFound);
    double[] origin = {cameraFromCluster[0][3], cameraFromCluster[1][3], cameraFromCluster[2][3]};
    assertEquals(origin[0], cluster.rawPose.x, 1e-3);
    assertEquals(origin[1], cluster.rawPose.y, 1e-3);
    assertEquals(origin[2], cluster.rawPose.z, 1e-3);
    assertEquals(origin[0], cluster.ftcPose.x, 1e-3);   // ftc x right
    assertEquals(origin[2], cluster.ftcPose.y, 1e-3);   // ftc y forward = raw z
    assertEquals(-origin[1], cluster.ftcPose.z, 1e-3);  // ftc z up = -raw y
    assertEquals(Math.hypot(origin[0], origin[2]), cluster.ftcPose.range, 1e-3);
    // Robot pose in the cluster frame: cluster←camera←robot, the camera 10 in up on a level mount.
    double[][] robotFromCamera = mul(translation(0, 0, 10), rotation(0, -90));
    double[][] clusterFromRobot = mul(clusterFromCamera, inverse(robotFromCamera));
    assertEquals(clusterFromRobot[0][3], cluster.robotPose.getPosition().x, 1e-2);
    assertEquals(clusterFromRobot[1][3], cluster.robotPose.getPosition().y, 1e-2);
    assertEquals(clusterFromRobot[2][3], cluster.robotPose.getPosition().z, 1e-2);
  }

  @Test
  void partial_cluster_still_gives_the_pose() {
    double[][] cameraFromCluster = inverse(translation(0, 3, -30));
    List<SimFrame.NativeDetection> natives = List.of(project(31, cameraFromCluster, 3.25, -2.75, 7.1874, -5.622));
    AprilTagProcessor processor = new AprilTagProcessor.Builder().build(); // current game library
    AprilTagClusterDetection cluster = assertInstanceOf(AprilTagClusterDetection.class, run(processor, natives).get(0));
    assertEquals("RED SCORING", cluster.metadata.name);
    assertEquals(25, cluster.percentClusterFound);
    assertEquals(30, cluster.rawPose.z, 1e-2);
  }

  @Test
  void single_library_tag_localizes_the_robot_on_the_field() {
    // CENTERSTAGE tag 7 (audience wall); robot at (-40, -40) in, heading +90° (facing -x, the audience wall).
    double[][] fieldFromTag = mul(translation(-70.25, -40.625, 5.5), quaternion(0.5, -0.5, -0.5, 0.5));
    double[][] fieldFromRobot = mul(translation(-40, -40, 0), rotation(2, 90));
    double[][] robotFromCamera = mul(translation(0, 0, 8), rotation(0, -90));
    double[][] cameraFromTag = mul(inverse(mul(fieldFromRobot, robotFromCamera)), fieldFromTag);
    List<SimFrame.NativeDetection> natives = List.of(project(7, cameraFromTag, 5.0, 0, 0, 0));
    AprilTagProcessor processor = new AprilTagProcessor.Builder()
        .setTagLibrary(AprilTagGameDatabase.getCenterStageTagLibrary())
        .setCameraPose(new Position(DistanceUnit.INCH, 0, 0, 8, 0), new YawPitchRollAngles(AngleUnit.DEGREES, 0, -90, 0, 0))
        .build();
    AprilTagSingleDetection tag = assertInstanceOf(AprilTagSingleDetection.class, run(processor, natives).get(0));
    assertNotNull(tag.metadata);
    assertEquals(7, tag.id);
    assertEquals(-40, tag.robotPose.getPosition().x, 0.05);
    assertEquals(-40, tag.robotPose.getPosition().y, 0.05);
    assertEquals(0, tag.robotPose.getPosition().z, 0.05);
    assertEquals(90, tag.robotPose.getOrientation().getYaw(AngleUnit.DEGREES), 0.1);
  }

  @Test
  void unknown_tag_has_no_pose() {
    double[][] cameraFromTag = inverse(translation(0, 0, -20));
    AprilTagProcessor processor = new AprilTagProcessor.Builder().build();
    AprilTagSingleDetection tag = assertInstanceOf(AprilTagSingleDetection.class,
        run(processor, List.of(project(99, cameraFromTag, 2, 0, 0, 0))).get(0));
    assertEquals(null, tag.metadata);
    assertEquals(null, tag.ftcPose);
    assertTrue(tag.center.x > 300 && tag.center.x < 340);
  }

  @SuppressWarnings("unused")
  private static Quaternion unused() {
    return null;
  }
}
