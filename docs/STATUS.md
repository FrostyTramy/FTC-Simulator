# Status

**Current phase:** 0 — Inventory and setup (built, waiting for review).
**Next phase:** 1 — Rules (starts when the user says "start phase 1").

## Phase 0 acceptance
| Criterion | State |
|---|---|
| `docs/RESOURCES.md` complete | ✅ all 14 resource files, incl. the 7 field documents added 2026-10-24 |
| Tools OK | ⚠️ Git, .NET SDK 10, JDK 17, Python 3.13, Node OK. Missing: project `.venv` (user installs, `docs/SETUP.md` §1). Godot .NET found but hangs at start (Q-001) |
| Skeleton committed | ✅ |

## What works
- `tools/inventory/inventory.py`: hashes and lists the originals, detects replaced files, scans
  STEP assemblies and units (`--scan-step`: field 0.6 s, robot 5.7 s).
- `scripts/check_tools.ps1`, `scripts/test_all.ps1`, `scripts/test_unit.ps1`.
- 11 pytest tests (STEP units mm/m/inch, assembly flattening, wrapped strings, manifest checks).

## Known problems
- Godot hangs at process start on this laptop (Q-001).
- pytest prints a pytest-asyncio deprecation warning when run with the system Python (plugin in
  the user site-packages); gone once `.venv` is used.
