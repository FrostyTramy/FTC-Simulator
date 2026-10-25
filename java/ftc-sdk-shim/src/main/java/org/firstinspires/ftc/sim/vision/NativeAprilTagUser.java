package org.firstinspires.ftc.sim.vision;

/**
 * A processor that needs the simulator's native AprilTag detections with its frames (the SDK's AprilTagProcessor runs
 * the AprilTag 3 library on the robot; the simulator runs the same library on the rendered frame).
 */
public interface NativeAprilTagUser {
  /** quad_decimate the detector should use. */
  float requestedDecimation();

  /** Tag family, e.g. "tag36h11". */
  String requestedFamily();
}
