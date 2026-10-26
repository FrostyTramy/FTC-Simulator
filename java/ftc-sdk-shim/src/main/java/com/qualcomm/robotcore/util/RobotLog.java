package com.qualcomm.robotcore.util;

import org.firstinspires.ftc.sim.SimRuntime;

/**
 * Robot Controller log (SDK 12.0 public API, subset). Verbose/debug/info lines are dropped; warnings and
 * errors go to the simulator's log (spec 01: no user data beyond error lines).
 */
public class RobotLog {
  public static void v(String message) {}

  public static void v(String format, Object... args) {}

  public static void d(String message) {}

  public static void d(String format, Object... args) {}

  public static void i(String message) {}

  public static void i(String format, Object... args) {}

  public static void w(String message) {
    SimRuntime.get().bus().log("W " + message);
  }

  public static void w(String format, Object... args) {
    w(String.format(format, args));
  }

  public static void e(String message) {
    SimRuntime.get().bus().log("E " + message);
  }

  public static void e(String format, Object... args) {
    e(String.format(format, args));
  }

  public static void vv(String tag, String message) {}

  public static void vv(String tag, String format, Object... args) {}

  public static void dd(String tag, String message) {}

  public static void dd(String tag, String format, Object... args) {}

  public static void ii(String tag, String message) {}

  public static void ii(String tag, String format, Object... args) {}

  public static void ww(String tag, String message) {
    w(tag + ": " + message);
  }

  public static void ww(String tag, String format, Object... args) {
    w(tag + ": " + String.format(format, args));
  }

  public static void ee(String tag, String message) {
    e(tag + ": " + message);
  }

  public static void ee(String tag, String format, Object... args) {
    e(tag + ": " + String.format(format, args));
  }

  public static void ee(String tag, Throwable throwable, String message) {
    e(tag + ": " + message + " (" + throwable + ")");
  }

  public static void ee(String tag, Throwable throwable, String format, Object... args) {
    ee(tag, throwable, String.format(format, args));
  }

  public static void ww(String tag, Throwable throwable, String message) {
    w(tag + ": " + message + " (" + throwable + ")");
  }

  public static void ww(String tag, Throwable throwable, String format, Object... args) {
    ww(tag, throwable, String.format(format, args));
  }

  public static void ii(String tag, Throwable throwable, String message) {}

  public static void ii(String tag, Throwable throwable, String format, Object... args) {}

  public static void dd(String tag, Throwable throwable, String message) {}

  public static void dd(String tag, Throwable throwable, String format, Object... args) {}

  public static void vv(String tag, Throwable throwable, String message) {}

  public static void vv(String tag, Throwable throwable, String format, Object... args) {}

  /** A warning the Driver Station shows until it is cleared (here: one log line). */
  public static void addGlobalWarningMessage(String message) {
    w(message);
  }

  public static void setGlobalWarningMessage(String message) {
    w(message);
  }

  public static void clearGlobalWarningMsg() {}

  public static void logStackTrace(Throwable throwable) {
    e(String.valueOf(throwable));
  }
}
