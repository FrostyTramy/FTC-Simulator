package org.firstinspires.ftc.teamcode.calibration;

import java.io.File;
import java.io.FileWriter;
import java.io.IOException;
import java.io.PrintWriter;
import java.util.Locale;

/**
 * CSV log for the calibration OpModes (docs/calibration.md). On a Robot Controller the files go to
 * /sdcard/FIRST/data (pull them with the REV Hardware Client or adb); in the simulator to "calibration" in the runner's
 * work folder. Uses only java.io, so it builds unchanged in Android Studio.
 */
public final class CalibrationLog implements AutoCloseable {
  private final PrintWriter out;
  private final File file;

  public CalibrationLog(String test, String header) throws IOException {
    File dir = new File("/sdcard/FIRST/data");
    if (!dir.isDirectory()) {
      dir = new File("calibration").getAbsoluteFile();   // absolute: file permissions are checked on the path as written
      if (!dir.isDirectory() && !dir.mkdirs()) {
        throw new IOException("cannot create " + dir.getAbsolutePath());
      }
    }
    File candidate;
    int n = 1;
    do {
      candidate = new File(dir, String.format(Locale.US, "cal_%s_%d.csv", test, n++));
    } while (candidate.exists());
    file = candidate;
    out = new PrintWriter(new FileWriter(file));
    out.println("# " + test);
    out.println(header);
  }

  public File file() {
    return file;
  }

  /** One row; numbers are written with 6 decimals, everything else as text. */
  public void row(Object... values) {
    StringBuilder sb = new StringBuilder();
    for (int i = 0; i < values.length; i++) {
      if (i > 0) {
        sb.append(',');
      }
      Object v = values[i];
      sb.append(v instanceof Double ? String.format(Locale.US, "%.6f", (Double) v) : String.valueOf(v));
    }
    out.println(sb);
  }

  @Override
  public void close() {
    out.close();
  }
}
