package org.firstinspires.ftc.teamcode.util;

import android.os.Environment;
import com.qualcomm.robotcore.eventloop.opmode.OpMode;
import com.qualcomm.robotcore.eventloop.opmode.OpModeManagerNotifier;
import com.qualcomm.robotcore.util.RobotLog;
import java.io.File;
import java.io.FileWriter;
import java.io.IOException;

/** A CSV log on the Robot Controller's storage that closes itself when the OpMode stops. */
public class FileLog implements OpModeManagerNotifier.Notifications {
  private final String fileName;
  private FileWriter writer;

  public FileLog(String name) {
    fileName = name + ".csv";
    try {
      File dir = new File(Environment.getExternalStorageDirectory(), "FTCLogs");
      dir.mkdirs();
      writer = new FileWriter(new File(dir, fileName));
      writer.write("t,x\n");
    } catch (IOException e) {
      RobotLog.ee("FileLog", e, "opening %s failed", fileName);
    }
  }

  public void row(double t, double x) {
    try {
      if (writer != null) {
        writer.write(t + "," + x + "\n");
      }
    } catch (IOException e) {
      RobotLog.ee("FileLog", e, "writing %s failed", fileName);
    }
  }

  @Override
  public void onOpModePreInit(OpMode opMode) {}

  @Override
  public void onOpModePreStart(OpMode opMode) {
    row(-1, 0);   // marks START in the file
  }

  @Override
  public void onOpModePostStop(OpMode opMode) {
    try {
      if (writer != null) {
        writer.write("stopped\n");
        writer.close();
        writer = null;
      }
    } catch (IOException e) {
      RobotLog.ee("FileLog", e, "closing %s failed", fileName);
    }
  }
}
