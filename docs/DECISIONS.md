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
