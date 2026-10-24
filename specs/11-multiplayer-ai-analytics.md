# 11 — Multiplayer, AI Opponents, Drills, Analytics (later phases)

## Local multiplayer
- Up to 4 robots on one PC with multiple gamepads (each robot has its own
  DS panel and JVM runner).
- Split-screen driver views optional; shared broadcast view by default.

## Online 2v2
- Server-authoritative: one host (player-hosted or dedicated) runs the
  physics and game logic. Clients send gamepad input + their robot's
  actuator commands (their robot code runs on their own machine in their own
  JVM, like a real robot owned by that team), server sends state snapshots.
- Client-side interpolation for other robots, prediction only for the local
  driver's own robot if latency is noticeable; correct from server state.
- Latency target: playable up to ~100 ms; show ping.
- Lobby: create/join with code, choose alliance/slot, choose robot (from
  library or own robot YAML + CAD; host validates robot rules), ready-up.
- Anti-cheat basics: server validates robot config limits (size, motor
  counts), actuator command ranges; replays saved by the server.
- NAT traversal: start with direct connect / port forward and a relay option
  later; decide transport (ENet/Godot high-level multiplayer vs custom UDP)
  in DECISIONS.md.

## AI opponents and partners
- Use the same robots and the same physics as players (no cheating physics).
- Brain: behavior tree / utility AI that plans cycles (intake → score →
  repeat), considers endgame tasks from the rules, avoids collisions, plays
  light defense if enabled. Driven through the same "gamepad/actuator"
  interface as humans.
- Difficulty levels change reaction time, accuracy, path quality and
  strategy, not physics.
- Option: AI runs real Java OpModes (an "AI team" written in FTC Java), so
  teams can scrim against sample autonomous code.
- Optional later: reinforcement learning training mode using faster-than-
  real-time headless sim.

## Practice drills
- Intake drills (pollen at walls/corners), launching accuracy, cycling
  against the clock, endgame timing, driver handoff, defense evasion.
- Leaderboards (local), personal bests, ghost of your best run.
- Autonomous drills: repeatability test (run auto N times with randomized
  start errors, report success rate and final pose error).

## Analytics
- Per match/robot: cycle times, elements controlled/scored per phase,
  launch accuracy, distance driven, time in each zone (heatmap), collisions,
  battery use, peak currents, loop time stats, fouls.
- Code analytics: loop time histogram, hardware calls per loop (bulk vs
  individual), exceptions, time spent in `sleep`.
- Pose estimation quality: estimated vs ground-truth pose error over time.
- Export CSV/JSON; built-in charts; compare two matches.
- Strategy board: top-down field to draw plans, saved with matches.

## Logging for teams
- `RobotLog`-style logs and user telemetry saved with each match so teams can
  debug after the fact, synced with replays.
