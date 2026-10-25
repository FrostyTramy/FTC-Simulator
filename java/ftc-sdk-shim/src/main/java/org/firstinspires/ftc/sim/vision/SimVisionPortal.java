package org.firstinspires.ftc.sim.vision;

import android.graphics.Bitmap;
import android.util.Size;
import java.nio.file.Path;
import java.util.ArrayDeque;
import java.util.ArrayList;
import java.util.IdentityHashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.TimeUnit;
import org.firstinspires.ftc.robotcore.external.function.Consumer;
import org.firstinspires.ftc.robotcore.external.function.Continuation;
import org.firstinspires.ftc.robotcore.external.hardware.camera.CameraName;
import org.firstinspires.ftc.robotcore.external.hardware.camera.WebcamName;
import org.firstinspires.ftc.robotcore.external.hardware.camera.controls.CameraControl;
import org.firstinspires.ftc.robotcore.external.hardware.camera.controls.ExposureControl;
import org.firstinspires.ftc.robotcore.external.hardware.camera.controls.FocusControl;
import org.firstinspires.ftc.robotcore.external.hardware.camera.controls.GainControl;
import org.firstinspires.ftc.robotcore.external.hardware.camera.controls.PtzControl;
import org.firstinspires.ftc.robotcore.external.hardware.camera.controls.WhiteBalanceControl;
import org.firstinspires.ftc.robotcore.internal.camera.calibration.CameraCalibration;
import org.firstinspires.ftc.robotcore.internal.camera.calibration.CameraCalibrationHelper;
import org.firstinspires.ftc.robotcore.internal.camera.calibration.CameraCalibrationIdentity;
import org.firstinspires.ftc.robotcore.internal.collections.MutableReference;
import org.firstinspires.ftc.sim.SimRuntime;
import org.firstinspires.ftc.sim.devices.SimWebcamName;
import org.firstinspires.ftc.vision.VisionPortal;
import org.firstinspires.ftc.vision.VisionProcessor;
import org.opencv.core.CvType;
import org.opencv.core.Mat;
import org.opencv.imgcodecs.Imgcodecs;
import org.opencv.imgproc.Imgproc;

/**
 * The simulator's VisionPortal. Asks the simulator to stream the webcam (sensor command "stream"); frames arrive in
 * TICKs after the camera's latency and run through the enabled processors (see {@link SimVision}). The camera state
 * stays OPENING_CAMERA_DEVICE until the first frame (the simulator models the open time). The calibration is the
 * webcam model's (config/sensors.yaml); the rendered image has no lens distortion, so its distortion is zero.
 */
public final class SimVisionPortal extends VisionPortal {
  private final SimWebcamName webcam;
  private final String camera;
  private final int index;
  private final int width;
  private final int height;
  private final List<VisionProcessor> processors;
  private final Map<VisionProcessor, Boolean> enabled = new IdentityHashMap<>();
  private final Controls controls = new Controls();
  private final ArrayDeque<Long> recentFrames = new ArrayDeque<>();
  private volatile CameraState state;
  private boolean processorsInitialized;
  private boolean closed;
  private boolean streaming;
  private Mat rgba;
  private byte[] pixels;
  private volatile String saveNextFrame;
  private String lastDetectionRequest = "";

  public SimVisionPortal(CameraName cameraName, Size resolution, List<VisionProcessor> processors, boolean autoStart,
      boolean liveView, boolean autoStopLiveView, int liveViewContainerId, StreamFormat streamFormat,
      boolean showStatsOverlay) {
    if (!(cameraName instanceof SimWebcamName)) {
      throw new RuntimeException("This camera is not available on the simulated robot: " + cameraName);
    }
    webcam = (SimWebcamName) cameraName;
    camera = webcam.configName();
    index = (int) webcam.param("index", 0);
    int w = (int) webcam.param("width", 640);
    int h = (int) webcam.param("height", 480);
    if (resolution.getWidth() != w || resolution.getHeight() != h) {
      throw new RuntimeException("Camera does not support requested resolution! Supported resolutions are: [" + w + "x"
          + h + "]");
    }
    width = w;
    height = h;
    this.processors = new ArrayList<>(processors);
    for (VisionProcessor p : processors) {
      enabled.put(p, true);
    }
    CameraCalibrationHelper.getInstance().register(new CameraCalibration(identity(), new org.firstinspires.ftc.robotcore
        .external.android.util.Size(w, h), (float) webcam.param("fx", 0), (float) webcam.param("fy", 0),
        (float) webcam.param("cx", 0), (float) webcam.param("cy", 0), new float[8], false, false));
    SimVision.get().register(camera, this);
    state = CameraState.OPENING_CAMERA_DEVICE;
    if (autoStart) {
      stream(true);
    }
    sendDetectionRequest();
  }

  /** The built-in calibration of this camera's model, as the SDK looks it up. */
  private CameraCalibrationIdentity identity() {
    return new SimCameraIdentity(camera);
  }

  private record SimCameraIdentity(String camera) implements CameraCalibrationIdentity {
    @Override
    public boolean isDegenerate() {
      return false;
    }
  }

  private void stream(boolean on) {
    streaming = on;
    SimRuntime.get().bus().sensorCommand(camera, "stream", on ? 1 : 0, width, height);
  }

  /** Tells the simulator whether to run the native AprilTag detector on this camera's frames, and how. */
  private void sendDetectionRequest() {
    String request = "";
    double decimation = 0;
    double family = 0;
    for (VisionProcessor p : processors) {
      if (enabled.get(p) && p instanceof NativeAprilTagUser) {
        NativeAprilTagUser user = (NativeAprilTagUser) p;
        decimation = user.requestedDecimation();
        family = familyCode(user.requestedFamily());
        request = decimation + "/" + family;
      }
    }
    if (!request.equals(lastDetectionRequest)) {
      lastDetectionRequest = request;
      SimRuntime.get().bus().sensorCommand(camera, "apriltag", request.isEmpty() ? 0 : 1, decimation, family);
    }
  }

  private static double familyCode(String family) {
    switch (family) {
      case "tag25h9":
        return 1;
      case "tag16h5":
        return 2;
      case "tagStandard41h12":
        return 3;
      default:
        return 0; // tag36h11
    }
  }

  /** Vision thread: one frame through the enabled processors. */
  void process(SimFrame frame) {
    if (closed || !streaming) {
      return; // frames still in flight after stopStreaming/close are dropped
    }
    state = CameraState.STREAMING;
    if (!processorsInitialized) {
      CameraCalibration calibration = CameraCalibrationHelper.getInstance().getCalibration(identity(), width, height);
      for (VisionProcessor p : processors) {
        p.init(width, height, calibration);
      }
      processorsInitialized = true;
    }
    if (rgba == null) {
      rgba = new Mat(height, width, CvType.CV_8UC4);
      pixels = new byte[width * height * 4];
    }
    SimVision.get().readPixels(index, frame, pixels);
    rgba.put(0, 0, pixels);
    for (VisionProcessor p : processors) {
      if (enabled.get(p)) {
        p.processFrame(rgba, frame.captureNanos());
      }
    }
    recentFrames.addLast(frame.captureNanos());
    while (!recentFrames.isEmpty() && frame.captureNanos() - recentFrames.peekFirst() >= 1_000_000_000L) {
      recentFrames.removeFirst();
    }
    String save = saveNextFrame;
    if (save != null) {
      saveNextFrame = null;
      Mat bgr = new Mat();
      Imgproc.cvtColor(rgba, bgr, Imgproc.COLOR_RGBA2BGR);
      Path dir = Path.of(System.getProperty("ftcsim.workdir", "."));
      Imgcodecs.imwrite(dir.resolve("VisionPortal-" + save + ".png").toString(), bgr);
      bgr.release();
    }
  }

  void fail() {
    state = CameraState.ERROR;
  }

  boolean isClosed() {
    return closed;
  }

  @Override
  public void setProcessorEnabled(VisionProcessor processor, boolean enable) {
    if (!enabled.containsKey(processor)) {
      throw new IllegalArgumentException("Processor not attached to this portal");
    }
    enabled.put(processor, enable);
    sendDetectionRequest();
  }

  @Override
  public boolean getProcessorEnabled(VisionProcessor processor) {
    Boolean e = enabled.get(processor);
    if (e == null) {
      throw new IllegalArgumentException("Processor not attached to this portal");
    }
    return e;
  }

  @Override
  public CameraState getCameraState() {
    return state;
  }

  @Override
  public void saveNextFrameRaw(String filename) {
    saveNextFrame = filename;
  }

  @Override
  public void stopStreaming() {
    if (streaming) {
      stream(false);
      state = CameraState.CAMERA_DEVICE_READY;
    }
  }

  @Override
  public void resumeStreaming() {
    if (!streaming && !closed) {
      stream(true);
      state = CameraState.STARTING_STREAM;
    }
  }

  @Override
  public void stopLiveView() {}

  @Override
  public void resumeLiveView() {}

  @Override
  public float getFps() {
    return streaming ? recentFrames.size() : 0f;
  }

  @Override
  public <T extends CameraControl> T getCameraControl(Class<T> controlType) {
    if (controlType == ExposureControl.class) {
      return controlType.cast(controls.exposure);
    }
    if (controlType == GainControl.class) {
      return controlType.cast(controls.gain);
    }
    if (controlType == FocusControl.class) {
      return controlType.cast(controls.focus);
    }
    if (controlType == WhiteBalanceControl.class) {
      return controlType.cast(controls.whiteBalance);
    }
    if (controlType == PtzControl.class) {
      return controlType.cast(controls.ptz);
    }
    return null;
  }

  @Override
  public void setActiveCamera(WebcamName webcamName) {
    throw new UnsupportedOperationException("setActiveCamera is only supported for a switchable camera");
  }

  @Override
  public WebcamName getActiveCamera() {
    return webcam;
  }

  @Override
  public void close() {
    if (!closed) {
      if (streaming) {
        stream(false);
      }
      closed = true;
      state = CameraState.CAMERA_DEVICE_CLOSED;
      SimVision.get().unregister(camera, this);
      SimRuntime.get().bus().sensorCommand(camera, "apriltag", 0, 0, 0);
    }
  }

  @Override
  public void getFrameBitmap(Continuation<? extends Consumer<Bitmap>> continuation) {
    Bitmap bitmap = Bitmap.createBitmap(width, height, Bitmap.Config.ARGB_8888);
    if (pixels != null) {
      for (int y = 0; y < height; y++) {
        for (int x = 0; x < width; x++) {
          int i = (y * width + x) * 4;
          bitmap.setPixel(x, y, (pixels[i + 3] & 0xFF) << 24 | (pixels[i] & 0xFF) << 16 | (pixels[i + 1] & 0xFF) << 8
              | (pixels[i + 2] & 0xFF));
        }
      }
    }
    continuation.dispatch(consumer -> consumer.accept(bitmap));
  }

  /** The webcam's UVC controls; values go to the simulator's renderer (ranges ASSUMED, config/sensors.yaml). */
  private final class Controls {
    private ExposureControl.Mode exposureMode = ExposureControl.Mode.Auto;
    private long exposureNanos = 16_000_000L;
    private int gainValue = 100;
    private boolean aePriority;
    private FocusControl.Mode focusMode = FocusControl.Mode.ContinuousAuto;
    private double focusLength = 0;
    private WhiteBalanceControl.Mode wbMode = WhiteBalanceControl.Mode.AUTO;
    private int wbTemperature = 4000;
    private final PtzControl.PanTiltHolder panTilt = new PtzControl.PanTiltHolder();
    private int zoom = 100;

    private void sendExposure() {
      SimRuntime.get().bus().sensorCommand(camera, "exposure", exposureMode == ExposureControl.Mode.Manual ? 1 : 0,
          exposureNanos / 1e6, gainValue);
    }

    final ExposureControl exposure = new ExposureControl() {
      @Override
      public Mode getMode() {
        return exposureMode;
      }

      @Override
      public boolean setMode(Mode mode) {
        if (!isModeSupported(mode)) {
          return false;
        }
        exposureMode = mode;
        sendExposure();
        return true;
      }

      @Override
      public boolean isModeSupported(Mode mode) {
        return mode == Mode.Manual || mode == Mode.AperturePriority || mode == Mode.Auto;
      }

      @Override
      public long getMinExposure(TimeUnit resultUnit) {
        return resultUnit.convert((long) (webcam.param("exposureMinMs", 1) * 1e6), TimeUnit.NANOSECONDS);
      }

      @Override
      public long getMaxExposure(TimeUnit resultUnit) {
        return resultUnit.convert((long) (webcam.param("exposureMaxMs", 204) * 1e6), TimeUnit.NANOSECONDS);
      }

      @Override
      public long getExposure(TimeUnit resultUnit) {
        return resultUnit.convert(exposureNanos, TimeUnit.NANOSECONDS);
      }

      @Override
      public long getCachedExposure(TimeUnit resultUnit, MutableReference<Boolean> refreshed, long permittedStaleness,
          TimeUnit permittedStalenessUnit) {
        if (refreshed != null) {
          refreshed.setValue(false);
        }
        return getExposure(resultUnit);
      }

      @Override
      public boolean setExposure(long duration, TimeUnit durationUnit) {
        long nanos = durationUnit.toNanos(duration);
        if (nanos < getMinExposure(TimeUnit.NANOSECONDS) || nanos > getMaxExposure(TimeUnit.NANOSECONDS)) {
          return false;
        }
        exposureNanos = nanos;
        sendExposure();
        return true;
      }

      @Override
      public boolean isExposureSupported() {
        return true;
      }

      @Override
      public boolean getAePriority() {
        return aePriority;
      }

      @Override
      public boolean setAePriority(boolean priority) {
        aePriority = priority;
        return true;
      }
    };

    final GainControl gain = new GainControl() {
      @Override
      public int getMinGain() {
        return (int) webcam.param("gainMin", 0);
      }

      @Override
      public int getMaxGain() {
        return (int) webcam.param("gainMax", 255);
      }

      @Override
      public int getGain() {
        return gainValue;
      }

      @Override
      public boolean setGain(int g) {
        if (g < getMinGain() || g > getMaxGain()) {
          return false;
        }
        gainValue = g;
        sendExposure();
        return true;
      }
    };

    final FocusControl focus = new FocusControl() {
      @Override
      public Mode getMode() {
        return focusMode;
      }

      @Override
      public boolean setMode(Mode mode) {
        focusMode = mode;
        return true;
      }

      @Override
      public boolean isModeSupported(Mode mode) {
        return mode == Mode.ContinuousAuto || mode == Mode.Fixed;
      }

      @Override
      public double getMinFocusLength() {
        return 0;
      }

      @Override
      public double getMaxFocusLength() {
        return 250;
      }

      @Override
      public double getFocusLength() {
        return focusLength;
      }

      @Override
      public boolean setFocusLength(double length) {
        focusLength = length;
        return true;
      }

      @Override
      public boolean isFocusLengthSupported() {
        return true;
      }
    };

    final WhiteBalanceControl whiteBalance = new WhiteBalanceControl() {
      @Override
      public Mode getMode() {
        return wbMode;
      }

      @Override
      public boolean setMode(Mode mode) {
        wbMode = mode;
        return true;
      }

      @Override
      public int getMinWhiteBalanceTemperature() {
        return 2000;
      }

      @Override
      public int getMaxWhiteBalanceTemperature() {
        return 6500;
      }

      @Override
      public int getWhiteBalanceTemperature() {
        return wbTemperature;
      }

      @Override
      public boolean setWhiteBalanceTemperature(int temperature) {
        wbTemperature = temperature;
        return true;
      }
    };

    final PtzControl ptz = new PtzControl() {
      @Override
      public PanTiltHolder getPanTilt() {
        PanTiltHolder h = new PanTiltHolder();
        h.pan = panTilt.pan;
        h.tilt = panTilt.tilt;
        return h;
      }

      @Override
      public boolean setPanTilt(PanTiltHolder holder) {
        panTilt.pan = holder.pan;
        panTilt.tilt = holder.tilt;
        return true;
      }

      @Override
      public PanTiltHolder getMinPanTilt() {
        return new PanTiltHolder();
      }

      @Override
      public PanTiltHolder getMaxPanTilt() {
        return new PanTiltHolder();
      }

      @Override
      public int getZoom() {
        return zoom;
      }

      @Override
      public boolean setZoom(int z) {
        zoom = z;
        return true;
      }

      @Override
      public int getMinZoom() {
        return 100;
      }

      @Override
      public int getMaxZoom() {
        return 100;
      }
    };
  }
}
