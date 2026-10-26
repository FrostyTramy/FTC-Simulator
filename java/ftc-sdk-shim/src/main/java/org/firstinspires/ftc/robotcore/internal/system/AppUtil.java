package org.firstinspires.ftc.robotcore.internal.system;

import android.app.Activity;
import java.io.File;

/**
 * Robot Controller app utilities (SDK 12.0 internal API, the parts team code uses, D-039). The app's folders are under
 * the simulated /sdcard (android.os.Environment), the one place robot code may write.
 */
public class AppUtil {
  private static final AppUtil INSTANCE = new AppUtil();
  private static final Activity ACTIVITY = new Activity();

  public static final File ROOT_FOLDER = android.os.Environment.getExternalStorageDirectory();
  public static final File FIRST_FOLDER = new File(ROOT_FOLDER, "FIRST");
  public static final File ROBOT_SETTINGS = new File(FIRST_FOLDER, "settings");
  public static final File ROBOT_DATA_DIR = new File(FIRST_FOLDER, "data");
  public static final File MATCH_LOG_FOLDER = new File(FIRST_FOLDER, "matchlogs");

  public static AppUtil getInstance() {
    return INSTANCE;
  }

  public Activity getActivity() {
    return ACTIVITY;
  }

  public Activity getRootActivity() {
    return ACTIVITY;
  }

  public void ensureDirectoryExists(File directory) {
    directory.mkdirs();
  }
}
