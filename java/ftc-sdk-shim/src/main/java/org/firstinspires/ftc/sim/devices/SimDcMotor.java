package org.firstinspires.ftc.sim.devices;

import com.qualcomm.robotcore.exception.TargetPositionNotSetException;
import com.qualcomm.robotcore.hardware.DcMotorController;
import com.qualcomm.robotcore.hardware.DcMotorEx;
import com.qualcomm.robotcore.hardware.PIDCoefficients;
import com.qualcomm.robotcore.hardware.PIDFCoefficients;
import com.qualcomm.robotcore.hardware.configuration.typecontainers.MotorConfigurationType;
import org.firstinspires.ftc.robotcore.external.navigation.AngleUnit;
import org.firstinspires.ftc.robotcore.external.navigation.CurrentUnit;
import org.firstinspires.ftc.robotcore.external.navigation.Rotation;
import org.firstinspires.ftc.sim.DeviceBus;
import org.firstinspires.ftc.sim.SimHub;
import org.firstinspires.ftc.sim.SimRuntime;

/**
 * A motor on a REV hub port, with the SDK's DcMotorImplEx semantics: direction handling through the motor
 * type's orientation, power clipping, mode rules, LastKnown write caching and bulk-cached reads. The
 * hub firmware itself (PIDF, RUN_TO_POSITION) runs in the simulator (sim/Sim.Hardware).
 */
public final class SimDcMotor implements DcMotorEx {
  /** Firmware defaults (config/motors.yaml hub_motor_controller, ASSUMED for goBILDA motors). */
  public static final PIDFCoefficients DEFAULT_VELOCITY_PIDF = new PIDFCoefficients(10, 3, 0, 0);
  public static final PIDFCoefficients DEFAULT_POSITION_PIDF = new PIDFCoefficients(10, 0.05, 0, 0);

  private final SimHub hub;
  private final int port;
  private final String key;
  private final DcMotorController controller;
  private MotorConfigurationType motorType;
  private Direction direction = Direction.FORWARD;
  private double power;
  private RunMode mode = RunMode.RUN_WITHOUT_ENCODER;
  private ZeroPowerBehavior zeroPowerBehavior = ZeroPowerBehavior.FLOAT; // hub floats its outputs at init
  private boolean targetPositionSet;
  private int targetPosition;
  private PIDFCoefficients velocityPidf = new PIDFCoefficients(DEFAULT_VELOCITY_PIDF);
  private PIDFCoefficients positionPidf = new PIDFCoefficients(DEFAULT_POSITION_PIDF);
  private double currentAlertAmps;

  public SimDcMotor(SimHub hub, int port, MotorConfigurationType motorType, DcMotorController controller) {
    this.hub = hub;
    this.port = port;
    this.key = hub.index() + ":" + port;
    this.motorType = motorType;
    this.controller = controller;
    command(); // creates the port's command record
  }

  private DeviceBus.MotorCommand command() {
    return SimRuntime.get().bus().motorCommand(key);
  }

  /** The direction the hardware sees: FORWARD/REVERSE flipped for clockwise motor types (SDK DcMotorImpl). */
  private Direction operationalDirection() {
    return motorType.getOrientation() == Rotation.CCW ? direction : direction.inverted();
  }

  private double adjust(double value) {
    return operationalDirection() == Direction.REVERSE ? -value : value;
  }

  private int adjust(int value) {
    return operationalDirection() == Direction.REVERSE ? -value : value;
  }

  // ------------------------------------------------------------------ DcMotorSimple

  @Override
  public void setDirection(Direction direction) {
    this.direction = direction;
  }

  @Override
  public Direction getDirection() {
    return direction;
  }

  @Override
  public void setPower(double power) {
    double clipped = Math.max(-1, Math.min(1, power));
    if (mode == RunMode.STOP_AND_RESET_ENCODER) {
      return; // the hub ignores power while resetting
    }
    this.power = clipped;
    DeviceBus.MotorCommand c = command();
    double raw = adjust(clipped);
    if (c.power != raw || c.velocitySet) {
      c.power = raw;
      c.velocitySet = false;
      hub.command();
    }
  }

  @Override
  public double getPower() {
    return power;
  }

  // ------------------------------------------------------------------ DcMotor

  @Override
  public MotorConfigurationType getMotorType() {
    return motorType;
  }

  @Override
  public void setMotorType(MotorConfigurationType motorType) {
    this.motorType = motorType;
  }

  @Override
  public DcMotorController getController() {
    return controller;
  }

  @Override
  public int getPortNumber() {
    return port;
  }

  @Override
  public void setZeroPowerBehavior(ZeroPowerBehavior zeroPowerBehavior) {
    if (zeroPowerBehavior == ZeroPowerBehavior.UNKNOWN) {
      throw new IllegalArgumentException("zeroPowerBehavior may not be UNKNOWN");
    }
    if (this.zeroPowerBehavior != zeroPowerBehavior) {
      this.zeroPowerBehavior = zeroPowerBehavior;
      command().zeroPower = zeroPowerBehavior.name();
      hub.command();
    }
  }

  @Override
  public ZeroPowerBehavior getZeroPowerBehavior() {
    return zeroPowerBehavior;
  }

  @Override
  @Deprecated
  public void setPowerFloat() {
    setZeroPowerBehavior(ZeroPowerBehavior.FLOAT);
    setPower(0);
  }

  @Override
  public boolean getPowerFloat() {
    return zeroPowerBehavior == ZeroPowerBehavior.FLOAT && power == 0;
  }

  @Override
  public void setTargetPosition(int position) {
    targetPositionSet = true;
    targetPosition = position;
    int raw = adjust(position);
    DeviceBus.MotorCommand c = command();
    if (c.targetPosition != raw) {
      c.targetPosition = raw;
      hub.command();
    }
  }

  @Override
  public int getTargetPosition() {
    return targetPosition;
  }

  @Override
  public boolean isBusy() {
    return mode == RunMode.RUN_TO_POSITION && hub.read("p" + port + ".busy", m -> input(m).busy);
  }

  @Override
  public int getCurrentPosition() {
    return adjust((int) hub.read("p" + port + ".position", m -> input(m).position));
  }

  private DeviceBus.MotorInput input(java.util.Map<String, DeviceBus.MotorInput> inputs) {
    DeviceBus.MotorInput in = inputs.get(key);
    return in != null ? in : new DeviceBus.MotorInput();
  }

  @Override
  public void setMode(RunMode mode) {
    RunMode m = mode.migrate();
    if (m == RunMode.RUN_TO_POSITION && !targetPositionSet) {
      throw new TargetPositionNotSetException();
    }
    DeviceBus.MotorCommand c = command();
    if (m == RunMode.STOP_AND_RESET_ENCODER) {
      c.resetCount++;
      c.power = 0;
      power = 0;
    }
    if (this.mode != m || m == RunMode.STOP_AND_RESET_ENCODER) {
      this.mode = m;
      c.mode = m.name();
      hub.command();
    }
  }

  @Override
  public RunMode getMode() {
    return mode;
  }

  // ------------------------------------------------------------------ DcMotorEx

  @Override
  public void setMotorEnable() {
    command().enabled = true;
    hub.command();
  }

  @Override
  public void setMotorDisable() {
    command().enabled = false;
    hub.command();
  }

  @Override
  public boolean isMotorEnabled() {
    return command().enabled;
  }

  @Override
  public void setVelocity(double angularRate) {
    DeviceBus.MotorCommand c = command();
    double raw = adjust(angularRate);
    double maxTps = motorType.getAchieveableMaxTicksPerSecond();
    power = Math.max(-1, Math.min(1, angularRate / maxTps));
    if (!c.velocitySet || c.velocity != raw) {
      c.velocitySet = true;
      c.velocity = raw;
      c.power = Math.max(-1, Math.min(1, raw / maxTps));
      hub.command();
    }
  }

  @Override
  public void setVelocity(double angularRate, AngleUnit unit) {
    double revPerSecond = unit == AngleUnit.RADIANS ? angularRate / (2 * Math.PI) : angularRate / 360.0;
    setVelocity(revPerSecond * motorType.getTicksPerRev());
  }

  @Override
  public double getVelocity() {
    return adjust((double) hub.read("p" + port + ".velocity", m -> input(m).velocity));
  }

  @Override
  public double getVelocity(AngleUnit unit) {
    double revPerSecond = getVelocity() / motorType.getTicksPerRev();
    return unit == AngleUnit.RADIANS ? revPerSecond * 2 * Math.PI : revPerSecond * 360.0;
  }

  @Override
  @Deprecated
  public void setPIDCoefficients(RunMode mode, PIDCoefficients pidCoefficients) {
    setPIDFCoefficients(mode, new PIDFCoefficients(pidCoefficients));
  }

  @Override
  public void setPIDFCoefficients(RunMode mode, PIDFCoefficients pidf) throws UnsupportedOperationException {
    RunMode m = mode.migrate();
    DeviceBus.MotorCommand c = command();
    if (m == RunMode.RUN_USING_ENCODER) {
      velocityPidf = new PIDFCoefficients(pidf);
      c.velocityPidf = new double[] {pidf.p, pidf.i, pidf.d, pidf.f};
    } else if (m == RunMode.RUN_TO_POSITION) {
      positionPidf = new PIDFCoefficients(pidf);
      c.positionP = pidf.p;
    } else {
      throw new UnsupportedOperationException(String.format("setPIDFCoefficients(%s): only RUN_USING_ENCODER and RUN_TO_POSITION use PIDF", m));
    }
    hub.command();
  }

  @Override
  public void setVelocityPIDFCoefficients(double p, double i, double d, double f) {
    setPIDFCoefficients(RunMode.RUN_USING_ENCODER, new PIDFCoefficients(p, i, d, f));
  }

  @Override
  public void setPositionPIDFCoefficients(double p) {
    setPIDFCoefficients(RunMode.RUN_TO_POSITION, new PIDFCoefficients(p, 0, 0, 0));
  }

  @Override
  @Deprecated
  public PIDCoefficients getPIDCoefficients(RunMode mode) {
    PIDFCoefficients f = getPIDFCoefficients(mode);
    return new PIDCoefficients(f.p, f.i, f.d);
  }

  @Override
  public PIDFCoefficients getPIDFCoefficients(RunMode mode) {
    hub.command();
    return new PIDFCoefficients(mode.migrate() == RunMode.RUN_TO_POSITION ? positionPidf : velocityPidf);
  }

  @Override
  public void setTargetPositionTolerance(int tolerance) {
    command().tolerance = tolerance;
    hub.command();
  }

  @Override
  public int getTargetPositionTolerance() {
    return command().tolerance;
  }

  @Override
  public double getCurrent(CurrentUnit unit) {
    hub.command(); // motor current is not part of the bulk data
    double amps = SimRuntime.get().bus().copyMotorInputs().getOrDefault(key, new DeviceBus.MotorInput()).currentAmps;
    return unit.convert(Math.abs(amps), CurrentUnit.AMPS);
  }

  @Override
  public double getCurrentAlert(CurrentUnit unit) {
    return unit.convert(currentAlertAmps, CurrentUnit.AMPS);
  }

  @Override
  public void setCurrentAlert(double current, CurrentUnit unit) {
    currentAlertAmps = unit.toAmps(current);
    hub.command();
  }

  @Override
  public boolean isOverCurrent() {
    return currentAlertAmps > 0 && getCurrent(CurrentUnit.AMPS) > currentAlertAmps;
  }

  // ------------------------------------------------------------------ HardwareDevice

  @Override
  public Manufacturer getManufacturer() {
    return Manufacturer.Lynx;
  }

  @Override
  public String getDeviceName() {
    return "DC Motor";
  }

  @Override
  public String getConnectionInfo() {
    return controller.getConnectionInfo() + "; port " + port;
  }

  @Override
  public int getVersion() {
    return 1;
  }

  @Override
  public void resetDeviceConfigurationForOpMode() {
    direction = Direction.FORWARD;
  }

  @Override
  public void close() {
    setPower(0);
  }
}
