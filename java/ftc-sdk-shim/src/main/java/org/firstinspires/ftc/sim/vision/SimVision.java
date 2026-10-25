package org.firstinspires.ftc.sim.vision;

import java.io.IOException;
import java.nio.MappedByteBuffer;
import java.nio.channels.FileChannel;
import java.nio.file.Files;
import java.nio.file.Path;
import java.nio.file.StandardOpenOption;
import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;
import java.util.concurrent.ExecutionException;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;
import java.util.concurrent.Future;
import java.util.concurrent.TimeUnit;
import java.util.concurrent.TimeoutException;

/**
 * The cameras of the running session (spec 09 "Robot cameras"). The runner hands every frame of a TICK to
 * {@link #deliver} before the OpMode runs that tick; the portal of that camera runs its processors on the vision thread
 * while the OpMode thread is parked, so results appear at the same tick in every run (determinism). Pixels come from
 * the camera's shared-memory file {@code <workdir>/camera-<index>-<tag>.bin}, written by the simulator.
 */
public final class SimVision {
  public static final long PROCESSOR_TIMEOUT_MS = 5_000;
  private static volatile SimVision current = new SimVision(null, "");
  private static final ThreadLocal<SimFrame> PROCESSING = new ThreadLocal<>();

  private final Path workDir;
  private final String tag;
  private final Map<String, SimVisionPortal> portals = new HashMap<>();
  private final Map<Integer, MappedByteBuffer> buffers = new HashMap<>();
  private final ExecutorService worker = Executors.newSingleThreadExecutor(r -> {
    Thread t = new Thread(r, "VisionPortal");
    t.setDaemon(true);
    return t;
  });

  /** {@code tag} names the session's frame files: {@code camera-<index>-<tag>.bin}. */
  public SimVision(Path workDir, String tag) {
    this.workDir = workDir;
    this.tag = tag;
  }

  public static SimVision get() {
    return current;
  }

  public static void install(SimVision vision) {
    current = vision;
  }

  /** The frame whose processors are running on this thread (the AprilTag processor reads its detections). */
  public static SimFrame processingFrame() {
    return PROCESSING.get();
  }

  /** Runs {@code action} with {@code frame} as the frame being processed (its detections reach the AprilTag processor). */
  public static void runWithFrame(SimFrame frame, Runnable action) {
    SimFrame previous = PROCESSING.get();
    PROCESSING.set(frame);
    try {
      action.run();
    } finally {
      if (previous == null) {
        PROCESSING.remove();
      } else {
        PROCESSING.set(previous);
      }
    }
  }

  synchronized void register(String camera, SimVisionPortal portal) {
    SimVisionPortal old = portals.get(camera);
    if (old != null && old != portal && !old.isClosed()) {
      throw new RuntimeException("Camera " + camera + " is already in use by another VisionPortal");
    }
    portals.put(camera, portal);
  }

  synchronized void unregister(String camera, SimVisionPortal portal) {
    portals.remove(camera, portal);
  }

  synchronized List<SimVisionPortal> openPortals() {
    return new ArrayList<>(portals.values());
  }

  /** Runner: one frame from the simulator; returns an error line if a processor failed or hung. */
  public String deliver(SimFrame frame) {
    SimVisionPortal portal;
    synchronized (this) {
      portal = portals.get(frame.camera());
    }
    if (portal == null) {
      return null;
    }
    Future<?> job = worker.submit(() -> runWithFrame(frame, () -> portal.process(frame)));
    try {
      job.get(PROCESSOR_TIMEOUT_MS, TimeUnit.MILLISECONDS);
      return null;
    } catch (TimeoutException e) {
      job.cancel(true);
      portal.fail();
      return "A VisionProcessor on camera " + frame.camera() + " did not return within "
          + PROCESSOR_TIMEOUT_MS / 1000 + " s";
    } catch (ExecutionException e) {
      portal.fail();
      return "A VisionProcessor on camera " + frame.camera() + " threw " + e.getCause();
    } catch (InterruptedException e) {
      Thread.currentThread().interrupt();
      return null;
    }
  }

  /** RGBA bytes of a frame slot of camera {@code index}. */
  synchronized void readPixels(int index, SimFrame frame, byte[] into) {
    MappedByteBuffer buffer = buffers.get(index);
    long needed = (long) (frame.slot() + 1) * frame.width() * frame.height() * 4;
    if (buffer == null || buffer.capacity() < needed) {
      buffer = map(index);
      buffers.put(index, buffer);
    }
    buffer.get(frame.slot() * frame.width() * frame.height() * 4, into, 0, frame.width() * frame.height() * 4);
  }

  private MappedByteBuffer map(int index) {
    if (workDir == null) {
      throw new IllegalStateException("no camera frames outside a runner session");
    }
    Path file = workDir.resolve("camera-" + index + "-" + tag + ".bin");
    try (FileChannel channel = FileChannel.open(file, StandardOpenOption.READ)) {
      return channel.map(FileChannel.MapMode.READ_ONLY, 0, Files.size(file));
    } catch (IOException e) {
      throw new IllegalStateException("cannot map camera frames " + file + ": " + e.getMessage(), e);
    }
  }

  /** Runner: stop the vision thread (session end). */
  public void shutdown() {
    worker.shutdownNow();
  }
}
