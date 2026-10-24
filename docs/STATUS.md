# Status

**Current phase:** 1 — Rules (done, self-reviewed per D-013).
**Next phase:** 2 — CAD pipeline. **Blocked for the Godot part by Q-001** (Godot cannot start).

## Phase 0 acceptance
| Criterion | State |
|---|---|
| `docs/RESOURCES.md` complete | ✅ |
| Tools OK | ⚠️ all OK including `.venv`, except Godot (hangs at start, Q-001) |
| Skeleton committed | ✅ |

## Phase 1 acceptance
| Criterion | State |
|---|---|
| Manual text extracted with page numbers | ✅ `tools/manual/extract.py` → `docs/manual_text/` (git-ignored, D-008) |
| `docs/game_rules.md` | ✅ field, elements, setup, timing, scoring, RP, penalties, fouls, human player, robot limits, interpretations |
| `config/rules_biobuzz.yaml` with § refs | ✅ 100+ quantities, all sourced, schema-validated |
| `docs/open_questions.md` | ✅ |
| User approval of game_rules.md | delegated (D-013); please skim §13 "Interpretations" when convenient |

## What works
- Inventory tool, tool check, test scripts (Phase 0).
- Manual extraction with damaged-text detection; AprilTag ID decoder.
- 20 pytest tests passing (`./scripts/test_all.ps1`).

## Known problems
- Godot hangs at process start (Q-001, Symantec Endpoint Protection suspected).
