# Changelog

## CAD pipeline: progress output, broken faces — 2026-10-26
- `tools/cad_pipeline/run.py` prints every stage (STEP read, mass properties, meshing, colliders, export) with counts,
  time left and the current part; colliders are cached the moment they finish and mass properties every minute, so an
  interrupted run resumes. (A 244 MB STEP printed nothing for many minutes and looked stuck.)
- Broken STEP faces meshed past their edges (a long bar out of the RoboDojo turret) are dropped and reported; cached
  meshes are checked once and only broken parts are meshed again. `max_part_extent` in the robot CAD configs reports
  any part drawn longer than 18 in + 1 in. Tests: `tests/python/test_cad_runaway_faces.py`.

## Phase 10 — Full match — 2026-10-26
- `sim/Sim.Match`: match setup (4 stations: robot code, gamepad, placeholder, empty; foul mode; automatic human player),
  start positions (`config/match.yaml`, G304-checked for every robot), match records with the official-style breakdown,
  fouls, scoring events and robot statistics, match history (JSON + CSV), replay files (D-034).
- Godot: `MatchSession` (spawns and places the robots, pre-loads, Driver Station flow, human player, Assist referee
  decisions, recording, results), per-station gamepads (`GamepadSeats`, Start+A/B to the focused station), Driver Station
  panel with a station picker, scoreboard with match control and G304 problems, results, history, match setup and foul
  review panels, basic replay viewer (`--replay`), user data in `%APPDATA%/BiobuzzSim`.
- Acceptance: `scripts/test_match.ps1` plays a full 2v2 MATCH headless (`--match-test`, 13 checks) and re-simulates its
  replay in a fresh process (`--verify-replay`): every checkpoint and the final hash match.
- Fixes: direct drive works again after the field re-enables the robot; G427 landing from contact reports.
- Tests: Sim.Match.Tests (17), GamepadSeatTests (1).

## RoboDojo fixes and tooling — 2026-10-26
- CAD pipeline: faces that do not mesh are counted, healed (ShapeFix) and reported; a part that still does not mesh is
  drawn from its mirrored twin, or as the hull of its B-rep edges; mirror-aware geometry signatures (products of
  inertia); `symmetry` config (pairing report, `mirror_unpaired` rules); pipeline version 2 (cache rebuilt).
- RoboDojo #23205: symmetric ramps and wheel guards, 30k-triangle detail, finer flower-ramp collider; flower bar floor
  stop (`floor_stop`, `Sim.Robots.FloorStop`); hood position 0 = starting pose, higher = hood up; TeleOp HOOD_START 0 and
  FLOWER_BAR_DOWN 1.0.
- App: a robot's own TeamCode jar is rebuilt when its sources are newer; workspace projects take template updates for
  files the team has not edited (`Sim.Editor.ProjectTemplate`).
- Tooling: scripts and tool lookups also run on Linux (pwsh); JDT LS needs Java 25.
- Tests: test_cad_symmetry.py (4), ArmAndHoodTests (7), ProjectTemplateTests (1), 2 team robot checks.

## Team robot RoboDojo #23205 — 2026-10-25
- CAD pipeline: plain STEP sources (`source: {path}`), zero-length normals rebuilt from triangles; config
  `config/cad/robodojo_23205.yaml` (wheels, intake, transfer stages, gate, flower bar, turret, shooter, hood).
- Robot model (D-032): mounted mechanisms, turret hard stops, follower stages, servo arms with CAD pose, geared servos,
  external encoders, guided path with feeder/gate/turret/hood/spring nip; Axon MAX/MINI servo models.
- `config/robots/robodojo_23205.yaml`, TeleOp `tests/compat/robodojo_23205` (built automatically for `--robot`).
- Tests: TurretRobotTests (6), test_team_robot.py (7), TeamRobotCodeTests (2), scenarios robodojo_pollen_turret_back,
  robodojo_nectar_forward; scenario driver sets any motor/servo.

## Phase 9 (in progress) — 2026-10-25
- Sensor models in `sim/Sim.Sensors` (config/sensors.yaml): Pinpoint firmware odometry from physical pods, OTOS optical
  tracking, REV colour/2m distance/touch; robot variants (`base:`), `gobilda_starter_sensors`.
- SDK shim: GoBildaPinpointDriver, SparkFunOTOS, RevColorSensorV3, Rev2mDistanceSensor, touch/digital, WebcamName,
  Android stubs; matrices (OpenGLMatrix, MatrixF), Euler angles for all conventions, Position, Pose3D.
- Vision: OpenCV 4.9 in the runner, VisionPortal, AprilTagProcessor (SDK 12 logic, clusters), AprilTag 3 detection in
  the simulator, camera frames over shared memory (protocol v3), Godot webcam rendering, tag decals on the hives.
- Tests: 23 sensor model tests, Java shim + AprilTag pipeline tests, SDK sensor and vision samples end to end.

## Phases 4-8 (in progress) — 2026-10-25
- Godot robot: rigid chassis on raycast wheel suspension driven by the shared RobotPowertrain; robot scenarios
  (settle, drive, intake pickup, launch); field surface = tile top.
- Guided element path (D-027) for intake, hopper, feeder and flywheel launch with physical RPM drop; compliant
  contacts for sweepers; kinematic windmill paddles; CAD config fixes (hood collider, front bar group).
- Hive hinges active in the app (hive tips with pollen); MatchController (WorldSnapshot → rules engine, field
  control), scoreboard, practice tools, Driver Station panel, gamepads (Start+A/B, keyboard fallback).
- Code editor (F4): JDT LS 1.62 integration (completion, diagnostics), templates, F5 build in ~2 s + runner
  restart; per-project build folder.
- Git (F6): git CLI client with safety confirmations, clone/init, device-flow sign-in, Credential Manager.


## Phase 6 (in progress) — 2026-10-25
- OpMode runner JVM (`java/runner`): protocol v1 (`docs/ipc_protocol.md`), OpMode discovery like the
  DS, one-time connection token, security-policy sandbox (9 JUnit tests with a probe jar).
- Simulator side (`sim/Sim.Runner`): runner launcher and client, Java 17 discovery, `RunnerBridge`
  (hub commands, inputs, IMU mounting, SDK Stop Robot behaviour); `RobotHardware` in `Sim.Robots`.
- Shim: deterministic start/stop (no team code outside tick windows), telemetry per SDK TelemetryImpl
  (dirty flush, clear-on-add, log), deadlock fix (telemetry monitor held across a park); 26 tests.
- End-to-end tests with the unchanged example code (15 tests): TeleOp powers and launcher PIDF,
  TeleOp vs direct drive, Autonomous, Stop Robot, determinism.
- `scripts/test_java.ps1`, Java tests in `test_unit.ps1`; `trivial_sdk_call` in hardware_latency.yaml.
- Earlier in Phase 6: FTC SDK 12.0 shim, build adapter, example Autonomous (commit dae3cd9).

## Phase 5 — 2026-10-24
- Robot YAML for the goBILDA StarterBot with evidence tests and an OpMode parser cross-check.
- Motor, battery, hub controller and servo models (`sim/Sim.Hardware`, 17 tests).
- Mecanum force model and flat-floor robot sim with benchmarks (`sim/Sim.Robots`, 8 tests).

## Phase 4 — 2026-10-24
- BIOBUZZ rules engine: match state machine, scoring, fouls (`sim/Sim.Game`, 57 tests).

## Phase 3 — 2026-10-24
- Field mechanisms derived from CAD (`config/field_mechanisms.yaml`, 9 tests).

## Phase 2 — 2026-10-24
- CAD pipeline (STEP → glTF, colliders, mass properties, checks) for the field and the robot;
  Godot field scene, cameras, HUD, screenshot and scenario runners; physics/hardware configs.

## Phase 1 — 2026-10-24
- `tools/manual/extract.py` (page text, rendered pages, damaged-text report) and
  `tools/manual/apriltag_ids.py` (decodes tag IDs 30–45 from the printable PDFs).
- `docs/game_rules.md` and `config/rules_biobuzz.yaml` + JSON schema; 9 new tests.
- Project `.venv` created; `cadquery-ocp` pinned to 7.9.3.1.1 (cadquery 2.8.0 requirement),
  `pupil-apriltags` added.

## Phase 0 — 2026-10-24
- Repository skeleton per `CLAUDE.md`, `.gitignore`, `.gitattributes`.
- Resource inventory tool with STEP scanner and SHA-256 manifest of the 14 original files.
- `docs/RESOURCES.md`, `SETUP.md`, `STATUS.md`, `ARCHITECTURE.md`, `DECISIONS.md`,
  `open_questions.md`, `CHANGELOG.md`, `game_rules.md` (empty until Phase 1).
- `scripts/check_tools.ps1`, `test_all.ps1`, `test_unit.ps1`; 11 pytest tests.
