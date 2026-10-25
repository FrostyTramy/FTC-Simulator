# Architecture (as built)

The target architecture is in `specs/01-architecture.md`. This file describes what exists.

## Repository
```
config/              YAML configs (every value {value, unit, source} or ASSUMED) + schemas/ + robots/ + cad/
docs/                project documents; ipc_protocol.md = simulator ↔ runner protocol
generated/           pipeline output, git-ignored (field/robot glTF, colliders, SDK jars for javap, tools)
godot/               Godot 4.7 .NET project (field scene, cameras, scenario runner)
java/                Gradle 8.14.3: ftc-sdk-shim/, runner/, build-adapter/
reports/             benchmark JSON, screenshots (git-ignored)
scripts/             PowerShell entry points (tests, tool checks, Godot helpers); lib/tools.ps1
sim/                 Godot-independent C# libraries (net8.0): Core, Game, Hardware, Robots, Runner
tests/               python/, dotnet/ (xUnit), scenarios/ (Godot), compat/ (unchanged example TeamCode)
tools/               inventory/, manual/, cad_pipeline/, field/, robot/ (Python)
workspace/           user code projects, git-ignored
```

## Data and tools (Python)
- **Inventory** (`tools/inventory/`): STEP scanner + SHA-256 manifest of the original resources.
- **Manual** (`tools/manual/`): page text, rendered pages, AprilTag ID decoding.
- **CAD pipeline** (`tools/cad_pipeline/run.py`, `config/cad/*.yaml`): STEP → glTF per group
  (static / rigid / element), per-part tessellation cache, colliders (sphere, box, hull, CoACD,
  ring sectors) in a process pool, render proxies, mass properties, validation checks.
- **Field / robot data** (`tools/field/derive_mechanisms.py`, `tools/robot/parse_opmode.py`):
  mechanism geometry from CAD; hardware names, directions and modes parsed from the example code
  (cross-checked against `config/robots/gobilda_starter.yaml` in tests).

## Simulation libraries (`sim/`, C#)
- **Sim.Core**: config quantities, repository paths, deterministic RNG, stable hashing, event log.
- **Sim.Game**: BIOBUZZ match state machine, scoring, fouls (pure logic, driven by events).
- **Sim.Hardware**: DC motor model + exact battery bus solve, battery (OCV, sag, brownout), REV hub
  motor firmware (`HubMotorController`: run modes, velocity PIDF, RUN_TO_POSITION, encoder window),
  hobby servos (pulse → angle/speed).
- **Sim.Robots**: robot YAML (`RobotDefinition`), mecanum force model (`MecanumDrivetrain`),
  `RobotHardware` (all motor/servo ports addressed "hub:port"; mechanism motors spin rigid shafts
  with YAML inertia), `PlanarRobotSim` (flat-floor robot for tests and benchmarks).
- **Sim.Runner**: starts the runner JVM (`RunnerSession`), protocol codec (`Wire`, `Protocol`),
  `RunnerBridge` (CONFIGURE device list, TICK inputs incl. IMU mounting, STATE commands → hub
  models, SDK Stop Robot behaviour when no OpMode runs).

## Java (`java/`)
- **ftc-sdk-shim**: the SDK 12.0 public API on virtual time. `SimRuntime` parks the OpMode thread at
  the end of each tick budget (lockstep); devices write raw commands into `DeviceBus`; `SimHub`
  emulates bulk caching; `OpModeDriver` runs OpMode/LinearOpMode lifecycles; `SimTelemetry` follows
  TelemetryImpl (throttle, dirty flush, clear-on-add).
- **runner**: `RunnerMain` (socket client, protocol, OpMode discovery via annotations), `Sandbox`
  (security policy: team-code jar may read files, write in its work dir, read properties; nothing else).
- **build-adapter**: compiles a TeamCode folder against the shim into `teamcode.jar`.

## Robot under code: one simulation step (240 Hz)
1. `RunnerBridge.Inputs()` + IMU reading (state after step k−1) go out in TICK k.
2. The runner lets the OpMode run until the step's end time (virtual; SDK calls cost
   `config/hardware_latency.yaml` time), then replies STATE with raw motor/servo commands.
3. The physics step k runs with the commands applied before (meanwhile, in the same order every run).
4. `RunnerBridge.Apply(STATE k)`: commands go to the hub firmware models and act from step k+1.
Same inputs → same trace, bit for bit (tested).

## Godot app (`godot/`)
Field scene from the generated glTF/colliders, hive hinge mechanism, free camera, HUD, screenshot
runner, quality presets, headless scenario runner (`tests/scenarios`). Jolt physics at 240 Hz.
