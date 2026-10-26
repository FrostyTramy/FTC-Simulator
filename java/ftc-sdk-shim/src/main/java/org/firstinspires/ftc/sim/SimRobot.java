package org.firstinspires.ftc.sim;

import com.qualcomm.hardware.gobilda.GoBildaPinpointDriver;
import com.qualcomm.hardware.lynx.LynxModule;
import com.qualcomm.hardware.rev.Rev2mDistanceSensor;
import com.qualcomm.hardware.rev.RevColorSensorV3;
import com.qualcomm.hardware.rev.RevTouchSensor;
import com.qualcomm.hardware.sparkfun.SparkFunOTOS;
import com.qualcomm.robotcore.hardware.HardwareMap;
import com.qualcomm.robotcore.hardware.I2cAddr;
import com.qualcomm.robotcore.hardware.configuration.typecontainers.MotorConfigurationType;
import java.util.ArrayList;
import java.util.List;
import java.util.Map;
import org.firstinspires.ftc.robotcore.external.navigation.Rotation;
import org.firstinspires.ftc.sim.devices.SimCRServo;
import org.firstinspires.ftc.sim.devices.SimDcMotor;
import org.firstinspires.ftc.sim.devices.SimDigitalChannel;
import org.firstinspires.ftc.sim.devices.SimDigitalController;
import org.firstinspires.ftc.sim.devices.SimI2cClient;
import org.firstinspires.ftc.sim.devices.SimI2cDevice;
import org.firstinspires.ftc.sim.devices.PinpointRegisters;
import org.firstinspires.ftc.sim.devices.SimImu;
import org.firstinspires.ftc.sim.devices.SimWebcamName;
import org.firstinspires.ftc.sim.devices.SimMotorController;
import org.firstinspires.ftc.sim.devices.SimServo;
import org.firstinspires.ftc.sim.devices.SimServoController;
import org.firstinspires.ftc.sim.devices.SimVoltageSensor;

/**
 * Builds the HardwareMap of a simulated robot from its device list (the robot YAML, sent by the
 * simulator in INIT), the way the Robot Controller builds it from the active configuration file.
 */
public final class SimRobot {
  /**
   * One configured device. motorType fields are used for type "motor" only; port is the I2C bus or digital port of a
   * sensor; params holds static values of a sensor (e.g. a webcam's calibration).
   */
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
      String orientation,
      Map<String, Double> params) {
    public DeviceSpec(String name, String type, int hub, int port, String motorTypeName, double ticksPerRev,
        double gearing, double maxRpm, double achievableFraction, String orientation) {
      this(name, type, hub, port, motorTypeName, ticksPerRev, gearing, maxRpm, achievableFraction, orientation, Map.of());
    }
  }

  // Value counts of the simulated sensors (sim/Sim.Sensors *.Count).
  private static final int PINPOINT_VALUES = 22;
  private static final int OTOS_VALUES = 30;
  private static final int COLOR_VALUES = 8;
  private static final int DISTANCE_VALUES = 3;

  private static final String[] HUB_NAMES = {"Control Hub", "Expansion Hub 2"};

  private final HardwareMap hardwareMap = new HardwareMap(new android.app.Activity(), null); // the Robot Controller app
  private final List<SimHub> hubs = new ArrayList<>();

  public SimRobot(SimRuntime runtime, List<DeviceSpec> devices, int hubCount) {
    List<SimServoController> servoControllers = new ArrayList<>();
    List<SimMotorController> motorControllers = new ArrayList<>();
    List<SimDigitalController> digitalControllers = new ArrayList<>();
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
      SimDigitalController dc = new SimDigitalController(hub, name);
      digitalControllers.add(dc);
      hardwareMap.put(name, dc);
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
        case "pinpoint":
          // The registers too: a team's own copy of the goBILDA driver reads them (HardwareMap builds it on this client).
          hardwareMap.put(d.name(), new GoBildaPinpointDriver(new SimI2cClient(new SimI2cDevice(d.name(), d.hub(), d.port(), PINPOINT_VALUES),
              I2cAddr.create7bit(GoBildaPinpointDriver.DEFAULT_ADDRESS), new PinpointRegisters()), true));
          break;
        case "otos":
          hardwareMap.put(d.name(), new SparkFunOTOS(i2c(d, OTOS_VALUES, SparkFunOTOS.DEFAULT_ADDRESS)));
          break;
        case "color":
          hardwareMap.put(d.name(), new RevColorSensorV3(i2c(d, COLOR_VALUES, 0x52), true));
          break;
        case "distance":
          hardwareMap.put(d.name(), new Rev2mDistanceSensor(i2c(d, DISTANCE_VALUES, 0x29), true));
          break;
        case "touch":
          digitalControllers.get(d.hub()).attach(d.port(), d.name());
          hardwareMap.put(d.name(), new RevTouchSensor(digitalControllers.get(d.hub()), d.port()));
          break;
        case "digital":
          digitalControllers.get(d.hub()).attach(d.port(), d.name());
          hardwareMap.put(d.name(), new SimDigitalChannel(digitalControllers.get(d.hub()), d.port()));
          break;
        case "webcam":
          hardwareMap.put(d.name(), new SimWebcamName(d.name(), d.params()));
          break;
        case "limelight":
          hardwareMap.put(d.name(), new com.qualcomm.hardware.limelightvision.Limelight3A(d.name()));
          break;
        case "analog":
          hardwareMap.put(d.name(), new com.qualcomm.robotcore.hardware.AnalogInput(d.name(), hubs.get(d.hub()), d.port(),
              d.params().getOrDefault("volts", 0.0)));
          break;
        default:
          throw new IllegalArgumentException("unsupported device type '" + d.type() + "' for " + d.name());
      }
    }
  }

  private static SimI2cClient i2c(DeviceSpec d, int values, int address7Bit) {
    return new SimI2cClient(new SimI2cDevice(d.name(), d.hub(), d.port(), values), I2cAddr.create7bit(address7Bit));
  }

  public HardwareMap hardwareMap() {
    return hardwareMap;
  }

  public List<SimHub> hubs() {
    return hubs;
  }
}
