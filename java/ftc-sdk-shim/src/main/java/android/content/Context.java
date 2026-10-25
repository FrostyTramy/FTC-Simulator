package android.content;

import android.content.res.Resources;

/**
 * Stub of Android's Context (spec 06 "Android stubs"): enough for team code that reads resource ids or the package
 * name from {@code hardwareMap.appContext}. The simulator's context is the Robot Controller activity stand-in.
 */
public abstract class Context {
  private final Resources resources = new Resources();

  public Resources getResources() {
    return resources;
  }

  public String getPackageName() {
    return "com.qualcomm.ftcrobotcontroller";
  }

  public Context getApplicationContext() {
    return this;
  }
}
