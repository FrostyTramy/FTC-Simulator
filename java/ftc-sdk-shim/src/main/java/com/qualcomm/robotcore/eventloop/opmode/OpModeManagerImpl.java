package com.qualcomm.robotcore.eventloop.opmode;

import android.app.Activity;
import java.util.List;
import java.util.concurrent.CopyOnWriteArrayList;
import org.firstinspires.ftc.sim.SimRuntime;

/**
 * The Robot Controller's OpMode manager (SDK 12.0 internal API, the parts team code uses: the active OpMode and its
 * lifecycle listeners, D-039). Listeners run on the OpMode's thread; one that throws is logged and skipped.
 */
public class OpModeManagerImpl implements OpModeManagerNotifier {
  public static final String DEFAULT_OP_MODE_NAME = "$Stop$Robot$";
  private static final OpModeManagerImpl INSTANCE = new OpModeManagerImpl();

  private final List<Notifications> listeners = new CopyOnWriteArrayList<>();
  private volatile OpMode active;
  private volatile String activeName = DEFAULT_OP_MODE_NAME;

  public static OpModeManagerImpl getOpModeManagerOfActivity(Activity activity) {
    return INSTANCE;
  }

  public OpMode getActiveOpMode() {
    return active;
  }

  public String getActiveOpModeName() {
    return activeName;
  }

  @Override
  public OpMode registerListener(Notifications listener) {
    listeners.add(listener);
    return active;
  }

  @Override
  public void unregisterListener(Notifications listener) {
    listeners.remove(listener);
  }

  /** Simulator: the OpMode the Driver Station INIT-ed (called on its thread before its code runs). */
  public static void simPreInit(OpMode opMode, String name) {
    INSTANCE.active = opMode;
    INSTANCE.activeName = name;
    INSTANCE.notifyAll(opMode, 0);
  }

  public static void simPreStart(OpMode opMode) {
    INSTANCE.notifyAll(opMode, 1);
  }

  public static void simPostStop(OpMode opMode) {
    INSTANCE.notifyAll(opMode, 2);
    INSTANCE.active = null;
    INSTANCE.activeName = DEFAULT_OP_MODE_NAME;
  }

  private void notifyAll(OpMode opMode, int event) {
    for (Notifications l : listeners) {
      try {
        switch (event) {
          case 0 -> l.onOpModePreInit(opMode);
          case 1 -> l.onOpModePreStart(opMode);
          default -> l.onOpModePostStop(opMode);
        }
      } catch (RuntimeException e) {
        SimRuntime.get().bus().log("W OpMode listener " + l.getClass().getSimpleName() + " threw " + e);
      }
    }
  }
}
