# BIOBUZZ FTC Simulator — Master Brief

You are the lead engineer on a large, long-running project: the most realistic
FIRST Tech Challenge simulator ever made, for the 2026-27 game **BIOBUZZ**.
This file is the short, always-loaded brief. The detailed requirements live in
`specs/`. Read the relevant spec files completely before planning or coding
any part of the system. If this file and a spec disagree, this file wins; tell
me about the conflict.

## The product in one paragraph
A Windows desktop app where FTC teams drive robots on a photoreal BIOBUZZ
field with real gamepads, write real FTC Java code in a built-in editor synced
with GitHub, run that code unchanged on a simulated robot whose motors,
sensors, battery and mechanisms behave like the real hardware, and play full
matches with automatic scoring, fouls, replays and analytics. Code that works
in the sim should work on the real robot, and code that fails on the real
robot (bad loop times, wrong motor direction, unhandled stop) should fail the
same way in the sim.

## Resources (already in this folder)
Project root: `D:\Code\FTC-Simulator`. It contains:
- the **BIOBUZZ field STEP** with every game element already modeled
  (hives, garden/flowers, pollen, nectar, walls, tiles),
- a **goBILDA starter robot STEP**, its **build instructions** and an
  **example code project**,
- the **official game manual (PDF)**.
Phase 0 is a full inventory of these. Never modify, move or delete my original
files; all generated output goes to `generated/`. If you need anything else
(measurements, photos, a cleaner CAD export, a rule clarification, a newer
SDK version), **stop and ask me**. Never invent data or rules.

## Non-negotiable rules
1. **The game manual is law.** Every dimension, timing, scoring rule and foul
   comes from it, with a section reference. Unclear → `docs/open_questions.md`
   and ask me.
2. **Mechanisms and robots are data.** Joints, triggers, motors and sensors
   are defined in reviewed YAML files (`config/`), never hard-coded in scripts.
   Every value carries a source (manual §, CAD part, instruction page, code
   line) or is marked `ASSUMED`.
3. **Real FTC code runs unchanged.** The FTC SDK shim mirrors the real API
   and its behavior, including the annoying parts.
4. **Determinism.** Same inputs + same seed = same match, bit for bit, on the
   same machine. This enables replays and regression tests.
5. **Security.** User code runs only in the sandboxed JVM runner. GitHub
   tokens live only in Windows Credential Manager. Never log secrets.
6. **Stay in phase.** Do one phase at a time (see `specs/13-phases.md`).
   Plan first, wait for my OK, then build, test, commit, report.
7. **Simple and working beats clever.** No speculative frameworks.
8. **Ask, don't guess,** whenever a decision is mine (scope, look, rules,
   trade-offs) or data is missing.

## Tech stack (do not change without asking)
- Godot 4 (latest stable, .NET build), C# for all game code, Forward+ renderer.
- Jolt physics, fixed timestep (start at 240 Hz, see physics spec).
- Robot code: separate JVM process (JDK 17) talking to Godot over a local
  socket + shared memory for camera frames.
- Java tooling: Gradle, Eclipse JDT Language Server for the editor.
- CAD pipeline: Python 3 (cadquery/OCP; FreeCAD headless as fallback).
- Git: git CLI or LibGit2Sharp (decide in the editor phase, ask me).
- Target: Windows 10/11 x64, 1080p/60 fps on a mid-range gaming GPU, with
  quality presets for weaker PCs.

## Spec index (read the ones relevant to your current task)
| File | Covers |
|---|---|
| `specs/00-vision-and-scope.md` | Goals, users, realism benchmarks, budgets, non-goals |
| `specs/01-architecture.md` | Processes, modules, tick loop, IPC protocol, determinism, saves, logging |
| `specs/02-cad-pipeline.md` | STEP → glTF, units/axes, instancing, materials, colliders, joint/wheel detection |
| `specs/03-field-and-game.md` | Field mechanisms, trigger zones, game state machine, scoring engine, fouls |
| `specs/04-physics.md` | Jolt setup, materials, pollen, drivetrains, motors, battery, servos, launchers |
| `specs/05-robots.md` | Robot YAML schema, goBILDA starter import, hubs/ports, robot library |
| `specs/06-ftc-sdk-shim.md` | FTC SDK API reimplementation and exact behaviors |
| `specs/07-code-editor-and-github.md` | Built-in editor, LSP, build/run, hot reload, GitHub sync |
| `specs/08-driver-station-and-match.md` | Driver Station replica, gamepads, match control, scoreboard |
| `specs/09-sensors-and-vision.md` | Sensor models, robot cameras, AprilTags, Limelight |
| `specs/10-graphics-audio-ui.md` | Rendering, lighting, materials, cameras, replays, audio, menus |
| `specs/11-multiplayer-ai-analytics.md` | Online 2v2, AI opponents, drills, logs and analytics |
| `specs/12-testing-and-quality.md` | Test strategy, golden replays, performance, CI, definition of done |
| `specs/13-phases.md` | Ordered phases with deliverables and acceptance criteria |

## Repository layout
```
D:\Code\FTC-Simulator\
  <my original resources>      # read-only
  CLAUDE.md
  specs/                       # requirements (only I change these, you may propose edits)
  docs/                        # RESOURCES.md, game_rules.md, ARCHITECTURE.md, STATUS.md,
                               # DECISIONS.md, open_questions.md, CHANGELOG.md
  config/                      # field_mechanisms.yaml, rules_biobuzz.yaml, materials.yaml,
                               # physics.yaml, motors.yaml, servos.yaml, sensors.yaml,
                               # hardware_latency.yaml, robots/*.yaml, schemas/
  generated/                   # pipeline output, never hand-edited, git-ignored if large
  tools/cad_pipeline/          # Python
  godot/                       # Godot project (C#)
  java/ftc-sdk-shim/           # SDK reimplementation
  java/runner/                 # JVM-side bridge + OpMode runner
  java/build-adapter/          # Gradle project that compiles user TeamCode against the shim
  tests/                       # cross-component and golden-replay tests
  workspace/                   # user code projects opened in the editor (git-ignored)
```

## How to work every session
1. Read `docs/STATUS.md` and the specs for the current phase.
2. Propose a short plan (files to touch, risks, open questions). Wait for OK.
3. Build in small steps. Commit after each working step
   (`phase-N: what changed`).
4. Write and run tests (see testing spec). Nothing is "done" untested.
5. Update `docs/STATUS.md`, `docs/ARCHITECTURE.md`, `docs/DECISIONS.md`
   (why you chose X over Y) and `docs/CHANGELOG.md`.
6. Report: what works, what doesn't, screenshots or numbers, next step,
   questions for me.

If something in the specs turns out to be impossible, too slow or wrong,
say so with evidence and propose an alternative. Don't silently work around it.
