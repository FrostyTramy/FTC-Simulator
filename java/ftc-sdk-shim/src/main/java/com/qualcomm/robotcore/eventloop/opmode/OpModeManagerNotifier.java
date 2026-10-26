package com.qualcomm.robotcore.eventloop.opmode;

/** Notifies listeners about OpMode changes (SDK 12.0 public API). */
public interface OpModeManagerNotifier {
  /** Called on the OpMode's thread before INIT, before START and after it stopped. */
  interface Notifications {
    void onOpModePreInit(OpMode opMode);

    void onOpModePreStart(OpMode opMode);

    void onOpModePostStop(OpMode opMode);
  }

  OpMode registerListener(Notifications listener);

  void unregisterListener(Notifications listener);
}
