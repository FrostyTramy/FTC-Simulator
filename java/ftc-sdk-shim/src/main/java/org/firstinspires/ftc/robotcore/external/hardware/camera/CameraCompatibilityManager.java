package org.firstinspires.ftc.robotcore.external.hardware.camera;

import java.util.ArrayList;
import java.util.HashMap;
import java.util.List;
import java.util.Map;

/** USB camera quirks by vendor/product id (SDK 12.0 public API); simulated cameras need none, the list is kept. */
public abstract class CameraCompatibilityManager {
  /** Work-arounds for misbehaving cameras. */
  public enum Quirk {
    RESET_ALT_SETTING_ON_RELEASE(1),
    AVOID_LIB_USB_RESET_DEVICE(2);

    public final int bitmask;

    Quirk(int bitmask) {
      this.bitmask = bitmask;
    }
  }

  private static CameraCompatibilityManager instance;

  public CameraCompatibilityManager() {}

  public abstract void addQuirk(int vid, int pid, Quirk quirk);

  public abstract void removeQuirk(int vid, int pid, Quirk quirk);

  public abstract List<Quirk> getQuirks(int vid, int pid);

  public static synchronized CameraCompatibilityManager getInstance() {
    if (instance == null) {
      instance = new CameraCompatibilityManager() {
        private final Map<Long, List<Quirk>> quirks = new HashMap<>();

        private long key(int vid, int pid) {
          return ((long) vid << 32) | (pid & 0xFFFFFFFFL);
        }

        @Override
        public synchronized void addQuirk(int vid, int pid, Quirk quirk) {
          List<Quirk> list = quirks.computeIfAbsent(key(vid, pid), k -> new ArrayList<>());
          if (!list.contains(quirk)) {
            list.add(quirk);
          }
        }

        @Override
        public synchronized void removeQuirk(int vid, int pid, Quirk quirk) {
          List<Quirk> list = quirks.get(key(vid, pid));
          if (list != null) {
            list.remove(quirk);
          }
        }

        @Override
        public synchronized List<Quirk> getQuirks(int vid, int pid) {
          return new ArrayList<>(quirks.getOrDefault(key(vid, pid), List.of()));
        }
      };
    }
    return instance;
  }
}
