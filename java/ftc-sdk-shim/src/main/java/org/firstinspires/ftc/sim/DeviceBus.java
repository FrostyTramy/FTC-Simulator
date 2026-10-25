package org.firstinspires.ftc.sim;

import java.util.ArrayList;
import java.util.Collections;
import java.util.LinkedHashMap;
import java.util.List;
import java.util.Map;

/**
 * The robot's hardware as seen by the shim: the latest inputs from the simulator (encoders, currents,
 * battery, IMU) and the commands the OpMode issued (raw values, after the SDK applied directions).
 * Motor and servo control (PID, RUN_TO_POSITION, PWM) is emulated on the simulator side, like the
 * firmware of a real REV hub (sim/Sim.Hardware).
 */
public final class DeviceBus {
  /** Raw state of one motor port, as the simulator reports it (motor's own direction). */
  public static final class MotorInput {
    public int position;
    public double velocity; // ticks per second
    public double currentAmps;
    public boolean busy;
    public boolean overCurrent;
  }

  /** Raw command of one motor port, sent to the simulator every tick. */
  public static final class MotorCommand {
    public String mode = "RUN_WITHOUT_ENCODER";
    public double power; // raw, −1..1, direction already applied
    public boolean velocitySet;
    public double velocity; // raw ticks per second (DcMotorEx.setVelocity)
    public int targetPosition; // raw counts
    public String zeroPower = "FLOAT";
    public int tolerance = 5;
    public double[] velocityPidf; // null = firmware default
    public double positionP = Double.NaN; // NaN = firmware default
    public boolean enabled = true;
    public int resetCount; // incremented by STOP_AND_RESET_ENCODER
  }

  /** Raw command of one servo port: PWM pulse width or disabled. */
  public static final class ServoCommand {
    public double pulseUs = Double.NaN; // NaN until the first setPosition / setPower
    public boolean pwmEnabled = true;
  }

  /** A write of the robot code to a sensor device, applied by the simulator in its next step. */
  public record SensorCommand(String name, String command, double[] args) {}

  private final Map<String, MotorInput> motorInputs = new LinkedHashMap<>();
  private final Map<String, MotorCommand> motorCommands = new LinkedHashMap<>();
  private final Map<String, ServoCommand> servoCommands = new LinkedHashMap<>();
  private final List<String> logLines = new ArrayList<>();
  private final Map<String, double[]> sensorInputs = new LinkedHashMap<>();
  private final List<SensorCommand> sensorCommands = new ArrayList<>();
  private long ticks;
  private double batteryVolts = 12.0;
  private double[] imuQuaternion = {1, 0, 0, 0}; // w, x, y, z of the IMU chip frame in the world (Z up)
  private double[] imuAngularVelocity = {0, 0, 0}; // rad/s about the IMU chip axes
  private long inputTimeNanos;

  public synchronized MotorInput motorInput(String port) {
    return motorInputs.computeIfAbsent(port, k -> new MotorInput());
  }

  /** Deep copy of all motor inputs (a bulk read snapshot). */
  public synchronized Map<String, MotorInput> copyMotorInputs() {
    Map<String, MotorInput> copy = new LinkedHashMap<>();
    for (Map.Entry<String, MotorInput> e : motorInputs.entrySet()) {
      MotorInput src = e.getValue();
      MotorInput dst = new MotorInput();
      dst.position = src.position;
      dst.velocity = src.velocity;
      dst.currentAmps = src.currentAmps;
      dst.busy = src.busy;
      dst.overCurrent = src.overCurrent;
      copy.put(e.getKey(), dst);
    }
    return copy;
  }

  public synchronized MotorCommand motorCommand(String port) {
    return motorCommands.computeIfAbsent(port, k -> new MotorCommand());
  }

  public synchronized ServoCommand servoCommand(String port) {
    return servoCommands.computeIfAbsent(port, k -> new ServoCommand());
  }

  public synchronized Map<String, MotorCommand> motorCommands() {
    return Collections.unmodifiableMap(new LinkedHashMap<>(motorCommands));
  }

  public synchronized Map<String, ServoCommand> servoCommands() {
    return Collections.unmodifiableMap(new LinkedHashMap<>(servoCommands));
  }

  public synchronized double batteryVolts() {
    return batteryVolts;
  }

  public synchronized void setBatteryVolts(double volts) {
    batteryVolts = volts;
  }

  public synchronized double[] imuQuaternion() {
    return imuQuaternion.clone();
  }

  public synchronized double[] imuAngularVelocity() {
    return imuAngularVelocity.clone();
  }

  public synchronized void setImu(double[] quaternionWxyz, double[] angularVelocity) {
    imuQuaternion = quaternionWxyz.clone();
    imuAngularVelocity = angularVelocity.clone();
  }

  /** The latest values of one sensor (layout per device type), or null before the first TICK. */
  public synchronized double[] sensorInput(String name) {
    double[] v = sensorInputs.get(name);
    return v == null ? null : v.clone();
  }

  /** Deep copy of all sensor inputs (a bulk read snapshot of digital inputs). */
  public synchronized Map<String, double[]> copySensorInputs() {
    Map<String, double[]> copy = new LinkedHashMap<>();
    for (Map.Entry<String, double[]> e : sensorInputs.entrySet()) {
      copy.put(e.getKey(), e.getValue().clone());
    }
    return copy;
  }

  public synchronized void setSensorInput(String name, double[] values) {
    sensorInputs.put(name, values.clone());
  }

  /** Queues a write to a sensor for the next STATE. */
  public synchronized void sensorCommand(String name, String command, double... args) {
    sensorCommands.add(new SensorCommand(name, command, args.clone()));
  }

  public synchronized List<SensorCommand> drainSensorCommands() {
    List<SensorCommand> out = new ArrayList<>(sensorCommands);
    sensorCommands.clear();
    return out;
  }

  /** Number of TICK inputs received so far (a device's own writes show until the simulator reports them). */
  public synchronized long ticks() {
    return ticks;
  }

  public synchronized void countTick() {
    ticks++;
  }

  /** Simulation time of the latest inputs (for bulk-cache staleness and timestamps). */
  public synchronized long inputTimeNanos() {
    return inputTimeNanos;
  }

  public synchronized void setInputTime(long nanos) {
    inputTimeNanos = nanos;
  }

  /** Error/warning lines for the runner (never user data beyond error lines, spec 01 logging). */
  public synchronized void log(String line) {
    logLines.add(line);
  }

  public synchronized List<String> drainLog() {
    List<String> out = new ArrayList<>(logLines);
    logLines.clear();
    return out;
  }
}
