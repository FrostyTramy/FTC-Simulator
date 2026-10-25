# Status

**Mode:** sequential work by the lead (D-016; the parallel-agent mode of D-015 ended when it used up
the usage quota). Phases proceed back to back without approval stops (D-013); blockers go to
`docs/open_questions.md`.

## Phase overview
| Phase | State |
|---|---|
| 0 Inventory & setup | ✅ accepted |
| 1 Rules | ✅ done (`docs/game_rules.md`, `config/rules_biobuzz.yaml`) |
| 2 CAD pipeline | ✅ technically accepted — field 993 parts / 1.47 M triangles, 8/8 checks, ~50 s warm; robot 3/3 checks; field renders at 93–137 fps (Iris Xe, 720p Medium) |
| 3 Field mechanisms (design) | ✅ geometry from CAD (`config/field_mechanisms.yaml`); physics values `ASSUMED` (calibrate in Phase 11) |
| 4 Mechanisms + scoring | 🔧 rules engine complete (pure C#, 57 tests); Godot hive hinge exists, hive-tip scenarios not run yet |
| 5 goBILDA robot | 🔧 robot YAML + evidence tests, hardware models, drivetrain model + benchmarks done; Godot robot scene + gamepad direct drive missing |
| 6 Java runner + SDK shim + DS | 🔧 shim, build adapter, runner, IPC, sandbox, determinism done (see below); Driver Station panel missing |
| 7 Code editor | not started |
| 8 GitHub sync | not started (needs a GitHub OAuth App client ID from the user) |
| 9 Sensors & vision | not started |
| 10–17 | not started |

## Phase 6 progress
- `java/runner`: OpMode runner JVM (protocol v1, OpMode discovery like the DS, one-time token,
  security-policy sandbox, stuck-code detection, event-loop telemetry flush).
- `sim/Sim.Runner`: launcher + protocol client, Java 17 discovery, `RunnerBridge` (hub commands →
  firmware models, encoder/current inputs, IMU mounting, the SDK's Stop Robot behaviour).
- `sim/Sim.Robots/RobotHardware`: every motor and servo port of a robot on one battery bus.
- Acceptance evidence (tests/dotnet/Sim.Runner.Tests, 15 tests, ~7 s):
  - example TeleOp drives the robot like Phase 5 direct drive: code-path latency 2 steps
    (8.3 ms); per-phase pose difference ≤ 7 mm / 0.71° (`reports/benchmarks/teleop_vs_direct_drive.json`);
  - example Autonomous runs: launcher at speed and feeding within 2 s, 6 in forward, strafe right,
    ends by itself;
  - determinism: same seed → identical trace hash over a 6 s random-driver run; other seed differs.
- Shim unit tests 26, sandbox tests 9 (JUnit).
- Missing for Phase 6: Driver Station panel (UI + match timer logic).

## Tests (last full run)
Python (pytest) 82, Java (JUnit) 35, C#: Game 57, Hardware 17, Robots 8, Runner 15.

## Key facts
- Field CAD is Y-up in metres after import; red on −X, audience on +Z = Godot frame (no conversion).
- Field inside is 141.35 in in the CAD (manual: "approximately 144 in"; §9.1 makes the CAD official).
- Hive pivot 43.95 in above the tiles, hives tilt ±30° (60° between stable states).
- AprilTag IDs 30–45 (decoded from the printable tags).
- SDK facts come from the official 12.0.0 AARs (javap), e.g. Stop Robot default OpMode, telemetry
  dirty/clear-on-add semantics, int16 bulk-read velocities.

## Known problems
- Symantec Endpoint Protection holds each new Godot process for ~10 min (Q-001); Godot work is slow
  until an exclusion is added.
- Team threads created by OpMode code run outside the lockstep (not deterministic); raw
  `System.nanoTime()` in team code reads the wall clock. Documented limitation (D-018).
