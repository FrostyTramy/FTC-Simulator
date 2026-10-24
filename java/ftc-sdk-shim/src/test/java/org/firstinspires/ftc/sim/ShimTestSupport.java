package org.firstinspires.ftc.sim;

import java.util.List;

/** A fresh runtime + the goBILDA StarterBot devices (config/robots/gobilda_starter.yaml) for tests. */
final class ShimTestSupport {
  static final double GOBILDA_TPR = 2786.0; // SDK 12.0 GoBILDA5202Series @MotorType
  static final double GOBILDA_GEARING = 99.5;
  static final double GOBILDA_MAX_RPM = 60.0;

  private ShimTestSupport() {}

  static SimRuntime freshRuntime() {
    SimRuntime runtime = new SimRuntime(HardwareCosts.defaults());
    SimRuntime.install(runtime);
    return runtime;
  }

  static SimRobot.DeviceSpec motor(String name, int hub, int port) {
    return new SimRobot.DeviceSpec(name, "motor", hub, port, "goBILDA 5202/3/4 series", GOBILDA_TPR, GOBILDA_GEARING,
        GOBILDA_MAX_RPM, 0.85, "ccw");
  }

  static SimRobot starterBot(SimRuntime runtime) {
    return new SimRobot(runtime, List.of(
        motor("left_front_drive", 0, 0),
        motor("right_front_drive", 0, 1),
        motor("left_back_drive", 0, 2),
        motor("right_back_drive", 0, 3),
        motor("intake", 1, 0),
        motor("launcher", 1, 1),
        new SimRobot.DeviceSpec("windmillServo", "crservo", 0, 0, null, 0, 0, 0, 0, null),
        new SimRobot.DeviceSpec("left_intake_servo", "crservo", 0, 1, null, 0, 0, 0, 0, null),
        new SimRobot.DeviceSpec("right_intake_servo", "crservo", 0, 2, null, 0, 0, 0, 0, null),
        new SimRobot.DeviceSpec("arm", "servo", 0, 3, null, 0, 0, 0, 0, null),
        new SimRobot.DeviceSpec("imu", "imu", 0, 0, null, 0, 0, 0, 0, null)), 2);
  }
}
