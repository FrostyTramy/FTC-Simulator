# Decisions

Why we chose X over Y. Newest last. Format: ID, date, decision, why, alternatives.

## D-001 — Original resources stay out of git (2026-10-24)
- **Decision:** the manual, CAD, guides and Godot download are git-ignored. Their SHA-256 and
  size are committed in `tools/inventory/resources_manifest.json`; `inventory.py` flags changes.
- **Why:** they are third-party material we may not redistribute, they are ~125 MB (419 MB STEP
  unzipped) and the repo should be safe to push to GitHub. User left the choice to us.
- **Alternatives:** commit them (licensing risk, large repo); Git LFS (spec 12: ask first, and
  it does not fix licensing).

## D-002 — Inventory tool uses only the standard library (2026-10-24)
- **Decision:** `tools/inventory/` reads STEP files as text (Part 21) instead of using OCP.
- **Why:** it must run before the CAD environment exists, and only needs names, counts and
  units. The 420 MB robot STEP scans in ~6 s without loading geometry.
- **Alternatives:** cadquery/OCP (slow to load 420 MB, heavy dependency for Phase 0).

## D-003 — Python environment pinned in one requirements file (2026-10-24)
- **Decision:** `tools/cad_pipeline/requirements.txt` with exact versions, installed into `.venv`.
- **Why:** pipeline output must be reproducible (stable IDs, determinism).

## D-004 — FTC SDK target: latest official FtcRobotController (2026-10-24)
- **Decision:** the shim targets the latest release of
  https://github.com/FIRST-Tech-Challenge/FtcRobotController (v12.0, 2026-09-12, at time of
  writing); re-checked when Phase 6 starts.
- **Why:** the example project has no Gradle files, so spec 06's "match the example" rule cannot
  be applied. User approved.

## D-005 — We write the example Autonomous (2026-10-24)
- **Decision:** a simple Autonomous for the StarterBot is written in Phase 6 to satisfy the
  "example Autonomous runs" acceptance criterion. goBILDA ships only a TeleOp. User approved.

## D-006 — Hub pose and ports for starter robots are assumed (2026-10-24)
- **Decision:** Control Hub position/orientation and the port map are chosen plausibly and marked
  `ASSUMED` in the robot YAML. User: "put them somewhere random but ok for the basic robots".

## D-007 — Performance target on the dev laptop: Low/Medium ≈ 30 fps (2026-10-24)
- **Decision:** develop and measure against Intel Iris Xe (i7-13700H) at about 30 fps. The field
  may be simplified visually but keeps the exact CAD dimensions; detail is spent only where it
  matters (game elements, Hives, robot). The 60 fps High target (Phase 12) needs a test on an
  RTX 3060-class PC later.
- **Why:** no discrete GPU is available; user asked for this.

## D-008 — Manual text is generated, not committed (2026-10-24)
- **Decision:** `docs/manual_text/` (spec 13 Phase 1) is produced by `tools/manual/extract.py`
  and git-ignored. Rendered pages go to `generated/manual_pages/`.
- **Why:** it is the full third-party manual; keeps the repo publishable (D-001). Regenerates in
  seconds. User delegated the choice.

## D-009 — Numbers are verified against page images (2026-10-24)
- **Decision:** the manual's text layer drops some glyphs (digit 3, letters R/S in places), so
  `extract.py` flags suspicious lines and every number in `game_rules.md` was read from the
  rendered page. AprilTag IDs are decoded from the printable PDFs with `pupil-apriltags`.
- **Alternatives:** OCR (extra dependency, still needs checking), trusting the text (wrong).

## D-010 — Ranking-point thresholds default to "All Other Events" (2026-10-24)
- **Decision:** SWARM 16 points, POLLINATOR 4 and 7 TIPS; Regional/Championship values are
  "TBA" in TU06 and selectable once published.

## D-011 — HIVE tipping is physical (2026-10-24)
- **Decision:** the manual defines no element count for a TIP (§9.6 p69: bi-stable, tips when
  enough elements are launched). The HIVE is simulated as a pivot with real masses and dampers;
  the TIP event (§10.5.1) only drives scoring and NECTAR unlocks.

## D-012 — Python environment pinned to cadquery's OCP (2026-10-24)
- **Decision:** `cadquery==2.8.0` requires `cadquery-ocp==7.9.3.1.1` (not the newest 8.0), so the
  pin follows cadquery. The project `.venv` was created by the assistant (user delegated).

## D-013 — Work continues across phases without per-phase approval stops (2026-10-24)
- **Decision:** at the user's request ("finish your whole job ... only if needed max priority then
  stop and ask me"), phases proceed back to back; review STOPs in spec 13 (Phase 1, 3, 5) are
  replaced by documented self-review, with all ASSUMED values listed for later review.

## D-014 — Pure C# simulation libraries live in `sim/` (2026-10-24)
- **Decision:** Godot-independent C# code (rules/scoring, hardware models, IPC client) is in class
  libraries under `sim/` (`Sim.Core`, `Sim.Game`, `Sim.Hardware`, `Sim.Runner`, net8.0 like
  GodotSharp 4.7) with xUnit tests under `tests/dotnet/`. The Godot project in `godot/` references
  them. Not listed in the CLAUDE.md layout; added because spec 12 wants unit tests without Godot.
- **Alternatives:** everything inside the Godot project (needs Godot to run unit tests; slower CI).

## D-015 — Parallel agents with strict folder ownership (2026-10-24)
- **Decision:** at the user's request, work runs in parallel agents, each owning disjoint folders
  (Godot app; game logic; hardware models; Java side; robot/physics data; CAD pipeline). Agents do
  not commit or edit shared docs; they write `docs/notes/<area>.md`, the lead integrates and commits.
- **Frame:** all code uses the CAD/Godot frame (X → blue, Y up, Z → audience); `game_rules.md` §1 fixed.

## D-016 — Back to sequential work (2026-10-25)
- **Decision:** the parallel agents of D-015 used up the usage quota within minutes, so the user asked
  to work "1 by 1" again. One lead works through the phases; D-015's folder split stays as the module map.

## D-017 — Runner sandbox = Java security policy for the team-code jar (2026-10-25)
- **Decision:** the runner installs a `Policy` + the standard `SecurityManager` (JDK 17). Only classes
  whose code source is the team's jar are restricted: read files, write/delete in the session work
  directory, read system properties, reflect on declared members. Processes, sockets, System.exit,
  native libraries, class loaders, environment variables, private access and changing the security
  setup are denied whenever team code is on the stack. The simulator verifies the runner with a
  one-time token echoed in HELLO_ACK.
- **Alternatives:** a custom SecurityManager subclass (first version: ignores doPrivileged, cannot tell
  team code from JDK code, and its own fields could be changed by reflection); OS-level sandbox (Windows
  AppContainer/job objects: much more code, later if needed). The SecurityManager is removed after JDK
  17, so the runner is pinned to Java 17 (like FTC itself).

## D-018 — Lockstep details: no team code outside a tick window (2026-10-25)
- **Decision:** the OpMode thread gets no time until the first TICK (static initializers included), a
  STOP's interrupt is delivered when the thread next resumes (never while parked), and a park is never
  cut short by an interrupt. The runner only touches shim state while the OpMode thread is parked and
  never waits on a lock the OpMode thread can hold (telemetry snapshots are published lock-free; value
  producers run outside the telemetry monitor). SDK calls from other threads do not move the OpMode clock.
- **Why:** the first integration run deadlocked (telemetry monitor held across a park) and the OpMode
  could run ahead of the simulator between INIT and the first TICK; both broke determinism.
- **Limitation:** threads created by team code run freely (not deterministic); `System.nanoTime()` in
  team code reads the wall clock.

## D-019 — SDK behaviour taken from the 12.0.0 bytecode (2026-10-25)
- **Decision:** where docs are silent, the shim and bridge follow the official AARs (javap):
  Stop Robot default OpMode (setPower(0) at once, hub failsafe every 100 ms), TelemetryImpl
  (throttled update marks dirty, flushed by the event loop; auto-clear at the next addData; log lines
  transmit immediately when allowed), REV bulk data (int32 encoders, int16 velocities).
- **Assumed:** what the hub firmware does on failsafe (outputs off → motors float, servos limp) — Q-008.

## D-020 — Hub firmware emulation for every port on the C# side (2026-10-25)
- **Decision:** `RobotHardware` owns a firmware model for every motor and servo port of the robot YAML;
  all motors share one battery bus. Motors that drive a mechanism spin a rigid shaft with the YAML
  inertia × speed_ratio² + rotor inertia (substepped) until mechanisms become Godot joints.
- **Why:** the launcher's velocity PIDF, the encoder velocity the code checks and the battery sag all
  need the real load; the Phase 6 tests run the example code against it without Godot.

## D-021 — Godot robot: one rigid chassis on raycast wheel suspension (2026-10-25)
- **Decision:** the robot is one RigidBody3D (YAML mass, centre of mass, inertia; CAD colliders except wheels and
  gripping mechanisms) carried by one ray per wheel: a stiff spring-damper (1 mm static sag, ζ = 0.7, physics.yaml,
  ASSUMED) whose force is the wheel's normal load. The shared `RobotPowertrain` (also used by the flat-floor sim)
  turns hub commands into wheel forces. Meshes from the pipeline are Y-up; the robot undoes that turn before applying
  the YAML cad_to_robot. The field's y = 0 is the tile top (`LoadedField.TileTopY`); `GroundY` is the venue floor.
- **Why:** wheel loads, weight transfer and tilt come out of the rigid-body motion; the same powertrain in tests and in
  Godot keeps the C# benchmarks valid (Godot forward run 1.82 m vs 1.85 m flat-floor reference).
- **Alternatives:** wheel colliders (Godot friction is per body, so frictionless wheels would make the whole chassis
  frictionless); joints per wheel (stiff, slow, unnecessary for the force model).

## D-022 — Mechanism contacts with compliance (2026-10-25)
- **Decision:** rollers, sweeper wheels and the flywheel touch elements through `CompliantContact` (squeeze spring with
  element and surface stiffness in series, slip friction from the pair table); the reaction loads their motor or servo.
  Windmill paddles are kinematic bodies. These parts have no colliders.

## D-023 — Code editor = Godot CodeEdit + Eclipse JDT LS 1.62 (2026-10-25)
- **Decision:** the editor uses Godot's CodeEdit with an LSP client for JDT LS (runs on Java 25+ — its bundles require JavaSE-25 — here JDK 26; team
  code still compiles for Java 17). The server sees a generated Eclipse project outside the team repository that links
  TeamCode/src/main/java and the shim jar + sources jar. JDT LS was downloaded from download.eclipse.org (1.62.0,
  SHA-256 f587dafd…09342 verified) into generated/tools.
- **Evidence:** completion of `telemetry.` lists `addData` from the shim; a syntax error is reported on its line and
  clears after the fix; 12 s including server start (tests/dotnet/Sim.Editor.Tests).

## D-024 — Per-project build folder for TeamCode (2026-10-25)
- **Decision:** `build-adapter` takes `-PbuildOut`; each project builds into the app's own folder (incremental, the
  repository is never written to). Save → build → OpModes listed in about 2 s with a warm Gradle daemon.

## D-025 — Phase 8 uses the git CLI (2026-10-25)
- **Decision:** git CLI (user delegated the choice: "For github you choose"). It reuses the user's git config and
  credential helper, supports every operation (clone, status v2, diff, stage, commit, push, pull, branches, conflicts)
  and needs no native library. GitHub device-flow sign-in stores the token only in Windows Credential Manager and gives
  it to git through the environment of its own process (never as an argument or in a log). No force push exists in the
  client; commits with likely secrets or files > 10 MB and pushes to main/master need confirmation.
- **Alternatives:** LibGit2Sharp (native binaries per platform, credential handling to reimplement, no Git Credential
  Manager integration).

## D-026 — Practice start pose and field control (2026-10-25)
- **Decision:** in practice the robot starts on the audience-side lane facing blue (ASSUMED, D-006 "somewhere ok");
  during a match the rules engine (Assisted mode) disables robot outputs between periods; starting a match presses
  PLAY on an INIT-ed OpMode like the drive team would.

## D-027 — Guided element path for the StarterBot intake and launcher (2026-10-25)
- **Decision:** capture, hopper storage, feeding and launching follow robot YAML ball_path rules instead of contact
  physics: an element in the capture zone of the inward-turning Gecko row is stored (capacity 4); each 90° of windmill
  rotation in the feeding direction launches the oldest one at half the flywheel rim speed (rolling between rim and
  hood) along the YAML launch direction, and the flywheel loses m·v·R of angular momentum (RPM drop and recovery stay
  physical). The sweepers keep the contact model.
- **Evidence:** with contact-only intake the CAD gap between the Gecko row and the foam bar (42 mm) is far smaller than
  a POLLEN (71 mm); the ball jammed in the nip at z = −0.151 m (Godot scenario robot_intake_pickup trace). Passing it
  needs ball/foam deformation data that the resources do not contain (spec 04 allows "compression"; the numbers are
  unknown). The spec deviation is limited to this robot's ball path and can be replaced when measured.


## D-028 — AprilTag detection runs in the simulator with the AprilTag 3 library (2026-10-25)
- **Decision:** the real AprilTag 3 C library (BSD-2, from the pinned pupil-apriltags wheel) runs in C# (P/Invoke) on
  the grey rendered frame, with the decimation the robot code set; detections travel with the frame to the JVM, where
  the SDK 12 AprilTagProcessor logic (ported) solves poses with OpenCV. Spec 09 asked to "decide and document" between
  JNI and a Java port: this keeps one native library in one process and no JNI build.
- **Consequences:** a new decimation applies to frames captured after it is set; APRILTAG_BUILTIN pose solving is
  approximated with OpenCV IPPE_SQUARE (logged once). The bundled apriltag_to_image is the old buggy version, so tag
  images are drawn from the family code table.

## D-029 — Sensors at driver level with device firmware in the simulator (2026-10-25)
- **Decision:** each sensor is a C# device model (Pinpoint odometry, OTOS tracking, colour, distance, touch) that steps
  from ground truth and exposes the values its SDK driver reads; the shim drivers keep the SDK API, units,
  quantization (OTOS int16, Pinpoint float32) and corrupt-read checks, and send register writes back. Protocol v2/v3.
- **Why:** register-level emulation (VL53L0X init sequences, APDS-9151 registers) would cost much and add nothing a
  team's code can observe through the SDK classes.

## D-030 — OpenCV 4.9 (openpnp packaging) for the vision API (2026-10-25)
- **Decision:** org.openpnp:opencv:4.9.0-0 from Maven Central (SHA-256 pinned in scripts/setup_tools.ps1): the official
  Java bindings plus the Windows x64 native library, loaded by the runner at start-up. The SDK ships OpenCV 4.10; the
  official 4.10 Windows package is a 180 MB self-extracting exe (antivirus removed the download). Java APIs used by
  team code are the same; a 4.10-only call would fail to compile.

## D-031 — BIOBUZZ tag stickers and cluster frame (2026-10-25)
- **Decision:** sticker print axes from the CAD part frame: right = −X, up = −Y. Evidence: the reference-hole notches of
  the CAD sticker are on its +Y edge, 7.0 in from the centre, which is the bottom edge with the notches in the printable
  PDFs; §9.9 p75 "bottom edge oriented towards the center of the FIELD" agrees; with this orientation the SDK cluster
  origin (7.1874 in above the tag row, 5.622 in behind it) lands within 3-4 cm of each CELL opening's centre (SDK 12.0
  release notes: "the Origin of each cluster is located in the center of the Cell opening"). The other orientation
  misses by 7 cm. Tag row 0.26 in above the sticker middle (printable pages).

## D-032 — Team robot RoboDojo #23205 as data on the shared robot model (2026-10-25)
- **Decision:** the team's turret robot is `config/robots/robodojo_23205.yaml` plus `config/cad/robodojo_23205.yaml`, using
  the following generic extensions (no robot-specific code):
  - Mechanisms can be `mounted_on` another one (the shooter and hood ride on the turret: their meshes hang under the
    turret pivot, their colliders are re-posed every tick).
  - Mechanism angle `limits` act as hard stops on the driving motor's shaft.
  - `driven_by`/`follow_ratio` spin follower stages.
  - Positional-servo `arm`s (kinematic, like the windmill paddles) start at their `cad_servo_angle`, and a servo's
    `speed_ratio` gears its arm.
  - A device's `encoder_counts_per_output_rev` models an external encoder (signed).
  - The guided path takes a configurable `feeder` (servo or motor), a `gate` servo window, a `turret`, a `hood`
    (elevation from the servo angle) and a `spring_nip`.
- **Spring nip:** the element pushes the sprung plate back (preload + rate·push, up to its travel), then is itself
  squeezed (its contact stiffness). The wheel can give it at most μ·N·L/v of impulse, so v = min(rim/2, √(μ·N·L/m)).
  POLLEN (71 mm in a 52 mm gap, 19 mm push, inside the 20 mm travel) slips; NECTAR bottoms out the travel and grips
  fully. This answers slide 6, "nectar will shoot different than pollen due to different compression".
- **Why:** the brief asks for mechanisms as data, and every addition is reusable for the next team robot (Phase 13).
  Contact simulation inside the robot stays out (D-027).
- **Found in the CAD:**
  - The encoder pinion meshes the motor pinion, not the ring, so the Through Bore Encoder counter-rotates. The
    TeleOp closes its own loop with the sign flipped; RUN_TO_POSITION would run away, as on the real robot.
  - Both left mecanum wheels have the same roller direction. The sim uses the standard X layout (open point for
    the team).

