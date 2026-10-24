# BIOBUZZ game rules (for the simulator)

Source: `BIOBUZZ_Competition_Manual_TU06.pdf` (TU06, 2026-10-22). Every item cites the manual
section and page (`§x.y pN`), a figure, or a rule number. All numbers are also in
`config/rules_biobuzz.yaml`, which is what the code reads. Imperial values are the manual's
primary units; metric values in brackets are the manual's own conversions.

The text layer of the manual drops some characters (digits "3", letters "R", "S" in places).
Every number below was checked against the rendered page; the affected places are marked ⚑.

Items marked **[SIM]** are our interpretation of how a rule is evaluated by software. They are
not in the manual; they are listed again in §13 and in `docs/open_questions.md`.

---

## 1. Coordinate system and names [SIM]
- Viewed from the audience: **red ALLIANCE AREA on the left, blue on the right** (§9.5 p68).
- TILE grid (Fig 9-5 p67): columns **A–F** left→right, rows **1–6** from the audience wall to the
  rear wall. Tile seams (Fig 9-4): V–Z between columns, 1–5 between rows.
- Sim frame: origin at the field centre on the TILE top surface, **+X towards blue** (right),
  **+Y away from the audience**, **+Z up**, metres. Tile A1 is the front-left corner.
- Red side of the field = columns A, B, C; blue side = D, E, F (G304.A p104, G402 p106).

## 2. FIELD (§9.1–9.4 p63–67)
| Item | Value | Source |
|---|---|---|
| Official geometry | the 3D CAD model; tolerance ±1 in (2.5 cm) | §9.1 p63 |
| FIELD size (inside of walls) | ≈ 144 × 144 in (365.75 cm) | §9.2 p64 |
| TILES | 36 interlocking foam tiles, ≈ 24 × 24 × 0.59 in (60.95 × 60.95 × 1.50 cm) | §9.2 p64 |
| Perimeter | AndyMark am-0481 perimeter kit | §9.2 p64 |
| Field elements | 1 HIVE structure (frame + red HIVE + blue HIVE), 4 FLOWERS | §9.2 p64 |
| Tape | 1 in (2.5 cm) or 2 in (5.1 cm) gaffer tape, red and electric blue; never across tile seams | §9.3 p65 |

### 2.1 Areas and zones (§9.3 p65–66, Fig 9-2, Fig 9-3)
"Zone" = inside the FIELD, "area" = outside. All are **infinitely tall volumes** and include
their tape lines.
| Name | Size | Where (Fig 9-2, Fig 9-5) |
|---|---|---|
| ALLIANCE AREA | ≈ 97 in (246.40 cm) wide × 54 in (137.15 cm) deep, outside the FIELD | red: left wall; blue: right wall |
| LOADING ZONE | ≈ 23 in (58.40 cm) wide × 11 in (27.95 cm) deep, against the alliance wall | red: left wall, tile A5; blue: right wall, tile F2 |
| GARDEN | ≈ 23 in (58.40 cm) × 2 in (5.10 cm), defined by the outside edge of the tape | red: audience wall from the front-left corner (A1); blue: rear wall from the rear-right corner (F6) |

The exact zone outlines come from the gaffer tape parts in the field CAD (§9.1).

## 3. HIVE structure (§9.6 p69–72)
- Centre of the FIELD. A frame (two triangular metal structures + crossbar) holds a red HIVE
  and a blue HIVE (§9.6 p69).
- Frame: **49.46 in (125.65 cm) wide, 38.95 in (98.95 cm) deep** at the base; **2 pivots with
  their axis 43.95 in (111.65 cm) above the TILES** (§9.6.1 p69, Fig 9-8).
- Each HIVE: 2 CELLS of the same colour, ≈ **18.8 in (47.8 cm)** apart, joined by an assembly
  that rotates on the pivot (§9.6.2 p70, Fig 9-9).
- **Bi-stable**: two stable positions, each with one CELL facing up. It holds its position
  until enough POLLEN/NECTAR are LAUNCHED into the upward CELL (§9.6 p69). The manual gives
  **no count threshold**: tipping is a physical result of the weight in the CELL → simulated
  physically (pivot, mass, dampers), the event is used only for scoring (spec 03).
- CELL opening ≈ **20 in (50.8 cm) wide × 14 in (35.6 cm) tall × 12 in (30.5 cm) deep** (Fig 9-11 p72).
- Dampers: each HIVE has dampers (Blumotion 970A in the CAD) that contact the frame at the end of
  a tip (Fig 10-3 p87).
- From Fig 9-5: red HIVE over columns C (rows 3–4), blue HIVE over column D (rows 3–4).

### 3.1 AprilTags (§9.9 p74–77)
- 36h11 family, **3.25 in (8.25 cm)** square, in clusters of 4 on one sticker, on the **bottom
  face of each CELL facing down** towards the TILES, bottom edge towards the field centre
  (§9.9 p74–75, Fig 9-16). Positioned with the CELL's Reference Holes (Fig 9-15).
- IDs (§9.9 p76 ⚑, confirmed by decoding `PrintableAprilTag_A4/Letter.pdf` with
  `tools/manual/apriltag_ids.py`):

| CELL | IDs (left→right) |
|---|---|
| Red, rear side (opposite the audience) | 30, 31, 32, 33 |
| Red, audience side | 34, 35, 36, 37 |
| Blue, audience side | 38, 39, 40, 41 |
| Blue, rear side | 42, 43, 44, 45 |

- Printable layout: tag pairs 2.75 in (7.0 cm) apart … 6.5 in (16.5 cm), 7.0 in (17.8 cm)
  (printable PDFs, p1). Exact poses come from the AprilTag sticker parts in the field CAD.

## 4. FLOWER (§9.7 p72–73, Fig 9-12)
- 4 FLOWERS, attached to the perimeter wall. Placement (Fig 9-2, 9-5): rear wall at seam W
  (between B and C), right wall at seam 4, left wall at seam 2, audience wall at seam Y.
- Top opening ≈ **4 in (10.15 cm)** diameter, ≈ **21.5 in (54.6 cm)** above the TILES.
- Backstop on top, **1.25 in (3.15 cm)** tall.
- Retrieval Opening at the bottom ≈ **3.55 in (9.0 cm) tall × 3.57 in (9.1 cm) deep**.
- Lower ring on the TILES ≈ **0.4 in (1.0 cm)** tall with a hole ≈ **2.79 in (7.1 cm)** diameter.
- Upper and middle rings joined by 4 HIPS pipes; middle and lower ring joined by a square
  extrusion on the wall side.
- Scoring volume: **between the top ring and the middle ring** (§10.5.2 p87, CAD Ref 10-4 p88).
- Only POLLEN can come out of the bottom (NECTAR is too big for the retrieval path; G418 p112).

## 5. SCORING ELEMENTS (§9.8 p74)
| Element | Size | Colour | Count |
|---|---|---|---|
| POLLEN | ≈ 2.8 in (7.1 cm) Gopher ResisDent polyethylene ball (am-5851) | yellow | 40 |
| NECTAR | ≈ 3.6 in (9.1 cm) Gopher ResisDent polyethylene ball (am-5852) | red / blue | 8 red + 8 blue |

Not perfectly spherical, sizes vary (§9.8 p74). Mass and bounce are not in the manual →
`ASSUMED` in Phase 3, calibrated in Phase 11.

## 6. Pre-MATCH setup (§10.3 p82–85)
### 6.1 HIVES (§10.3.1 p83, Fig 10-2)
Each HIVE starts with one CELL down and one up: **the CELL that points at a FLOWER is the one
tilted down**. Fig 10-2: red HIVE's audience-side CELL is up; blue HIVE's rear CELL is up.

### 6.2 SCORING ELEMENTS (§10.3.1 p83–84, Fig 10-2; Field Reset Guide)
| What | Where | Count |
|---|---|---|
| POLLEN | 4 in each FLOWER | 16 |
| POLLEN | 4 in the red GARDEN, in a line from the corner nearest the red ALLIANCE AREA, touching the audience wall | 4 |
| POLLEN | 4 in the blue GARDEN, same rule, touching the rear wall | 4 |
| POLLEN | 4 pre-loaded per ROBOT (in/on the ROBOT, or on TILES touching it, on its own side) | 16 |
| NECTAR | 3 in each upward-facing CELL, own colour, touching the back wall, in a line on the side nearest the own ALLIANCE AREA | 6 |
| NECTAR | 5 in each ALLIANCE AREA, own colour (held off-field; the CAD's 2 "Artifact Trays" are these holders) | 10 |

- A ROBOT that is not present: its 4 POLLEN go to about the centre of its LOADING ZONE against
  the wall (§10.3.1 p83, G304 p104).
- Event variations of element counts are possible at Championship/Premier events (§10.3.1 p84);
  the sim uses the standard layout.

### 6.3 ROBOT starting position — G304 (p104)
A ROBOT must be: fully on its own side (red A–C, blue D–F) and inside the perimeter; not attached
to a field element; **touching the perimeter wall**; not touching a FLOWER or in its scoring
volume; **not in the LOADING ZONE**; inside its STARTING CONFIGURATION (18 in cube, R102);
**touching exactly 4 pre-load POLLEN** on its own side; motionless after INIT.
Placement order on request: red 1, blue 1, red 2, blue 2 (§10.3.4 p85).

### 6.4 OpMode (§10.3.3 p84, G305 p105)
Every team selects and INITs an OpMode before the start. For AUTO, the 30-second timer must be
enabled; teams without AUTO select a TELEOP OpMode.

## 7. MATCH timing (§10.1 p81, §10.4 p85, Table 9-1 p79 ⚑)
| Period | Length | Field timer |
|---|---|---|
| AUTO | 30 s | 2:30 → 2:00 |
| AUTO→TELEOP transition | 8 s | 0:08 → 0:01 (separate countdown) |
| TELEOP | 2 min (120 s) | 2:00 → 0:00 |

Audio cues (Table 9-1 p79 ⚑; courtesy only, the visual timer is authoritative):
| Event | Timer | Cue |
|---|---|---|
| MATCH start | 2:30 | "This MATCH begins in 3, 2, 1, GO" (optional), "Cavalry Charge" |
| AUTO ends | 2:00 | "Buzzer x 3" |
| Transition | 0:08–0:01 | "Drivers, pick up your controllers, 3-2-1" |
| TELEOP begins | 2:00 | "3 Bells" |
| FLOWER ownership unlocked | 1:00 | "Chime" |
| Final 20 seconds | 0:20 | "Train Whistle" |
| MATCH end | 0:00 | "3-second Buzzer" |
| MATCH stopped | — | "Foghorn" |

- During AUTO the DRIVERS give no input (§11.4.1 p106, G401: only ▶ at the start within a
  MOMENTARY margin, ■ any time, or for safety).
- **Transition**: no powered movement (G403 p106). Teams may stop AUTO, INIT and start a TELEOP
  OpMode during the transition; inertia/gravity/de-energizing are not violations (p107).
- **After 0:00**: no powered movement (G404 p107).
- Robot behaviour in the sim, as on the real field: AUTO OpMode is stopped at 2:00 like the DS
  30-second timer does; ROBOTS are re-enabled at TELEOP start.

## 8. Scoring (§10.5 p86–91)
### 8.1 Point values (Table 10-2 p91)
| Achievement | AUTO | TELEOP | Notes |
|---|---|---|---|
| LEAVE | 3 | — | per ROBOT |
| PARK | 5 | 5 | per ROBOT; AUTO PARK judged at end of AUTO, TELEOP PARK at end of MATCH |
| HIVE TIP | 20 | 20 | per TIP; AUTO if the TIP is complete before TELEOP starts |
| POLLEN/NECTAR remaining in CELL | — | 2 | per element, at end of MATCH |
| Bottom NECTAR Bonus | — | 5 | per FLOWER |
| POLLEN/NECTAR in an owned FLOWER | — | 2 | per element |
| POLLEN/NECTAR in GARDEN | — | 1 | per element |

### 8.2 When things are assessed (§10.5 A–G p86)
- **A.** HIVE TIPS: throughout the MATCH, until all elements and ROBOTS are at rest after the end.
- **B.** TIPS completed before TELEOP starts count as AUTO (TIPs that complete during the
  transition are AUTO [SIM: transition belongs to "before TELEOP starts"]).
- **C.** Elements remaining in CELLS: after everything is at rest at the end of the MATCH.
- **D.** FLOWERS: throughout, final assessment at the end of TELEOP once at rest.
- **E.** GARDEN: at the end of TELEOP once at rest.
- **F.** LEAVE and AUTO PARK: at the end of AUTO.
- **G.** TELEOP PARK: at the end of the MATCH.
- Achievements made before the start, during the transition, or after 0:00 may be penalised (p86).

### 8.3 HIVE TIP (§10.5.1 p87, Glossary p172)
A HIVE is TIPPED when **(A)** it moves from one stable state to the other, the down CELL
becoming the up CELL, **and (B)** the damper that was not touching the frame starts touching it.
- Only LAUNCHING into the upward CELL may cause a TIP (G417).
- Each TIP of an ALLIANCE's HIVE scores for that ALLIANCE and **unlocks one NECTAR** for that
  ALLIANCE's human player (§10.1 p81, G426 p116).
- [SIM] TIP detection: HIVE angle passes from one stable side to the other and the damper
  collider of the new side touches the frame.

### 8.4 Elements remaining in CELL (§10.5.1 p87)
At the end of the MATCH, every POLLEN and/or NECTAR in an **upward-facing CELL** scores 2 for
"that ALLIANCE" — [SIM] the ALLIANCE whose HIVE it is.

### 8.5 FLOWER (§10.5.2 p87–89)
- An element scores when it is **at least partially within the FLOWER scoring volume** (top ring
  to middle ring).
- **Bottom NECTAR Bonus**: the ALLIANCE with the bottom-most NECTAR of its colour that meets the
  scoring criteria earns 5 (per FLOWER).
- **FLOWER Owner**: the ALLIANCE with the top-most scoring NECTAR of its colour owns the FLOWER
  and earns 2 for **every** scoring POLLEN and NECTAR in it, whoever placed them.
- Scoring only begins with **1:00 remaining** (G410); NECTAR placed earlier still counts but is
  penalised.
- Elements may only enter through the top; only POLLEN may be removed from the bottom (G418).
- [SIM] "bottom-most/top-most" = lowest/highest NECTAR centre height among scoring NECTAR.

### 8.6 GARDEN (§10.5.3 p89–90, Fig 10-6)
- POLLEN or NECTAR (either colour) at least partially in a GARDEN zone scores 1 for the
  ALLIANCE of the GARDEN's colour. GARDENS are not protected.
- [SIM] "partially in" = the ball's sphere intersects the zone's footprint prism (Fig 10-6:
  balls overlapping the tape strip in top view count, balls beside it do not).

### 8.7 LEAVE and PARK (§10.5.4 p90, Fig 10-7)
- LEAVE: the ROBOT has moved so it is **no longer touching the perimeter wall** (end of AUTO).
- PARK: the ROBOT is **at least partially in its own ALLIANCE's LOADING ZONE** (top-view overlap
  of the robot outline with the zone, Fig 10-7).

### 8.8 RANKING POINTS (Table 10-2/10-3 p91)
| RP | Condition | RP | Threshold (other events / regional / championship) |
|---|---|---|---|
| SWARM | combined LEAVE + PARK points ≥ threshold | 1 | 16 points / TBA / TBA |
| POLLINATOR 1 | number of TIPS ≥ threshold | 1 | 4 TIPS / TBA / TBA |
| POLLINATOR 2 | number of TIPS ≥ threshold | 1 | 7 TIPS / TBA / TBA |
| WIN | more MATCH points | 3 | — |
| TIE | equal MATCH points | 1 | — |
The sim uses "all other events" by default; thresholds are configurable (Decision D-010).

### 8.9 Penalties (Table 10-4 p92)
| Penalty | Effect |
|---|---|
| MINOR FOUL | +5 to the opponent's MATCH points |
| MAJOR FOUL | +20 to the opponent's MATCH points |
| YELLOW CARD | warning; a second → RED CARD (§10.6.1 p93) |
| RED CARD | team DISQUALIFIED for the MATCH |
| DISABLED | all outputs stopped for the rest of the MATCH |
| DISQUALIFIED | 0 MATCH points and 0 RP (Qualification) |
Duration words (§10.6 p92): **MOMENTARY < ≈3 s, CONTINUOUS > ≈10 s, REPEATED = more than once
per MATCH**. "Per instance"/"per element"/"every 3 s" counting follows Table 10-6 p95–96.

## 9. Human player and NECTAR (§10.1 p81, §10.8.2 p97, G422–G428 p114–117)
- NECTAR may be entered by a DRIVE TEAM member only: **one extra NECTAR per TIP of their own
  HIVE**, or **all remaining NECTAR when 60 s or less remain**, whichever first (G426).
  Violation: MINOR FOUL per NECTAR.
- Entry only by hand (no tool), only own colour, and it must **touch the TILE inside the own
  LOADING ZONE before touching a ROBOT or field element** (G427). MINOR FOUL per NECTAR.
- NECTAR that leaves the FIELD goes back to its ALLIANCE's drive team and may be re-entered at
  any time (G426 note, G427, §10.8.2). POLLEN that leaves the FIELD is put back by FIELD STAFF
  "at the earliest safe opportunity in the nearest convenient location" (§10.8.2).
- DRIVE TEAM may not remove elements from the FIELD (G428, MINOR per element) or touch ROBOTS /
  elements on TILES / field elements (G425).
- [SIM] the human player is an action ("enter NECTAR") taken by a player or by automation,
  dropping the NECTAR into the LOADING ZONE; the sim enforces the count rule (Phase 10).

## 10. Fouls relevant to ROBOTS (§11.4 p106–117)
Sim handling: **Auto** = detected by the sim and applied in Strict mode (suggested in Assist);
**Review** = sim flags a candidate for the referee/user; **No** = not simulated.

| Rule | Summary | Penalty (base; STRATEGIC escalations in the manual) | Sim |
|---|---|---|---|
| G304 | Start setup (own side, touch wall, not in LOADING ZONE, not in FLOWER, 18 in cube, 4 pre-loads, motionless) | MATCH does not start | Auto (setup check) |
| G401 | No DRIVER input during AUTO | VERBAL; MAJOR + YELLOW if STRATEGIC | Auto (gamepad input ignored during AUTO) |
| G402 | No AUTO interference on the opponent's side (A–C red / D–F blue): CONTROLLING elements there, contacting an opponent ROBOT there | MAJOR per MATCH | Auto |
| G403 | No powered movement in the transition | VERBAL; MAJOR + YELLOW if STRATEGIC | Auto (outputs disabled, like the field) |
| G404 | No powered movement after 0:00 | VERBAL; MAJOR + YELLOW if STRATEGIC | Auto (outputs disabled) |
| G405 | Do not deliberately eject elements from the FIELD | MAJOR per element | Review |
| G406 | Do not damage the ARENA | VERBAL… | No |
| G407 | CONTROL at most **4** SCORING ELEMENTS at a time | VERBAL; MAJOR + YELLOW if STRATEGIC (≥ 6, or repeated > MOMENTARY with 5) | Auto (count) / Review (STRATEGIC) |
| G408 | Do not CONTROL opponent NECTAR | VERBAL; YELLOW if STRATEGIC | Auto (flag) |
| G409 | Do not catch/deflect elements released by a TIPPED HIVE before they touch something else | VERBAL; YELLOW if STRATEGIC | Review |
| G410 | No NECTAR into a FLOWER scoring volume before **60 s** remain | MAJOR per NECTAR | Auto |
| G411 | No hoarding | MAJOR + YELLOW | No |
| G412 | ROBOT under control, nothing outside the FIELD | DISABLED + VERBAL | Review |
| G413 | Stop when told | — | No |
| G414 | Identifiable ROBOT | — | No |
| G415 | No grabbing/attaching/entangling ARENA elements (except SCORING ELEMENTS) | VERBAL; YELLOW if STRATEGIC | Review |
| G416 | Stay within R105 expansion limits | VERBAL; MAJOR per instance if STRATEGIC | Auto (bounding volume check) |
| G417 | Only LAUNCHING into the up CELL may move a HIVE; no ramming, no contacting a HIVE | VERBAL; MAJOR + YELLOW if STRATEGIC | Review (contact with HIVE flagged) |
| G418 | Only enter FLOWER from the top, only remove POLLEN from the bottom | VERBAL; MAJOR + YELLOW if STRATEGIC | Review |
| G419 | Do not damage an opponent ROBOT | VERBAL; MAJOR + YELLOW/RED if STRATEGIC | No |
| G420 | Do not tip or entangle an opponent | VERBAL; MAJOR + YELLOW/RED if STRATEGIC | Review (tipped robot flagged) |
| G421 | PIN at most **3 s**; ends after **2 ft (≈61 cm)** separation for > 3 s, either ROBOT 2 ft from the pin start for > 3 s, or the pinner gets pinned | MAJOR + MAJOR every 3 s | Auto (pin detection) |
| G204 | Forcing an opponent to break a rule | MAJOR per instance | No |
| G426 | Human player enters NECTAR only after own TIPs (1 per TIP) or with ≤ 60 s left | MINOR per NECTAR | Auto (sim enforces) |
| G427 | NECTAR entered by hand, own colour, touching the TILE in the own LOADING ZONE first | MINOR per NECTAR | Auto (sim enforces) |
| G428 | DRIVE TEAM may not remove elements from the FIELD | MINOR per element | Auto (not possible in the sim) |

## 11. ROBOT construction limits the sim checks (§12 p118–144)
| Rule | Limit |
|---|---|
| R102 | STARTING CONFIGURATION inside an **18 × 18 × 18 in (45.70 cm)** cube, all parts stationary; pre-loaded elements may stick out |
| R103 | may hold start config mechanically or with an INIT OpMode holding positions |
| R104 | **no weight limit** |
| R105 | after start: always inside **18 × 24 in (45.70 × 61.0 cm) footprint × 29 in (73.65 cm) tall**, vertical axis from the FIELD surface; limits mechanical, not software; no deliberate detaching |
| R204 | no floor grabbing or suction |
| R501 | legal motors: AndyMark NeveRest (am-3104, am-3104b, am-3104c), goBILDA Yellow Jacket 5201/5202/5203/5204, goBILDA 5000 Series, Modern Robotics/MATRIX 5000-0002-0001, NFR Yuksel NFR-600-100-000, REV HD Hex REV-41-1291, REV Core Hex REV-41-1300, Studica Maverick 75001, SWYFT Spike SR-MOTOR-DC-01, TETRIX MAX 739530/39530, TETRIX TorqueNADO W44260, WATTOS Stingray WDM12 (Table 12-1 p131) |
| R502 | servos: mechanical output ≤ **8 W** @6 V and stall current ≤ **4 A** @6 V ⚑; linear servos ≤ **1 A** @6 V; P = 0.25 × stall torque (N·m) × no-load speed (rad/s) (Table 12-2 p132). Control/Expansion Hub servo ports supply **5 V**; servo power modules **6 V** |
| R503 | at most **8 motors and 8 servos** |
| R601 | exactly **1** 12 V NiMH main battery: am-5290 or goBILDA 3100-0012-0020 (Table 12-4 p134); 20 A ATM fuse |
| R701 | one ROBOT CONTROLLER (REV Control Hub REV-31-1595, or phone + Expansion Hub REV-31-1153) (Glossary p172) |
| R801 | no pneumatics, vacuum or blowers |

## 12. Definitions used by the scoring code (Glossary §16 p169–173)
- **CONTROL**: element fully supported by, or stuck in/on/under the ROBOT, or intentionally
  herded (pushed in a preferred direction with a flat or concave face); needs direct or
  transitive contact. Not CONTROL: bulldozing, deflecting, elements already LAUNCHED
  (G407 p108). [SIM] fully supported or held for > MOMENTARY; herding = in contact with the
  robot and moving with it for > 1 s → `ASSUMED` thresholds (Phase 3).
- **LAUNCH**: element shot into the air, propelled across the floor, or thrown forcefully.
- **PIN**: preventing movement of an opponent ROBOT by contact, direct or transitive.
- **TIP**: see §8.3.
- **"At rest"**: [SIM] all elements and ROBOTS below a speed threshold for a settle time, or a
  maximum settle time (Phase 3, `ASSUMED`).

## 13. Interpretations and open points
| ID | Item | Our choice |
|---|---|---|
| I-1 | "partially in" a zone | sphere/robot outline intersects the zone's infinitely tall prism |
| I-2 | CELL points go to | the ALLIANCE owning the HIVE |
| I-3 | Top/bottom NECTAR in a FLOWER | by centre height of scoring NECTAR |
| I-4 | TIP during the transition | counts as AUTO (§10.5 B: "complete prior to the start of TELEOP") |
| I-5 | "At rest" / CONTROL / herding thresholds | `ASSUMED` values in Phase 3 |
| I-6 | Artifact Trays (CAD) | off-field holders of the 5 reserve NECTAR per ALLIANCE |
| I-7 | Not simulated | conduct, cards across matches, inspection paperwork, tournament rules (§13), damage |
