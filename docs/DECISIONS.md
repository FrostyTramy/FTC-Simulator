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
