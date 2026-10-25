package org.firstinspires.ftc.sim.vision;

import java.util.List;

/**
 * One camera frame the simulator delivers in a TICK (docs/ipc_protocol.md): RGBA pixels in the camera's shared-memory
 * slot, the capture time, and the AprilTag detections of the native detector when a processor asked for them.
 */
public record SimFrame(String camera, long captureNanos, int slot, int width, int height, List<NativeDetection> detections) {
  /** One detection of the AprilTag 3 library: id, error bits, decision margin, centre and the 4 corners (pixels). */
  public record NativeDetection(int id, int hamming, float decisionMargin, double centerX, double centerY, double[] corners) {}
}
