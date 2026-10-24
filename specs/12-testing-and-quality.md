# 12 — Testing and Quality

Nothing is done until it is tested and the tests pass. Tests are part of every
phase's deliverable.

## Test layers
1. **Unit tests (C#)**: motor/battery/servo models, scoring predicates,
   state machine transitions, config loaders, IPC encoding/decoding.
   (xUnit/NUnit or GdUnit4 for Godot-dependent code.)
2. **Unit tests (Java, JUnit 5)**: SDK shim behaviors (motor modes, bulk
   caching, IMU orientation combos, telemetry, gamepad mapping, LinearOpMode
   lifecycle and stop semantics).
3. **Python tests (pytest)**: CAD pipeline on small sample STEP files (units,
   axes, instancing, collider fitting, stable IDs).
4. **Headless Godot scenario tests**: `tests/scenarios/*.yaml` place robots
   and elements, run sim for N seconds, assert scores/poses/events.
   Runnable with one command (`godot --headless` + a test runner scene).
5. **Integration tests**: build a sample TeamCode project → run OpMode in the
   JVM runner → robot moves as expected (e.g. "drive forward 1 s, robot moved
   between X and Y meters").
6. **Golden replays**: recorded matches replayed after every change; state
   hashes must match (determinism) or the change must be intentional and the
   golden file updated with a note in CHANGELOG.md.
7. **Physics benchmarks**: top speed, acceleration, strafe ratio, ball drop,
   shot trajectory; compared with `docs/calibration.md` targets, with
   tolerances.
8. **Performance tests**: full 4-robot match headless (physics time per step)
   and rendered (fps on High) with numbers logged per commit.
9. **Compatibility tests**: goBILDA example project, FTC samples, Road Runner,
   Pedro Pathing projects (see SDK shim spec).

## Commands (create them, keep them working)
```
./scripts/test_all.ps1          # everything
./scripts/test_unit.ps1
./scripts/test_scenarios.ps1
./scripts/test_compat.ps1
./scripts/bench.ps1             # perf + physics benchmarks → reports/
```

## CI
- GitHub Actions on Windows runners: build C#, build Java, run unit,
  scenario (headless) and compat tests on every push/PR; upload reports.
- Large generated assets are not committed; CI uses small sample fixtures.
  Ask me before adding Git LFS.

## Code quality
- C#: nullable enabled, warnings as errors in core modules, `dotnet format`.
- Java: Google Java style for shim/runner code.
- Python: ruff + black, type hints.
- No dead code, no commented-out blocks, no magic numbers outside config.
- Public classes/methods documented briefly; complex math has a comment
  with the formula and source.

## Definition of done (every task)
- Works in the running app (screenshots/numbers in the report).
- Tests added/updated and passing; no new warnings.
- Configs validated by schema; new ASSUMED values listed.
- Docs updated (STATUS, ARCHITECTURE, DECISIONS, CHANGELOG).
- Committed with a clear message.

## Bug reports
`docs/bugs/` template: steps, expected, actual, replay file, logs, sim build
hash. Fixes include a regression test (often a scenario or golden replay).
