# 13 — Phases

Do ONE phase at a time, only after I say "start phase N". Each phase: plan →
my OK → build → test → report. A phase is finished only when every
acceptance criterion is met or I explicitly accept a gap.

## Phase 0 — Inventory and setup
- Inventory every file in `D:\Code\FTC-Simulator` → `docs/RESOURCES.md`
  (what it is, size, how it will be used, quality issues, what's missing).
- Check tools: Godot 4 .NET, .NET SDK, JDK 17, Python 3 (+ pip packages),
  Git; write install instructions for anything missing (I install).
- Create repo skeleton, `.gitignore`, empty docs files, scripts folder.
- Ask me all questions you have so far.
**Accept**: RESOURCES.md complete, tools OK, skeleton committed.

## Phase 1 — Rules
- Extract manual text (keep page numbers) → `docs/manual_text/`.
- `docs/game_rules.md`: field description, all game elements with
  dimensions, match timing, every scoring rule, every foul relevant to robots,
  robot construction limits that the sim must check, all with § references.
- `config/rules_biobuzz.yaml`: every number (points, thresholds, timings,
  limits) with § refs.
- `docs/open_questions.md`.
**Accept**: I review and approve game_rules.md.

## Phase 2 — CAD pipeline
- Implement the pipeline (spec 02) for field and robot STEP files.
- Godot project created; field scene imported with materials (basic) and
  colliders; free camera to look around.
- Validation report with screenshots, triangle/draw-call numbers,
  `detected.yaml` for field and robot.
**Accept**: field looks correct and to scale, measured dimensions match the
manual, pipeline re-run is stable.

## Phase 3 — Field mechanisms (design)
- `config/field_mechanisms.yaml`, `config/materials.yaml`, `physics.yaml`
  (first version), debug overlay.
- Summary table of all ASSUMED values and questions.
**Accept**: STOP. I review and approve the YAML.

## Phase 4 — Field mechanisms (implementation) + scoring
- Joints, triggers, events, scoring engine, game state machine (without
  robots), practice mode tools (spawn/drag elements with mouse).
- Scenario tests for every scoring rule; hive behavior test with real
  physics drops.
**Accept**: all scoring tests pass; I can drop pollen into a hive with the
mouse and see it behave and score correctly.

## Phase 5 — goBILDA starter robot
- `config/robots/gobilda_starter.yaml` from STEP + instructions + example
  code (with source refs). STOP for review.
- Then: robot scene, drivetrain force model, motor/battery models,
  mechanisms, gamepad driving through a temporary C# "direct drive" mode.
- Physics benchmarks (speed, acceleration, strafe ratio) reported.
**Accept**: robot drives and its mechanisms work with a gamepad; numbers
plausible (calibration later).

## Phase 6 — Java runner + SDK shim + Driver Station
- IPC protocol, JVM runner, SDK shim (core devices, OpModes, telemetry,
  gamepads, IMU, bulk caching, timing model), DS panel.
- Build adapter compiles the goBILDA example project unchanged.
**Accept**: example TeleOp drives the robot identically to Phase 5 direct
drive; example Autonomous runs; shim unit tests pass; determinism test passes.

## Phase 7 — Code editor
- Editor UI, JDT LS integration, templates, build + run on robot, problems
  panel, split layout.
**Accept**: create a new TeleOp from template, get autocomplete, introduce an
error (see it), fix it, F5, drive with it — all under ~1 minute.

## Phase 8 — GitHub sync
- Device-flow sign-in, clone, status, diff, commit, push, pull, branches,
  conflict UI, safety checks.
**Accept**: clone our team repo, edit, run in sim, commit and push; the same
repo still builds in Android Studio.

## Phase 9 — Sensors and vision
- All sensor models, robot cameras, AprilTag detection, Pinpoint/OTOS,
  Limelight (if needed), fault injection menu.
**Accept**: FTC AprilTag sample localizes the robot within agreed error;
Pinpoint sample runs.

## Phase 10 — Full match
- Full match flow, field control, fouls (strict/assist), scoreboard,
  results, match history, replays (basic).
**Accept**: a full 2v2 match (with 4 local robots or placeholders) plays
start to finish with correct scoring and a replay that reproduces it.

## Phase 11 — Calibration
- Calibration OpModes, I measure the real robot, fitting tools, update
  physics values, `docs/calibration.md`.
**Accept**: benchmarks within the tolerances in spec 00.

## Phase 12 — Graphics and audio polish
- Lighting bake, arena, materials, cameras, photo mode, audio system,
  quality presets, UI polish, onboarding tutorial.
**Accept**: 60 fps High on target GPU; I approve the look.

## Phase 13 — More robots + robot import wizard
- Basic chassis, our turret robot (I provide CAD), import wizard.

## Phase 14 — Compatibility
- Road Runner, Pedro Pathing, FTCLib/SolversLib, Dashboard/Panels, FTC
  samples compatibility table.

## Phase 15 — AI opponents, drills, analytics

## Phase 16 — Online multiplayer

## Phase 17 — Release
- Installer (or portable zip), first-run checks (JDK bundled or detected),
  crash reporting (local), THIRD_PARTY_NOTICES, user guide.
