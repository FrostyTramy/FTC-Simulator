package org.firstinspires.ftc.vision.apriltag;

import android.graphics.Canvas;
import org.opencv.core.Mat;

/** Draws tag outlines and axes on the live view (SDK 12.0); the simulator has no live view yet, so nothing is drawn. */
public class AprilTagCanvasAnnotator {
  public AprilTagCanvasAnnotator(Mat cameraMatrix) {}

  public void noteDrawParams(float bmpPxToCanvasPx, float canvasDensityScale) {}

  public void drawTagID(AprilTagDetection detection, Canvas canvas) {}

  public void drawOutlineMarker(AprilTagDetection detection, Canvas canvas) {}

  public void drawAxisMarker(AprilTagDetection detection, Canvas canvas) {}

  public void draw3dRectMarker(AprilTagDetection detection, Canvas canvas) {}
}
