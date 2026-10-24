# 00 — Vision and Scope

## Vision
The FTC simulator teams actually trust: if a strategy, mechanism or OpMode
works here, it works on the real field. Realism in this order of priority:
1. **Code fidelity** — real FTC Java runs unchanged and behaves the same.
2. **Physics fidelity** — robots, motors, battery and pollen move like reality.
3. **Rules fidelity** — scoring, timing and fouls exactly as in the manual.
4. **Visual fidelity** — photoreal field, robots and arena.
When these conflict, the higher one wins.

## Users
- **Programmers**: write and debug TeleOp/Autonomous without the robot, tune
  paths, test vision, find bugs before competition.
- **Drivers**: practice driving and cycling with real gamepads, practice
  match strategy, driver handoff, endgame timing.
- **Designers/builders**: drop in a robot STEP, see if a mechanism concept
  works (turret, intake, launcher) before cutting metal.
- **Strategists/coaches**: test alliance strategies, measure cycle times,
  review replays.
- **New teams/students**: learn FTC programming with a default robot.

## Realism benchmarks (acceptance targets, measured against reality)
Values marked ASSUMED must be calibrated with real measurements; ask me to
record videos/measurements of our real robot when the time comes.
- goBILDA starter robot top speed and 0→top-speed time within ±10% of the
  real robot (I will measure).
- Strafe speed of mecanum lower than forward speed, with the ratio within
  ±10% of the real robot.
- Pollen dropped from 1 m bounces and rolls like real footage (ASSUMED values
  first, then calibrated from video).
- Example code from the goBILDA project runs without any edits and drives the
  robot with the same motor directions as on the real robot.
- Loop times reported by telemetry are in the same range as the real
  Control Hub for the same code (bulk reads vs individual reads matters).
- Battery voltage sags under heavy acceleration and drops over a match.
- Every scoring rule in `docs/game_rules.md` has at least one automated test.

## Performance budgets
- 60 fps at 1080p "High" on a mid-range GPU (RTX 3060 class) with 4 robots
  and all game elements on the field.
- Physics step (240 Hz) ≤ 2 ms average for a full 4-robot match.
- Sim ↔ JVM round trip ≤ 1 ms excluding simulated hardware latency.
- "Run on robot" from save to OpMode listed: ≤ 10 s incremental build.
- App start to main menu ≤ 10 s; field load ≤ 15 s.
- "Low" preset runs on integrated graphics at 720p/30 fps.

## In scope (full product)
Field with working mechanisms, full rules and scoring, realistic robots and
physics, FTC SDK shim, built-in editor with LSP and GitHub sync, Driver
Station replica, real gamepads, sensors and vision, replays, analytics,
graphics/audio polish, robot import, AI opponents, online 2v2.

## Out of scope (unless I say otherwise)
- Mobile, consoles, web version (maybe a web replay viewer later).
- Blocks programming (maybe later).
- Kotlin support (maybe later; Java first).
- Selling the app or using FIRST branding commercially.
- Physically simulating every screw. Simulate what affects behavior.

## Legal / credits
Non-commercial fan project. About screen credits FIRST, FIRST Tech Challenge,
BIOBUZZ (trademarks of their owners), goBILDA, and all open-source libraries
with their licenses (generate a `THIRD_PARTY_NOTICES.md`). Don't ship the
game manual or vendor CAD inside the app unless I confirm we may.
