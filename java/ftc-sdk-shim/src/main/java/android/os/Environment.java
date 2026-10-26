package android.os;

import java.io.File;

/**
 * Android's Environment: the Robot Controller's shared storage (/sdcard). In the simulator it is the folder "sdcard"
 * in the robot's working directory, the one place the sandbox lets robot code write files.
 */
public class Environment {
  public static File getExternalStorageDirectory() {
    File sdcard = new File("sdcard").getAbsoluteFile();
    sdcard.mkdirs();
    return sdcard;
  }
}
