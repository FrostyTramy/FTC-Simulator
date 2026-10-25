package org.firstinspires.ftc.sim.runner;

import com.qualcomm.robotcore.eventloop.opmode.OpMode;
import com.qualcomm.robotcore.hardware.Gamepad;
import java.io.IOException;
import java.io.InputStream;
import java.io.OutputStream;
import java.net.Socket;
import java.net.URL;
import java.net.URLClassLoader;
import java.nio.file.Files;
import java.nio.file.Path;
import java.util.ArrayList;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;
import org.firstinspires.ftc.sim.DeviceBus;
import org.firstinspires.ftc.sim.HardwareCosts;
import org.firstinspires.ftc.sim.OpModeDriver;
import org.firstinspires.ftc.sim.SimRobot;
import org.firstinspires.ftc.sim.SimRuntime;
import org.firstinspires.ftc.sim.SimTelemetry;

/**
 * The OpMode runner process (spec 01, docs/ipc_protocol.md). Usage:
 * {@code java -cp shim.jar;runner.jar org.firstinspires.ftc.sim.runner.RunnerMain --port N --token T --teamcode
 * teamcode.jar [--workdir DIR]}. Connects to the simulator, proves it is the process the simulator started (the
 * one-time token in HELLO_ACK), answers its messages and runs one OpMode in lockstep.
 */
public final class RunnerMain {
  static final int PROTOCOL_VERSION = 2;
  static final String SDK_VERSION = "12.0.0-sim";
  static final long STUCK_WALL_MS = 5_000;

  private final InputStream in;
  private final OutputStream out;
  private final ClassLoader teamCode;
  private final String teamCodeJar;
  private final String token;
  private SimRuntime runtime;
  private SimRobot robot;
  private SimTelemetry telemetry;
  private OpModeDriver driver;
  private long lastGamepadNanos = Long.MIN_VALUE / 2;
  private long lastEndNanos;

  RunnerMain(InputStream in, OutputStream out, ClassLoader teamCode, String teamCodeJar, String token) {
    this.in = in;
    this.out = out;
    this.teamCode = teamCode;
    this.teamCodeJar = teamCodeJar;
    this.token = token;
  }

  public static void main(String[] args) throws Exception {
    int port = -1;
    String jar = null;
    String token = null;
    Path workdir = Path.of(System.getProperty("java.io.tmpdir"), "ftc-sim-runner");
    for (int i = 0; i + 1 < args.length; i += 2) {
      switch (args[i]) {
        case "--port" -> port = Integer.parseInt(args[i + 1]);
        case "--teamcode" -> jar = args[i + 1];
        case "--token" -> token = args[i + 1];
        case "--workdir" -> workdir = Path.of(args[i + 1]);
        default -> throw new IllegalArgumentException("unknown argument " + args[i]);
      }
    }
    if (port <= 0 || jar == null || token == null) {
      System.err.println("usage: RunnerMain --port N --token T --teamcode teamcode.jar [--workdir DIR]");
      System.exit(2);
    }
    Files.createDirectories(workdir);
    int status = 0;
    try (Socket socket = new Socket("127.0.0.1", port)) {
      socket.setTcpNoDelay(true);
      URL teamCodeUrl = Path.of(jar).toUri().toURL();
      URLClassLoader loader = new URLClassLoader(new URL[] {teamCodeUrl}, RunnerMain.class.getClassLoader());
      Sandbox.install(teamCodeUrl, workdir);
      new RunnerMain(socket.getInputStream(), socket.getOutputStream(), loader, jar, token).serve();
    } catch (IOException e) {
      System.err.println("runner: " + e);
      status = 1;
    }
    System.exit(status); // also ends threads the team code may have left running
  }

  /** Handles messages until QUIT or end of stream. */
  void serve() throws Exception {
    while (true) {
      Wire.In msg = Wire.read(in);
      if (msg == null) {
        return;
      }
      int type = msg.u8();
      switch (type) {
        case 1 -> {
          msg.i32();
          new Wire.Out(101).i32(PROTOCOL_VERSION).str(SDK_VERSION).str(token).send(out);
        }
        case 2 -> listOpModes();
        case 3 -> configure(msg);
        case 4 -> init(msg.str());
        case 5 -> {
          if (driver != null) {
            driver.start();
          }
          new Wire.Out(105).send(out);
        }
        case 6 -> {
          if (driver != null) {
            driver.stop();
          }
          new Wire.Out(106).send(out);
        }
        case 7 -> tick(msg);
        case 8 -> {
          return;
        }
        default -> new Wire.Out(199).str("unknown message type " + type).send(out);
      }
    }
  }

  private void listOpModes() throws IOException {
    List<OpModeScanner.Entry> entries = OpModeScanner.scan(teamCodeJar, teamCode);
    Wire.Out reply = new Wire.Out(102).i32(entries.size());
    for (OpModeScanner.Entry e : entries) {
      reply.str(e.className()).str(e.name()).str(e.group()).u8(e.autonomous() ? 1 : 0).str(e.preselectTeleOp());
    }
    reply.send(out);
  }

  private void configure(Wire.In msg) throws IOException {
    HardwareCosts costs = new HardwareCosts(msg.i64(), msg.i64(), msg.i64(), msg.i64(), msg.i64(), msg.i64(), msg.i64(),
        msg.i64(), msg.i64());
    int hubCount = msg.i32();
    int n = msg.i32();
    List<SimRobot.DeviceSpec> devices = new ArrayList<>();
    for (int i = 0; i < n; i++) {
      String name = msg.str();
      String type = msg.str();
      int hub = msg.i32();
      int port = msg.i32();
      String motorType = msg.str();
      double ticksPerRev = msg.f64();
      double gearing = msg.f64();
      double maxRpm = msg.f64();
      double achievable = msg.f64();
      String orientation = msg.str();
      Map<String, Double> params = new LinkedHashMap<>();
      int p = msg.i32();
      for (int j = 0; j < p; j++) {
        params.put(msg.str(), msg.f64());
      }
      devices.add(new SimRobot.DeviceSpec(name, type, hub, port, motorType, ticksPerRev, gearing, maxRpm, achievable,
          orientation, params));
    }
    runtime = new SimRuntime(costs);
    SimRuntime.install(runtime);
    robot = new SimRobot(runtime, devices, hubCount);
    telemetry = new SimTelemetry(runtime);
    driver = null;
    lastGamepadNanos = Long.MIN_VALUE / 2;
    lastEndNanos = 0;
    new Wire.Out(103).send(out);
  }

  private void init(String className) throws IOException {
    try {
      if (robot == null) {
        throw new IllegalStateException("CONFIGURE must come before INIT");
      }
      Class<?> cls = Class.forName(className, false, teamCode); // static initializers run on the OpMode thread
      driver = new OpModeDriver(runtime, cls.asSubclass(OpMode.class), robot.hardwareMap(), telemetry);
      driver.init();
      new Wire.Out(104).send(out);
    } catch (ClassNotFoundException | ClassCastException | IllegalStateException | LinkageError e) {
      // LinkageError: the team code uses an SDK class or method the simulator does not provide (yet).
      new Wire.Out(199).str("Cannot initialize OpMode " + className + ": " + e).send(out);
    }
  }

  private static Gamepad readGamepad(Wire.In msg) throws IOException {
    Gamepad g = new Gamepad();
    g.left_stick_x = msg.f32();
    g.left_stick_y = msg.f32();
    g.right_stick_x = msg.f32();
    g.right_stick_y = msg.f32();
    g.left_trigger = msg.f32();
    g.right_trigger = msg.f32();
    int b = msg.i32();
    g.a = g.cross = (b & 1) != 0;
    g.b = g.circle = (b & 1 << 1) != 0;
    g.x = g.square = (b & 1 << 2) != 0;
    g.y = g.triangle = (b & 1 << 3) != 0;
    g.dpad_up = (b & 1 << 4) != 0;
    g.dpad_down = (b & 1 << 5) != 0;
    g.dpad_left = (b & 1 << 6) != 0;
    g.dpad_right = (b & 1 << 7) != 0;
    g.left_bumper = (b & 1 << 8) != 0;
    g.right_bumper = (b & 1 << 9) != 0;
    g.left_stick_button = (b & 1 << 10) != 0;
    g.right_stick_button = (b & 1 << 11) != 0;
    g.back = g.share = (b & 1 << 12) != 0;
    g.start = g.options = (b & 1 << 13) != 0;
    g.guide = g.ps = (b & 1 << 14) != 0;
    g.touchpad = (b & 1 << 15) != 0;
    g.id = msg.i32();
    return g;
  }

  private void tick(Wire.In msg) throws Exception {
    msg.i64(); // tick number (for logs)
    long endNanos = msg.i64();
    Gamepad g1 = readGamepad(msg);
    Gamepad g2 = readGamepad(msg);
    DeviceBus bus = runtime != null ? runtime.bus() : null;
    double volts = msg.f64();
    double[] q = {msg.f64(), msg.f64(), msg.f64(), msg.f64()};
    double[] w = {msg.f64(), msg.f64(), msg.f64()};
    int n = msg.i32();
    for (int i = 0; i < n; i++) {
      String key = msg.str();
      int position = msg.i32();
      double velocity = msg.f64();
      double current = msg.f64();
      boolean busy = msg.bool();
      if (bus != null) {
        DeviceBus.MotorInput in = bus.motorInput(key);
        in.position = position;
        in.velocity = velocity;
        in.currentAmps = current;
        in.busy = busy;
      }
    }
    int sensors = msg.i32();
    for (int i = 0; i < sensors; i++) {
      String name = msg.str();
      double[] values = new double[msg.i32()];
      for (int j = 0; j < values.length; j++) {
        values[j] = msg.f64();
      }
      if (bus != null) {
        bus.setSensorInput(name, values);
      }
    }
    if (bus != null) {
      bus.countTick();
    }
    int state = 5; // IDLE
    String error = null;
    if (bus != null) {
      bus.setBatteryVolts(volts);
      bus.setImu(q, w);
      bus.setInputTime(lastEndNanos); // inputs were sampled at the end of the previous step
      if (driver != null) {
        if (endNanos - lastGamepadNanos >= runtime.costs().gamepadPeriodNanos) {
          driver.setGamepads(g1, g2);
          lastGamepadNanos = endNanos;
        }
        driver.runUntil(endNanos, STUCK_WALL_MS);
        driver.eventLoopIteration();
        state = driver.state().ordinal();
        error = driver.error();
      } else {
        runtime.advanceIdle(endNanos);
      }
    }
    lastEndNanos = endNanos;
    sendState(state, error);
  }

  private void sendState(int state, String error) throws IOException {
    Wire.Out reply = new Wire.Out(107).i64(runtime != null ? runtime.nowNanos() : 0).u8(state).str(error);
    if (runtime == null) {
      reply.i32(0).i32(0).i32(0).i64(0).i32(0).i32(0).send(out);
      return;
    }
    Map<String, DeviceBus.MotorCommand> motors = runtime.bus().motorCommands();
    reply.i32(motors.size());
    for (Map.Entry<String, DeviceBus.MotorCommand> e : motors.entrySet()) {
      DeviceBus.MotorCommand c = e.getValue();
      reply.str(e.getKey()).str(c.mode).f64(c.power).bool(c.velocitySet).f64(c.velocity).i32(c.targetPosition).str(c.zeroPower)
          .i32(c.tolerance).bool(c.enabled).i32(c.resetCount).bool(c.velocityPidf != null);
      double[] pidf = c.velocityPidf != null ? c.velocityPidf : new double[4];
      for (double v : pidf) {
        reply.f64(v);
      }
      reply.f64(c.positionP);
    }
    Map<String, DeviceBus.ServoCommand> servos = runtime.bus().servoCommands();
    reply.i32(servos.size());
    for (Map.Entry<String, DeviceBus.ServoCommand> e : servos.entrySet()) {
      reply.str(e.getKey()).f64(e.getValue().pulseUs).bool(e.getValue().pwmEnabled);
    }
    List<DeviceBus.SensorCommand> commands = runtime.bus().drainSensorCommands();
    reply.i32(commands.size());
    for (DeviceBus.SensorCommand c : commands) {
      reply.str(c.name()).str(c.command()).i32(c.args().length);
      for (double a : c.args()) {
        reply.f64(a);
      }
    }
    reply.i64(telemetry.transmissions());
    List<String> lines = telemetry.lastTransmitted();
    reply.i32(lines.size());
    for (String line : lines) {
      reply.str(line);
    }
    List<String> log = runtime.bus().drainLog();
    reply.i32(log.size());
    for (String line : log) {
      reply.str(line);
    }
    reply.send(out);
  }
}
