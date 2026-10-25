# Simulator ↔ OpMode runner protocol (version 1)

The simulator (C#, `sim/Sim.Runner`) starts one JVM per OpMode session (`java/runner`, JDK 17) and
talks to it over a TCP socket on `127.0.0.1`. The simulator listens; the runner connects to the port
given on its command line and echoes the one-time `--token` from its command line in HELLO_ACK, so a
different local process cannot pose as the runner. Communication is **lockstep** (spec 01): the simulator sends one `TICK` per
physics step (240 Hz) and waits for the `STATE` reply before stepping again, so results never depend
on wall-clock timing (determinism).

## Framing
- Every message: `int32 length` (bytes that follow) + payload. All integers and floats **big-endian**.
- Payload starts with `uint8 type`.
- `string` = `int32 byteLength` + UTF-8 bytes; length −1 = null.
- `bool` = `uint8` 0/1; `double` = IEEE-754 binary64; `float` = binary32.

## Messages (simulator → runner, reply in brackets)
| Type | Name | Fields | Reply |
|---|---|---|---|
| 1 | HELLO | int32 protocolVersion | 101 HELLO_ACK: int32 version, string sdkVersion, string token |
| 2 | LIST_OPMODES | – | 102 OPMODES: int32 n × (string className, string name, string group, uint8 kind 0=TeleOp 1=Autonomous, string preselectTeleOp) |
| 3 | CONFIGURE | 6 × int64 costs (controlHubCommand, expansionHubCommand, i2cRead, loopOverhead, trivialCall, gamepadPeriod; ns), int32 hubCount, int32 n × device | 103 ACK |
| 4 | INIT | string className | 104 ACK or 199 ERROR(string) |
| 5 | START | – | 105 ACK |
| 6 | STOP | – | 106 ACK |
| 7 | TICK | see below | 107 STATE |
| 8 | QUIT | – | (runner exits) |

device = string name, string type (`motor`, `servo`, `crservo`, `imu`), int32 hub (0 = Control Hub,
1 = Expansion Hub), int32 port, string motorTypeName, double ticksPerRev, double gearing,
double maxRpm, double achievableFraction, string orientation (`cw`/`ccw`).

### TICK
int64 tick, int64 endNanos (virtual time at the end of this step), gamepad1, gamepad2,
double batteryVolts, double[4] imuQuaternion (w, x, y, z: IMU chip frame in a Z-up world),
double[3] imuAngularVelocity (rad/s about the chip axes), int32 n × motor input
(string key `hub:port`, int32 position, double velocity ticks/s, double currentAmps, bool busy).

gamepad = float left_stick_x, left_stick_y, right_stick_x, right_stick_y, left_trigger,
right_trigger, int32 buttons, int32 id. Button bits: 0 a/cross, 1 b/circle, 2 x/square,
3 y/triangle, 4 dpad_up, 5 dpad_down, 6 dpad_left, 7 dpad_right, 8 left_bumper, 9 right_bumper,
10 left_stick_button, 11 right_stick_button, 12 back/share, 13 start/options, 14 guide/ps,
15 touchpad. Values are already in SDK conventions (stick up = −1).

### STATE
int64 nowNanos (OpMode virtual time), uint8 state (0 INIT, 1 RUNNING, 2 STOPPING, 3 STOPPED,
4 CRASHED, 5 IDLE), string error (DS error text or null),
int32 n × motor command (string key, string mode, double power, bool velocitySet, double velocity,
int32 targetPosition, string zeroPower, int32 tolerance, bool enabled, int32 resetCount,
bool hasVelocityPidf, double[4] velocityPidf, double positionP (NaN = firmware default)),
int32 n × servo command (string key, double pulseUs (NaN = never set), bool pwmEnabled),
int64 telemetryTransmissions, int32 n × string telemetryLine, int32 n × string logLine.

Motor and servo values are **raw** (after the SDK applied directions); the hub firmware behaviour
(PIDF, RUN_TO_POSITION, PWM → servo motion) runs in the simulator (`sim/Sim.Hardware`).

## Timing
The runner lets the OpMode run until its virtual clock reaches `endNanos`; hardware calls advance the
clock by the costs from CONFIGURE (`config/hardware_latency.yaml`). Commands issued during step *k*
take effect in step *k+1*: the simulator sends TICK *k* with the inputs sampled at the end of step
*k−1*, steps the physics with the commands it already has, then applies the STATE reply. Gamepad data reaches the OpMode at most every `gamepadPeriod`
(Driver Station packet rate). If user code runs for 5 s of real time without any SDK call, the
OpMode is reported as stuck (CRASHED).

## Sandbox
The runner is a separate process. Before loading team code it installs a security manager
(JDK 17) that forbids starting processes, `System.exit` from user code, network connections other
than the simulator socket, and file writes outside the session's work directory. Errors are
reported as error lines only (spec 01: no user data in logs beyond error lines).
