package org.firstinspires.ftc.sim;

import com.qualcomm.hardware.lynx.LynxModule;
import com.qualcomm.robotcore.hardware.HardwareMap;
import com.qualcomm.robotcore.hardware.configuration.typecontainers.MotorConfigurationType;
import java.util.ArrayList;
import java.util.List;
import org.firstinspires.ftc.robotcore.external.navigation.Rotation;
import org.firstinspires.ftc.sim.devices.SimCRServo;
import org.firstinspires.ftc.sim.devices.SimDcMotor;
import org.firstinspires.ftc.sim.devices.SimImu;
import org.firstinspires.ftc.sim.devices.SimMotorController;
import org.firstinspires.ftc.sim.devices.SimServo;
import org.firstinspires.ftc.sim.devices.SimServoController;
import org.firstinspires.ftc.sim.devices.SimVoltageSensor;

/**
 * Builds the HardwareMap of a simulated robot from its device list (the robot YAML, sent by the
 * simulator in INIT), the way the Robot Controller builds it from the active configuration file.
 */
public final class SimRobot {
  /** One configured device. motorType fields are used for type "motor" only. */
  public record DeviceSpec(
      String name,
      String type,
      int hub,
      int port,
      String motorTypeName,
      double ticksPerRev,
      double gearing,
      double maxRpm,
      double achievableFraction,
      String orientation) {}

  private static final String[] HUB_NAMES = {"Control Hub", "Expansion Hub 2"};

  private final HardwareMap hardwareMap = new HardwareMap(null, null);
  private final List<SimHub> hubs = new ArrayList<>();

  public SimRobot(SimRuntime runtime, List<DeviceSpec> devices, int hubCount) {
    List<SimServoController> servoControllers = new ArrayList<>();
    List<SimMotorController> motorControllers = new ArrayList<>();
    for (int h = 0; h < hubCount; h++) {
      SimHub hub = new SimHub(h, runtime);
      hubs.add(hub);
      String name = HUB_NAMES[Math.min(h, HUB_NAMES.length - 1)];
      hardwareMap.put(name, new LynxModule(hub, name, h == 0));
      hardwareMap.put(name, new SimVoltageSensor(hub, name));
      SimMotorController mc = new SimMotorController(name);
      SimServoController sc = new SimServoController(hub, name);
      motorControllers.add(mc);
      servoControllers.add(sc);
      hardwareMap.put(name, mc);
      hardwareMap.put(name, sc);
    }
    for (DeviceSpec d : devices) {
      switch (d.type()) {
        case "motor":
          MotorConfigurationType type = new MotorConfigurationType(d.motorTypeName(), d.motorTypeName(), d.ticksPerRev(),
              d.gearing(), d.maxRpm(), d.achievableFraction(), "cw".equalsIgnoreCase(d.orientation()) ? Rotation.CW : Rotation.CCW);
          hardwareMap.put(d.name(), new SimDcMotor(hubs.get(d.hub()), d.port(), type, motorControllers.get(d.hub())));
          break;
        case "servo":
          hardwareMap.put(d.name(), new SimServo(servoControllers.get(d.hub()), d.port()));
          break;
        case "crservo":
          hardwareMap.put(d.name(), new SimCRServo(servoControllers.get(d.hub()), d.port()));
          break;
        case "imu":
          hardwareMap.put(d.name(), new SimImu(d.name()));
          break;
        default:
          throw new IllegalArgumentException("unsupported device type '" + d.type() + "' for " + d.name());
      }
    }
  }

  public HardwareMap hardwareMap() {
    return hardwareMap;
  }

  public List<SimHub> hubs() {
    return hubs;
  }
}
