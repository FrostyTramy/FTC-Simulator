# 01 — Architecture

## Processes
```
┌──────────────────────────── Godot app (C#) ─────────────────────────────┐
│ UI (menus, Driver Station, editor, scoreboard)                          │
│ SimCore: physics world, robots, field, game state, scoring, replay      │
│ Hardware layer: simulated hubs, motors, servos, sensors, cameras        │
│ RunnerHost: starts/stops JVM, socket server, shared-memory frames       │
│ ToolsHost: Gradle builds, JDT language server, git                      │
└───────────┬───────────────────────┬───────────────────────┬─────────────┘
            │ socket (binary)       │ stdio (LSP JSON-RPC)  │ process / lib
   ┌────────▼────────┐     ┌────────▼────────┐     ┌────────▼────────┐
   │ JVM runner      │     │ JDT LS (JVM)    │     │ Gradle / git    │
   │ SDK shim +      │     │ autocomplete,   │     │ build user code,│
   │ user OpModes    │     │ diagnostics     │     │ clone/pull/push │
   └─────────────────┘     └─────────────────┘     └─────────────────┘
```
- One JVM runner per robot whose code is running (each robot = its own
  "Control Hub"). Crash in user code must never crash Godot.
- The runner is restarted on every new build; the sim keeps running.

## Godot C# modules (namespaces)
- `Sim.Core` — fixed-step clock, world, deterministic RNG, event bus.
- `Sim.Physics` — Jolt setup, materials, custom force models (mecanum,
  motor, battery, flywheel, drag).
- `Sim.Field` — loads field scene + `field_mechanisms.yaml`, joints, triggers.
- `Sim.Game` — match state machine, scoring engine, fouls, rules config.
- `Sim.Robots` — loads `robots/*.yaml`, builds robot scenes, hub/port model.
- `Sim.Hardware` — device models (motor, servo, encoder, IMU, sensors,
  camera), latency model, fault injection.
- `Sim.Runner` — IPC server, protocol, JVM lifecycle, watchdog.
- `Sim.Editor` — code editor UI, LSP client, build + problems panel.
- `Sim.Git` — GitHub auth, git operations, diff/merge UI.
- `Sim.DriverStation` — DS UI, gamepad mapping, telemetry display.
- `Sim.Render` — cameras, post-processing, quality presets, replays.
- `Sim.Audio`, `Sim.Net` (later), `Sim.AI` (later), `Sim.Analytics`.
Keep dependencies one-directional (Core ← Physics ← Field/Robots ← Game ← UI).
No module reaches into another's internals; communicate through interfaces
and the event bus.

## Time model
- Physics: fixed 240 Hz step (`physics.yaml`, tunable). Rendering is
  decoupled and interpolated.
- Simulated time is the only clock for game logic and robot code. Never use
  wall-clock time in sim logic. `ElapsedTime` in the shim reads sim time.
- Sim can run at 1x (real time), paused, slow motion, or faster than real
  time (headless tests, AI training). Robot code must still see consistent
  sim time.
- **Robot loop timing is simulated**: each hardware call in the shim costs
  simulated time per `config/hardware_latency.yaml` (e.g. bulk read,
  individual motor read, I2C sensor read, servo write — values ASSUMED until
  measured). A loop that does 20 individual reads must get a realistically
  slow loop time, just like on a real Control Hub.

## Tick protocol (sim ↔ JVM)
Lockstep, sim-driven, so results are deterministic:
1. Sim advances physics until the robot's next loop deadline.
2. Sim sends `TICK{simTimeNs, gamepad1, gamepad2, deviceStates[], opmodeState}`.
3. JVM runs user code until it either finishes one `loop()` iteration, or (for
   LinearOpMode) blocks on the next hardware call that needs fresh data /
   calls `sleep()` / `idle()` / reaches the latency budget.
4. JVM sends `CMD{actuatorCommands[], telemetry?, consumedSimTimeNs, logs[]}`.
5. Sim applies commands, advances by `consumedSimTimeNs`, repeat.
- LinearOpMode user threads are driven cooperatively: the shim intercepts
  hardware calls, `sleep`, `idle`, `opModeIsActive` to yield to the sim.
- User-created threads: allowed but warned; document limitations.
- Watchdog: if the JVM doesn't answer within N ms of wall time in real-time
  mode, the sim shows the same kind of error the real DS shows (OpMode stuck
  in loop / failed to stop) and force-restarts the runner.

### Message format
- Length-prefixed binary frames, little-endian, versioned header
  `{magic, protocolVersion, msgType, seq, length}`.
- Schema defined once (e.g. FlatBuffers, or a hand-written spec in
  `docs/protocol.md` with generated C# + Java code). Decide and record in
  DECISIONS.md.
- Message types: HELLO/HELLO_ACK (versions, robot config hash),
  LOAD_OPMODES, OPMODE_LIST, INIT, START, STOP, TICK, CMD, TELEMETRY,
  LOG, EXCEPTION, GAMEPAD_EFFECT (rumble/LED), CAMERA_FRAME_READY, SHUTDOWN.
- Camera frames go through shared memory (ring buffer of N frames), only a
  frame index is sent on the socket.

## Determinism
- Fixed step, fixed iteration order (sort by stable IDs), seeded RNG per
  subsystem (noise, field randomization), no `Dictionary` iteration order
  dependencies, no wall-clock, no multithreaded physics nondeterminism (check
  Jolt determinism settings).
- Record: initial state + seed + all inputs (gamepads, JVM commands) per
  tick → a replay file. Replaying must reproduce the match exactly.
- A determinism test runs a recorded match twice and compares state hashes
  every second of sim time.

## Data and config
- All tunables in YAML under `config/`, loaded at start and hot-reloadable in
  dev mode. Validated against JSON Schemas in `config/schemas/`; invalid
  config = clear error with file and line.
- User data in `%APPDATA%/BiobuzzSim/` (settings, recent projects, replays,
  logs). Never write into the install folder.
- Replay files: compressed, versioned, include sim build hash.

## Logging and errors
- Structured logs (level, module, sim time, wall time) to rotating files.
- In-app log console (dev mode).
- Crash handler writes a report (logs + config hashes + last replay
  segment) and offers to open the folder.
- Never log tokens, emails, or file contents of user code beyond error lines.

## Extensibility
- New robot = new YAML + generated CAD, no code changes.
- New season later = new field STEP + rules config + mechanism YAML; keep
  BIOBUZZ-specific logic in a `Seasons/Biobuzz` folder, not spread around.
