package org.firstinspires.ftc.vision;

import android.graphics.Canvas;
import org.firstinspires.ftc.robotcore.internal.camera.calibration.CameraCalibration;
import org.opencv.core.Mat;

/** The three calls a VisionPortal makes on its processors (SDK 12.0: package-private, inherited by VisionProcessor). */
interface VisionProcessorInternal {
  /** Once, when the camera opens: the frame size and the camera calibration (may be null or degenerate). */
  void init(int width, int height, CameraCalibration calibration);

  /** Each camera frame (RGBA); the return value is handed to onDrawFrame. */
  Object processFrame(Mat frame, long captureTimeNanos);

  /** Draws on the live view (no live view in the simulator yet). */
  void onDrawFrame(Canvas canvas, int onscreenWidth, int onscreenHeight, float scaleBmpPxToCanvasPx,
      float scaleCanvasDensity, Object userContext);
}
