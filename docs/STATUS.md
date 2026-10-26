# Status

**Mode:** sequential work by the lead (D-016); phases proceed back to back without approval stops (D-013). Blockers
and questions go to `docs/open_questions.md`.

## Phase overview
| Phase | State |
|---|---|
| 0 Inventory & setup | ✅ accepted |
| 1 Rules | ✅ done (`docs/game_rules.md`, `config/rules_biobuzz.yaml`) |
| 2 CAD pipeline | ✅ technically accepted (field 8/8 checks, robot 3/3; 93–137 fps field render) |
| 3 Field mechanisms (design) | ✅ geometry from CAD; physics values `ASSUMED` |
| 4 Mechanisms + scoring | 🔧 rules engine (57 tests); hive hinges live in the app (pollen in the upper cell tips the hive 60°, scenario); MatchController feeds the rules engine every tick; scoreboard; practice tools (drag elements, P/N spawn). Needs: your hands-on check (drop pollen into a hive with the mouse, watch the score) |
| 5 goBILDA robot | 🔧 robot drives in Godot (raycast suspension + shared powertrain; 1.82 m vs 1.85 m reference); intake/hopper/feeder/launcher via the guided path (D-027): capture, 4 slots, launch 6.7 m/s, flywheel RPM drop 14 % and recovery; direct drive = example TeleOp controls. Needs: gamepad test by you |
| 6 Java runner + SDK shim + DS | ✅ runner, IPC, sandbox, determinism, Driver Station logic + panel; example TeleOp = direct drive within 7 mm; Auto runs |
| 7 Code editor | 🔧 editor panel (F4): tree, tabs, highlighting, JDT LS completion + diagnostics (tested), templates, F5 build (2 s) + runner restart. Needs: the 1-minute hands-on acceptance |
| 8 GitHub sync | 🔧 git CLI client (status, diff, stage, commit, push, pull, branches, conflicts; safety warnings), git panel (F6), clone/init, device-flow sign-in + Credential Manager. Sign-in needs your OAuth client ID (Q-010) |
| 9 Sensors & vision | 🔧 Pinpoint, OTOS, colour, distance, touch models + SDK drivers (samples SensorGoBildaPinpoint, SensorSparkFunOTOS, SensorREV2mDistance, SensorColor run unchanged); webcams rendered in Godot (C920 intrinsics, auto exposure), AprilTag 3 detection, SDK 12 VisionPortal/AprilTagProcessor on OpenCV; tags on the CELL stickers. Acceptance: unchanged ConceptAprilTag reports cluster poses within 0.10-0.71 in of ground truth at 4 poses (`tools/checks/vision_acceptance.py`; BIOBUZZ clusters do not give field poses, Q-011). Open: Limelight, fault injection, DS camera preview, lens distortion |
| 13 (early) Team robot | 🔧 RoboDojo #23205 turret robot (D-032): CAD pipeline (1142 parts, 14 groups), robot YAML, TeleOp `RoboDojo TeleOp`; scenarios: POLLEN 5.45 m/s / 1.78 m high landing behind with the turret at its +180° stop, NECTAR 7.71 m/s / 3.23 m in front; runner tests: turret to 90° and back with the counter-rotating encoder, gate opens only at speed. Needs: team checks (docs/notes/robodojo_23205.md "Open points") |
| 10–17 | not started |

## How to run
- App: `Godot_v4.7.2-stable_mono_win64\Godot_v4.7.2-stable_mono_win64.exe --path godot` (`-- --project DIR` for a
  team repository, default `workspace/starter` = copy of the example TeamCode).
- Team robot: `-- --robot robodojo_23205` (code project `workspace/robodojo_23205`, built on first start). Controls in
  `RoboDojoTeleOp.java` (sticks drive, RT intake, LT out, hold RB shoot, B near/far, dpad L/R turret, Y turret front,
  dpad U/D hood, X flower bar).
- Sensor robot + OpMode check: `-- --robot gobilda_starter_sensors --robot-pose X,Z,YAW --opmode "Concept: AprilTag"`.
- Keys: F4 code editor, F5 build + run on the robot, F6 git, 1-4 camera views, RMB look, WASD/QE camera, LMB drag
  element, P/N spawn pollen/nectar; gamepad Start+A = gamepad1 (keyboard gamepad if none: arrows, Z/X, C/V, Space).
- Tests: `scripts/test_unit.ps1` (Python, Java, C#), `scripts/godot_test.ps1` (Godot scenarios).

## Tests (last runs)
Python (pytest) 98, Java (JUnit) 53, C#: Game 57, Hardware 17, Robots 24, Runner 26, Sensors 23, DriverStation 9,
Editor 16; Godot scenarios 10 (3 field, 1 hive, 4 starter robot, 2 team robot).

## Key facts
- Field CAD frame = Godot frame (X → blue, Y up, Z → audience); y = 0 is the tile top (`TileTopY`); `GroundY` is the
  venue floor under the ~15 mm tiles.
- Robot meshes come out of the pipeline Y-up; the robot undoes that before applying the YAML cad_to_robot.
- SDK facts come from the official 12.0.0 AARs (javap): Stop Robot default OpMode, telemetry semantics, int16 bulk
  velocities.

## Known problems
- Symantec holds new Godot processes for ~10 min after the project DLL changes (Q-001); runs right after another run
  start in seconds.
- Guided ball path (D-027) instead of a contact-only intake; launch direction and exit speed are ASSUMED until measured.
- Team threads created by OpMode code run outside the lockstep; raw `System.nanoTime()` in team code reads the wall
  clock (D-018).
