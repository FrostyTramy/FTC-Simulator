package org.firstinspires.ftc.vision;

import android.util.Size;
import java.util.ArrayList;
import java.util.List;
import org.firstinspires.ftc.robotcore.external.hardware.camera.BuiltinCameraDirection;
import org.firstinspires.ftc.robotcore.external.hardware.camera.CameraName;
import org.firstinspires.ftc.robotcore.external.hardware.camera.WebcamName;
import org.firstinspires.ftc.robotcore.external.hardware.camera.controls.CameraControl;
import org.firstinspires.ftc.robotcore.external.stream.CameraStreamSource;
import org.firstinspires.ftc.sim.vision.SimVisionPortal;

/**
 * Runs vision processors on a camera's frames (SDK 12.0 public API). In the simulator the camera is rendered from its
 * robot YAML pose ({@link SimVisionPortal}); frames reach the processors with the camera's latency.
 */
public abstract class VisionPortal implements CameraStreamSource {
  public static final int DEFAULT_VIEW_CONTAINER_ID = 0x7f0b0001;

  /** Camera life cycle. */
  public enum CameraState {
    OPENING_CAMERA_DEVICE,
    CAMERA_DEVICE_READY,
    STARTING_STREAM,
    STREAMING,
    STOPPING_STREAM,
    CLOSING_CAMERA_DEVICE,
    CAMERA_DEVICE_CLOSED,
    ERROR
  }

  /** Webcam stream format. */
  public enum StreamFormat {
    YUY2,
    MJPEG
  }

  /** Layout of several live views on the Robot Controller screen. */
  public enum MultiPortalLayout {
    VERTICAL,
    HORIZONTAL
  }

  public VisionPortal() {}

  /** View ids for several portals' live views (no Robot Controller screen in the simulator: ids only). */
  public static int[] makeMultiPortalView(int numPortals, MultiPortalLayout layout) {
    int[] ids = new int[numPortals];
    for (int i = 0; i < numPortals; i++) {
      ids[i] = DEFAULT_VIEW_CONTAINER_ID + 1 + i;
    }
    return ids;
  }

  public static VisionPortal easyCreateWithDefaults(BuiltinCameraDirection cameraDirection, VisionProcessor... processors) {
    return new Builder().setCamera(cameraDirection).addProcessors(processors).build();
  }

  public static VisionPortal easyCreateWithDefaults(CameraName cameraName, VisionProcessor... processors) {
    return new Builder().setCamera(cameraName).addProcessors(processors).build();
  }

  /** Builds a VisionPortal (defaults as the SDK: 640x480, YUY2, live view on, auto start). */
  public static class Builder {
    private CameraName camera;
    private BuiltinCameraDirection builtinDirection;
    private StreamFormat streamFormat;
    private boolean liveView = true;
    private boolean autoStopLiveView = true;
    private int liveViewContainerId = DEFAULT_VIEW_CONTAINER_ID;
    private Size cameraResolution = new Size(640, 480);
    private final List<VisionProcessor> processors = new ArrayList<>();
    private boolean autoStartStreamOnBuild = true;
    private boolean showStatsOverlay = true;

    public Builder() {}

    public Builder setCamera(CameraName camera) {
      this.camera = camera;
      this.builtinDirection = null;
      return this;
    }

    public Builder setCamera(BuiltinCameraDirection cameraDirection) {
      this.builtinDirection = cameraDirection;
      this.camera = null;
      return this;
    }

    public Builder setStreamFormat(StreamFormat streamFormat) {
      this.streamFormat = streamFormat;
      return this;
    }

    public Builder enableLiveView(boolean enableLiveView) {
      this.liveView = enableLiveView;
      return this;
    }

    public Builder setAutoStopLiveView(boolean autoStopLiveView) {
      this.autoStopLiveView = autoStopLiveView;
      return this;
    }

    public Builder setLiveViewContainerId(int liveViewContainerId) {
      this.liveViewContainerId = liveViewContainerId;
      return this;
    }

    public Builder setCameraResolution(Size cameraResolution) {
      this.cameraResolution = cameraResolution;
      return this;
    }

    public Builder addProcessor(VisionProcessor processor) {
      processors.add(processor);
      return this;
    }

    public Builder addProcessors(VisionProcessor... processors) {
      for (VisionProcessor p : processors) {
        addProcessor(p);
      }
      return this;
    }

    public Builder setAutoStartStreamOnBuild(boolean autoStartStreamOnBuild) {
      this.autoStartStreamOnBuild = autoStartStreamOnBuild;
      return this;
    }

    public Builder setShowStatsOverlay(boolean showStatsOverlay) {
      this.showStatsOverlay = showStatsOverlay;
      return this;
    }

    public VisionPortal build() {
      if (camera == null && builtinDirection == null) {
        throw new RuntimeException("You must set a camera!");
      }
      if (builtinDirection != null) {
        throw new RuntimeException("This robot has no built-in phone camera (" + builtinDirection
            + "); use a webcam from the hardware map");
      }
      if (processors.isEmpty()) {
        throw new RuntimeException("You must add at least one VisionProcessor!");
      }
      if (processors.contains(null)) {
        throw new RuntimeException("Processors cannot be null");
      }
      return new SimVisionPortal(camera, cameraResolution, processors, autoStartStreamOnBuild, liveView, autoStopLiveView,
          liveViewContainerId, streamFormat, showStatsOverlay);
    }
  }

  public abstract void setProcessorEnabled(VisionProcessor processor, boolean enabled);

  public abstract boolean getProcessorEnabled(VisionProcessor processor);

  public abstract CameraState getCameraState();

  public abstract void saveNextFrameRaw(String filename);

  public abstract void stopStreaming();

  public abstract void resumeStreaming();

  public abstract void stopLiveView();

  public abstract void resumeLiveView();

  public abstract float getFps();

  public abstract <T extends CameraControl> T getCameraControl(Class<T> controlType);

  public abstract void setActiveCamera(WebcamName webcamName);

  public abstract WebcamName getActiveCamera();

  public abstract void close();
}
