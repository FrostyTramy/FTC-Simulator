package org.firstinspires.ftc.teamcode.opmodes;

import android.text.TextUtils;
import com.acmerobotics.dashboard.config.Config;
import com.bylazar.configurables.annotations.Configurable;
import com.bylazar.configurables.annotations.IgnoreConfigurable;
import com.bylazar.telemetry.PanelsTelemetry;
import com.bylazar.telemetry.TelemetryManager;
import com.qualcomm.hardware.limelightvision.LLResult;
import com.qualcomm.hardware.limelightvision.Limelight3A;
import com.qualcomm.robotcore.eventloop.opmode.LinearOpMode;
import com.qualcomm.robotcore.eventloop.opmode.OpModeManagerImpl;
import com.qualcomm.robotcore.eventloop.opmode.TeleOp;
import com.qualcomm.robotcore.hardware.AnalogInput;
import com.qualcomm.robotcore.util.RobotLog;
import java.util.ArrayList;
import java.util.List;
import org.firstinspires.ftc.robotcore.internal.system.AppUtil;
import org.firstinspires.ftc.teamcode.pinpoint.TeamPinpoint;
import org.firstinspires.ftc.teamcode.util.FileLog;

@Config
@Configurable
@TeleOp(name = "RD4 API check")
public class ApiTeleOp extends LinearOpMode {
  public static double LOG_EVERY_S = 0.1;
  @IgnoreConfigurable public static int hidden = 1;

  @Override
  public void runOpMode() {
    List<String> missing = new ArrayList<>();
    AnalogInput analog = hardwareMap.tryGet(AnalogInput.class, "sensor_analog");
    if (analog == null) {
      missing.add("sensor_analog");
    }
    if (hardwareMap.tryGet(AnalogInput.class, "analog3") == null) {
      missing.add("analog3");
    }
    if (!missing.isEmpty()) {
      RobotLog.addGlobalWarningMessage("Not in robot config: " + TextUtils.join(", ", missing));
    }
    Limelight3A limelight = hardwareMap.get(Limelight3A.class, "limelight");
    limelight.pipelineSwitch(0);
    limelight.setPollRateHz(100);
    limelight.start();
    TeamPinpoint pinpoint = hardwareMap.get(TeamPinpoint.class, "pinpoint");
    pinpoint.setOffsets(84, -168);
    pinpoint.resetPosAndIMU();
    OpModeManagerImpl manager = OpModeManagerImpl.getOpModeManagerOfActivity(AppUtil.getInstance().getActivity());
    FileLog log = new FileLog("api_check");
    manager.registerListener(log);
    TelemetryManager panels = PanelsTelemetry.INSTANCE.getTelemetry();
    long previousInterval = panels.getUpdateInterval();
    panels.setUpdateInterval(25);
    telemetry.addData("id", pinpoint.getDeviceID());
    telemetry.update();
    waitForStart();
    double lastLog = 0;
    while (opModeIsActive()) {
      pinpoint.update();
      LLResult result = limelight.getLatestResult();
      panels.addData("x mm", String.format(java.util.Locale.ROOT, "%.1f", pinpoint.getPosX()));
      panels.addData("heading", String.format(java.util.Locale.ROOT, "%.3f", pinpoint.getHeading()));
      panels.addData("status", pinpoint.getDeviceStatus());
      panels.addData("limelight valid", result != null && result.isValid());
      panels.addData("analog V", analog == null ? -1 : analog.getVoltage());
      panels.debug("active " + manager.getActiveOpModeName());
      if (getRuntime() - lastLog >= LOG_EVERY_S) {
        lastLog = getRuntime();
        log.row(lastLog, pinpoint.getPosX());
      }
      panels.update(telemetry);
    }
    panels.setUpdateInterval(previousInterval);   // the interval is global to Panels
  }
}
