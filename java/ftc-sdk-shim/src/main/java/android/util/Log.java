package android.util;

import com.qualcomm.robotcore.util.RobotLog;

/** Stub of Android's Log (spec 06 "Android stubs"): warnings and errors go to the Robot Controller log. */
public final class Log {
  private Log() {}

  public static int v(String tag, String msg) {
    return 0;
  }

  public static int d(String tag, String msg) {
    return 0;
  }

  public static int i(String tag, String msg) {
    return 0;
  }

  public static int w(String tag, String msg) {
    RobotLog.ww(tag, msg);
    return 0;
  }

  public static int e(String tag, String msg) {
    RobotLog.ee(tag, msg);
    return 0;
  }

  public static int e(String tag, String msg, Throwable tr) {
    RobotLog.ee(tag, tr, msg);
    return 0;
  }
}
