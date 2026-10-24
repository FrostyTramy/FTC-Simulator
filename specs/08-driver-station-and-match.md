# 08 — Driver Station, Gamepads, Match Control

## Driver Station replica
Looks and behaves like the real FTC Driver Station app, as a panel/overlay:
- Robot selector (which simulated robot/hub this DS controls).
- OpMode selector split into TeleOp and Autonomous lists, sorted/grouped the
  way the real DS does; `@Disabled` hidden.
- INIT → (init telemetry) → PLAY (▶) → STOP (■); buttons enabled/disabled
  exactly like the real DS states.
- Autonomous 30 s timer toggle; auto-stop at 0; `preselectTeleOp` loads the
  TeleOp afterwards.
- Telemetry area (respecting transmission interval, display format incl.
  HTML if supported), log lines, error/warning banner with the same wording
  style as the real DS.
- Robot battery voltage, hub status, ping/loop time, gamepad indicators
  (which physical controller is gamepad1/gamepad2, with the user-assigned
  icons).
- Restart Robot (restarts runner), Camera stream preview (when the OpMode
  uses a camera).

## Gamepads
- Input via Godot (SDL under the hood). Supported: Logitech F310 (XInput mode),
  PS4/PS5 (DualShock/DualSense), Xbox controllers, generic SDL controllers.
- Assignment like the real DS: press Start+A → gamepad1, Start+B → gamepad2
  (with PS: Options+Cross / Options+Circle). Show the assignment in the UI.
- Mapping to FTC `Gamepad` fields exactly (left_stick_y up = negative, etc.).
  Configurable deadzone defaults matching the SDK.
- Rumble and LED effects forwarded to the physical controller when supported.
- Keyboard/mouse fallback ("virtual gamepad") for people without controllers,
  clearly marked as non-realistic.
- Per-controller calibration and test screen (shows live values).
- Multiple robots: up to 4 DS instances (one per robot), each with its own
  gamepads; split-screen or one PC per alliance (local), online later.

## Match control (like the field control system)
- Match setup: choose robots for Red 1/2 and Blue 1/2 (player, AI, or
  "empty"), their starting positions (from manual), their code/OpMode, and the
  battery state.
- Phases and timings exactly from `rules_biobuzz.yaml`; field enables and
  disables robots like the real system; audio cues (start, end of auto,
  teleop start, endgame warning, end) with original-style but self-made
  sounds (no copyrighted audio).
- Driver pickup: between auto and teleop, the transition period and the
  "pick up controllers" moment are respected.
- Pause/resume for practice only; competition mode disables pause.
- Practice tools: reset field, place robot (drag + rotate), spawn/remove
  elements, slow motion, toggle timer.

## Scoreboard and results
- Live audience-style scoreboard: alliance scores, timer, phase, key game
  counters (e.g. hives tipped, Nectar remaining) as defined by the manual.
- Post-match screen: score breakdown identical in structure to the official
  breakdown, fouls with replay links, robot stats (distance driven, cycles,
  elements scored, shot accuracy, battery used, max current).
- Match history saved locally, with replay files.
- Export results as CSV/JSON.

## Referee tools
- Foul review list (from the field spec), confirm/reject in Assist mode.
- Manual adjustments with audit log (who/what/why), for practice scrims.
