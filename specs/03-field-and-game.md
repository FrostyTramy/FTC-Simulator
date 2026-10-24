# 03 — Field, Mechanisms, Game Logic, Scoring

## Source of truth
`docs/game_rules.md` is written in Phase 1 from the manual, with section
references for every rule. All values below that are game-specific come from
there. If the manual and the field STEP disagree, report it and ask me.

## Field model
- Static: floor tiles (with seams), perimeter walls, fixed structures.
- Dynamic game elements: every pollen, nectar and other loose element in the
  STEP becomes a rigid body at its CAD position (starting layout). Elements
  kept off-field at start (e.g. Nectar reserves) are pooled, not deleted.
- Moving field mechanisms (e.g. hives that tip): rigid bodies + joints from
  `field_mechanisms.yaml`.
- Alliance-specific objects tagged `red`/`blue` with mirrored/rotated
  placement validated against the manual.
- Field coordinate system: define it once (origin, axes) to match the FTC
  field coordinate convention used by the SDK/AprilTag library for the season,
  documented in `docs/ARCHITECTURE.md`. All poses use it.

## Mechanism YAML schema (`config/field_mechanisms.yaml`)
```yaml
version: 1
bodies:
  - id: hive_red_1
    parts: [ "Hive_Red_1/Bucket", "Hive_Red_1/Cell_*" ]   # CAD part paths/globs
    type: dynamic            # static | dynamic | kinematic
    mass_kg: auto            # auto = from manifest, or number
    source: "CAD Hive_Red_1; manual §X.Y"
joints:
  - id: hive_red_1_pivot
    type: hinge              # hinge | slider | fixed | ball | cone_twist
    body_a: hive_red_1_base
    body_b: hive_red_1
    anchor: auto             # auto = detected pivot, or [x,y,z] in field coords
    axis: auto
    limits: { min_deg: 0, max_deg: 90 }   # ASSUMED until checked
    damping: 0.2             # ASSUMED
    spring: null
    latch: { release_on_event: hive_red_1_tip }   # optional
    source: "detected.yaml#joint_12 (confidence 0.8)"
triggers:
  - id: hive_red_1_cell
    shape: from_part "Hive_Red_1/Cell_Top"   # or box/cylinder with dims
    counts: [pollen]
    alliance: red
    rule: "manual §X.Y"
events:
  - id: hive_red_1_tip
    when: "count(hive_red_1_cell, pollen) >= RULE(hive_tip_threshold)"
    do: [ release_latch(hive_red_1_pivot), score(...), grant_nectar(red, 1) ]
    source: "manual §X.Y"
```
- `RULE(name)` refers to numbers in `config/rules_biobuzz.yaml` (thresholds,
  points, timings), each with a manual reference.
- If the real tipping is purely physical (balls' weight tips the hive), model
  it physically with correct masses and pivot, and use the event only for
  scoring. Decide from the manual + field CAD; tell me which and why.
- Unknown values are `ASSUMED` and listed in the Phase 3 review summary.

## Trigger zones and counting (robust scoring)
- A game element counts as "in" a zone per the manual's definition (e.g.
  fully inside, supported by, touching). Implement each definition exactly
  as a reusable predicate: `fully_inside`, `centroid_inside`,
  `supported_by`, `contacting`, `breaks_plane`.
- Debounce: element must satisfy the predicate for N ms (configurable) and
  be "settled" when the manual says scoring is evaluated at rest or at
  match end.
- Separate **live scoring** (shown during the match, e.g. hive tips) and
  **final scoring** (evaluated at the end of the match / end of a period, as
  the manual says), plus a **post-match settle** time before final counts.
- Each scoring event records: time, alliance, element ID, zone, robot that
  last controlled the element (for fouls/attribution), rule reference.

## Game state machine
```
PreMatch → AutoInit → Auto → AutoEnd/Transition → TeleopInit → Teleop
  → Endgame (if manual defines one) → MatchEnd → PostMatchSettle → Results
```
- Durations, transition rules (driver pickup, OpMode switch) from the manual.
- Robots disabled between periods exactly like the real field control
  (motors stop, code stopped as the DS would).
- Human player actions, element introductions (e.g. Nectar entering play
  after a hive tip) as defined by the manual, triggered by events or by a
  "human player" input/automation.
- Field reset: one button returns everything to the start layout
  (deterministic).
- Practice mode: no timer, free reset, spawn/move elements with mouse,
  place robot anywhere.

## Scoring engine
- Event-sourced: score = f(list of scoring events + end-of-match evaluation).
  This makes replays, review and tests easy.
- Breakdown per alliance and per category (auto/teleop/endgame, by rule),
  identical in structure to the official scoreboard/score sheet.
- Ranking points / bonuses if the manual defines them.
- Every rule has unit tests with scripted element placements and a
  scenario test with a real physics drop.

## Fouls and penalties
Automate what can be detected reliably; flag the rest for "referee review".
- Auto-detectable examples (only if the manual has such rules): robot size at
  start, expansion limits (bounding box of the robot over time), launching
  outside allowed zones, controlling more elements than allowed, entering
  forbidden zones, contact with protected robots/areas, pinning duration,
  scoring after the buzzer, robot not stopping.
- Each foul: rule ref, robot, time, evidence (snapshot + replay timestamp),
  severity (minor/major/card), points to the opponent.
- Modes: Strict (auto-apply), Assist (suggest, human confirms), Off.
- Fouls list in the post-match screen, clickable to jump to replay.

## Debug tools
- Overlay: triggers (colored by alliance), joints (axes, limits), colliders,
  element IDs, live counts per zone, current rule thresholds.
- Event log panel with every game event.
- "Scenario" files (`tests/scenarios/*.yaml`): place elements and robots,
  run N seconds, assert scores. Used by tests and for debugging.
