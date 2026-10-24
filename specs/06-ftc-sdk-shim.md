# 06 — FTC SDK Shim

## Goal
User code written for the real FTC SDK compiles and runs unchanged. Same
package names, class names, method signatures, constants, exceptions and
behaviors. Match the SDK version used by the goBILDA example project (check
its `build.dependencies.gradle` / dependency versions) and track it in
`docs/DECISIONS.md`. When the real SDK source is available (the
FtcRobotController repo includes TeamCode samples and public API docs),
use the public API documentation as the reference; never copy proprietary
code you don't have rights to.

## Packages and classes (minimum)
- `com.qualcomm.robotcore.eventloop.opmode`: `OpMode`, `LinearOpMode`,
  `TeleOp`, `Autonomous`, `Disabled`, `OpModeManager` hooks as needed.
- `com.qualcomm.robotcore.hardware`: `HardwareMap` (both
  `hardwareMap.get(Class, name)` and the typed device mappings like
  `hardwareMap.dcMotor.get(name)`), `DcMotor`, `DcMotorEx`, `DcMotorSimple`
  (Direction), `Servo`, `CRServo`, `ServoController`, `PwmControl`,
  `TouchSensor`, `DigitalChannel`, `AnalogInput`, `ColorSensor`,
  `NormalizedColorSensor`, `DistanceSensor`, `VoltageSensor`, `IMU`,
  `Gamepad`, `PIDFCoefficients`, `LynxModule` (bulk caching modes OFF, AUTO,
  MANUAL, `clearBulkCache`).
- `org.firstinspires.ftc.robotcore.external`: `Telemetry` (addData, addLine,
  update, clear, setAutoClear, setMsTransmissionInterval, log(),
  setDisplayFormat), navigation classes (`AngleUnit`, `DistanceUnit`,
  `Pose3D`, `YawPitchRollAngles`, `AngularVelocity`, `Position`), `ElapsedTime`.
- `com.qualcomm.hardware.rev`: `RevHubOrientationOnRobot`, color/distance
  sensor classes used by samples.
- `org.firstinspires.ftc.vision`: `VisionPortal`, `apriltag.AprilTagProcessor`,
  `AprilTagDetection`, `AprilTagLibrary` (season tag library), `VisionProcessor`
  interface for custom processors (OpenCV `Mat` via OpenCV Java).
- Third-party device drivers commonly used: goBILDA Pinpoint
  (`GoBildaPinpointDriver`), SparkFun OTOS, Limelight 3A (`Limelight3A`),
  REV color sensor V3, distance sensor 2m.
- Android stubs: `android.util.Log`, `Context`, `AppUtil` bits, etc. that
  user code commonly imports. Stubs log a warning if called in unsupported ways.

## Exact behaviors to replicate (and test)
OpMode lifecycle:
- `OpMode`: `init()` → `init_loop()` repeatedly → `start()` → `loop()`
  repeatedly → `stop()`. Telemetry auto-update behavior after each call as in
  the SDK.
- `LinearOpMode`: `runOpMode()` thread; `waitForStart()`, `opModeIsActive()`,
  `opModeInInit()`, `isStarted()`, `isStopRequested()`, `idle()`, `sleep()`.
  Stop request semantics and the forced stop if code doesn't exit in time,
  with the same style of error message the DS shows.
- `@Autonomous(preselectTeleOp = "...")` and the 30 s autonomous timer
  behavior in the Driver Station.
- Exceptions in user code stop the OpMode and show the error and stack
  trace like the DS ("User code threw an uncaught exception...").
Motors:
- Power clipped to [-1, 1]; direction reversal; `setZeroPowerBehavior`;
  `RUN_WITHOUT_ENCODER`, `RUN_USING_ENCODER` (velocity control),
  `RUN_TO_POSITION` (requires target set before mode, tolerance,
  `isBusy()`), `STOP_AND_RESET_ENCODER`; `setVelocity` (ticks/s and angle
  units), `getCurrent`, `isOverCurrent`, `setTargetPositionTolerance`,
  `getMotorType` / achievable max ticks per second.
- Motor controller PIDF defaults per SDK, settable via
  `setPIDFCoefficients` / `setVelocityPIDFCoefficients`.
Servos:
- Position 0..1 mapped to the PWM range; `scaleRange`, direction; `CRServo`
  power; disabling PWM.
IMU:
- `initialize(new IMU.Parameters(new RevHubOrientationOnRobot(...)))`,
  `getRobotYawPitchRollAngles()`, `getRobotAngularVelocity()`, `resetYaw()`.
  Orientation mapping must be correct for all logo/USB combinations; test
  every combination.
Gamepad:
- All fields (sticks with Y up = negative, triggers 0..1, buttons, dpad,
  bumpers, stick buttons, guide/back/start, PS aliases like cross/circle/
  square/triangle/share/options/touchpad), rumble APIs (`rumble`,
  `rumbleBlips`, `runRumbleEffect`, `stopRumble`, `isRumbling`), LED
  (`setLedColor`, `runLedEffect`), and any edge-detection helper methods present
  in the target SDK version.
Bulk caching:
- OFF: every read is a separate (slow) simulated transaction.
- AUTO: cache cleared when a value is read twice.
- MANUAL: cache only cleared by `clearBulkCache()`; stale data if the user
  forgets, exactly like real hardware.
Timing:
- Each hardware transaction consumes simulated time per
  `config/hardware_latency.yaml` (ASSUMED until measured on our hub).

## Compatibility test suite
- Compile and run the official FTC sample OpModes (from the external samples
  folder of the FtcRobotController repo, Basic*/Concept*/Robot*/Sensor*) in
  the sim; maintain a compatibility table (compiles / runs / behaves right).
- Libraries: Road Runner (1.0 actions + tuning OpModes), Pedro Pathing
  (follower + tuners), FTCLib/SolversLib basics, FTC Dashboard or Panels
  (see below). Each with a sample project in `tests/compat/`.
- The goBILDA example project: must compile and run with zero edits.

## FTC Dashboard / Panels emulation
Teams use FTC Dashboard or Panels for live graphs, field overlay and config
variables. Provide compatible APIs (`@Config` static fields, `TelemetryPacket`,
`fieldOverlay()`), served either inside the app (dashboard panel) or on
localhost so the real web UI works. Decide approach and record it.

## Packaging
- `ftc-sdk-shim.jar` + sources jar + javadoc for the editor's autocomplete.
- Version string reports "SIM" so user code can detect it if needed
  (but never required).
