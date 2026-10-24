# Resources inventory

Original files in the project root. They are **read-only**, are **not committed to git**
(see DECISIONS D-001) and are fingerprinted in `tools/inventory/resources_manifest.json`.
`python tools/inventory/inventory.py` reports any file that is new, changed or missing;
`--scan-step` re-creates the CAD numbers below into `generated/inventory/`.

Inventory date: 2026-10-24.

## Summary

| File | Size | What it is | Used in phase |
|---|---|---|---|
| `BIOBUZZ_Competition_Manual_TU06.pdf` | 6.9 MB | Official game manual, Team Update 06, 173 pages | 1, 3, 4, 10, 12 (law for everything) |
| `field-cad-step.zip` | 6.6 MB (35.2 MB STEP) | Official BIOBUZZ field CAD (Onshape, AP242, metres) | 2, 3, 4 |
| `3200-2627-0004.zip` | 70.5 MB (419.5 MB STEP) | goBILDA FTC StarterBot with Mecanum (BIOBUZZ) CAD (Autodesk, AP214, mm) | 2, 5 |
| `3200-2627-0004_assembly-instructions.min.pdf` | 25.9 MB | StarterBot build instructions, 35 pages | 5 |
| `3200-2627-0004_example-code.zip` | 4.8 kB | StarterBot example TeleOp (one Java file) | 5, 6 |
| `Field Reset Guide V26-27.1.pdf` | 141 kB | Pre-match placement of all game elements, 1 page | 1, 4, 10 |
| `Event Field Setup Guide - V1.0.pdf` | 5.4 MB | On-site field assembly, 30 pages | 2, 3 |
| `Initial Field Element Assembly Guide - V1.0.pdf` | 5.9 MB | Assembly of Hives, Flowers, Frame, 42 pages | 3 |
| `Field Acceptance Checklist V26-27.2.pdf` | 338 kB | Field checks every event day, 2 pages | 2, 3 |
| `Field Mitigation Guide V26-27.1.pdf` | 298 kB | Fixes for field problems during events, 2 pages | background only |
| `PrintableAprilTag_A4.pdf` | 721 kB | Printable Hive AprilTag stickers (A4), 8 pages | 1, 9 |
| `PrintableAprilTag_Letter.pdf` | 720 kB | Same, US Letter, 8 pages | 1, 9 |
| `BIOBUZZAprilTagProduction.pdf` | 483 kB | Sticker production art, 4 pages, images only (no text) | 9 |
| `biobuzz-sim-prompt-kit.zip` | 39.7 kB | Byte-identical copy of `CLAUDE.md` + `specs/` | not used (backup) |

Also in the root, **not resources**: `CLAUDE.md`, `specs/` (both committed) and the Godot .NET
editor `Godot_v4.7.2-stable_mono_win64/` + its zip (tool, git-ignored, see `docs/SETUP.md`).

## Details

### Game manual — `BIOBUZZ_Competition_Manual_TU06.pdf`
- PDF 1.7 from Word, created 2026-10-22, text layer present, 162 bookmarks.
- Sections the sim needs: §8 Game Overview (p62), §9 ARENA (p63–80: 9.1 dimensions, 9.4 tile
  coordinates, 9.6 HIVE, 9.7 FLOWER, 9.8 scoring elements, 9.9 AprilTags), §10 Game Details
  (p81–98: 10.4 match periods, 10.5 scoring, 10.6 violations), §11 Game Rules (p99–117),
  §12 Robot Construction (p118–144), §16 Glossary (p169).
- §9.1: **the 3D CAD model is the official representation of the field**, tolerance ±1 in.
- §9.8: POLLEN ≈ 2.8 in (7.1 cm) yellow polyethylene foam ball, 40 per match; NECTAR ≈ 3.6 in
  (9.1 cm), 8 red + 8 blue; neither is perfectly spherical. Matches the field CAD counts.
- §9.9: AprilTags 36h11, 3.25 in (8.25 cm), clusters of 4 on the underside of each Hive CELL.
- **Quality issue:** the text layer loses most AprilTag ID digits on p76 ("IDs 0, , , on the red
  CELL…"); only 0, 7, 38–41 are readable. The full IDs must come from the printable tags.
- Spec conflict: `specs/04` says pollen "~3 in"; the manual says 2.8 in. The manual wins.

### Field CAD — `field-cad-step.zip` → `field-cad-step.step`
- Onshape 1.220 export, STEP AP242 Ed.2, 2026-09-09; units **metres** in all 188 geometric
  contexts; 527,790 entities, scanned in < 1 s.
- One root assembly `am-5850 BIOBUZZ`, depth 4, 146 unique products, 1,076 assembly links,
  993 leaf part occurrences. Names are clean and carry AndyMark part numbers.
- Game elements: 40 × `am-5851 Pollen`, 8 × `am-5852 Blue Nectar`, 8 × `am-5852 Red Nectar`.
- Hives (red + blue): goal ribs, top/back/bottom skins, basket base tubes, goal pivot brackets,
  axle holders, 4 × Blumotion 970A dampers in pivot damper holders, F608ZZ bearings (pivot).
- 4 AprilTag sticker parts (`am-5888-red1/red2/blue1/blue2`, Audience and Scoring).
- 4 Flowers: 16 HIPS pipes, layers B/C/X, peanut supports, backstops, field/under-field brackets.
- Frame: A-frame legs and top bar, 2 ACM panels with panel stickers, feet.
- Tiles: 16 center + 16 side + 4 corner soft tiles (36). Under-tile bars and washers.
- Perimeter: 24 rails, 12 polycarbonate "side glass" panels, panel links, quick-release pins.
- Tape lines: red and blue gaffer tape 11, 20.69, 22.69, 54, 94.82 in long.
- Fasteners (rivets, screws, nuts, washers, cable ties) ≈ 500 occurrences → dropped or merged
  in the pipeline, they matter neither for physics nor for looks at our quality target.
- 15 distinct colours are defined (yellow pollen, red, blue, purple, green, …).
- **Quality issue:** 2 × `am-5706 Artifact Tray` are in the CAD but nowhere in the manual
  (0 hits for "tray"). Likely staging trays, not a game element → `docs/open_questions.md` Q-003.
- Correction: an early quick scan reported 11 "orphan fastener" roots. That was a parser bug
  (names such as `#10-32` look like references); the real file has a single root.

### Robot CAD — `3200-2627-0004.zip` → `3200-2627-0004.step`
- Autodesk Translation Framework v15.15 export, STEP AP214, 2026-09-09; units **millimetres**
  (all 378 geometric contexts); 4,314,214 entities (≈ 3.1 M points, 50 k faces), 420 MB.
- One root `3200-2627-0004`, depth 5, 149 unique products, 1,392 links, 1,174 leaf occurrences.
- Motors: 6 × goBILDA 5203 Yellow Jacket bodies with 5103 gearboxes; labels 5 × `-0019`
  (19.2:1) and 1 × `-0051` (50.9:1).
- Servos: 3 × 2000 Series (2 × `2000-0025-0003` speed, 1 × `2000-0025-0002` torque).
- Mecanum wheels: 2 × `3625-0100-0104` (left slant) + 2 × `3625-0001-0104` (right slant)
  (rollers modelled as `RollerCore`/`RollerTire`/`Pin` × 44).
- Intake: 16 × `3618-4008-0016` rollers; 7 × 48 mm and 2 × 72 mm Gecko wheels; Hogback
  flywheel `3626-0014-0096`; battery `3100-0012-0020`; gridplates A–H; zip ties A–S.
- **Quality issues:**
  - Generic sub-part names (`IR`/`OR` × 99, `PATCH` × 54, `Shim` × 88, `PLATE`, `Pin`, `Body`):
    they are bearing races and sub-bodies; parent names identify them.
  - 88 occurrences of a `1600-0307-0003 (Dont Have)` placeholder product.
  - **Launcher gear ratio conflict:** CAD models all 6 motors with gearboxes (launcher at 19.2:1),
    but instructions step 38 convert the launcher motor to 1:1 and the example code's
    "1250 tps = 2678 RPM" comment assumes 28 ticks/rev (1:1). Instructions + code win (Q-004).
  - **No Control Hub / Expansion Hub** in the CAD; no wiring anywhere (see Missing).
  - Size: heavy for the pipeline; geometry must be cached and simplified (Phase 2).

### Build instructions — `3200-2627-0004_assembly-instructions.min.pdf`
- 35 pages, PDF 1.6, mostly images with a text layer, no bookmarks. "FTC StarterBot with
  Mecanum Wheels (for BIOBUZZ)": FTC Starter Kit 3200-4008-2627 + Strafer chassis 3209-0001-0007.
- BOM p2–4; gridplate cut lines p5–7; steps 1–57.
- Facts for the robot YAML: mecanum orientation (step 34, rollers form an X seen from above),
  miter-gear right-angle drive, intake 16 rollers on a 264 mm shaft (step 6), 50.9:1 intake-side
  motor (step 18), 7 × 48 mm Gecko wheels on a 240 mm shaft with 100T/20T gears (steps 24–28),
  speed servos with 72 mm Geckos (steps 32–33), launcher motor converted to 1:1 (step 38),
  Hogback flywheel on a Hyper Hub (steps 51, 55), windmill torque servo (step 52).
- **Missing:** page 35 says "next up is wiring and programming" — no wiring or ports.

### Example code — `3200-2627-0004_example-code.zip`
- One file, `BioBuzzStarterbotTeleopMecanum.java` (13.9 kB, UTF-8, CRLF), MIT license,
  © 2026 Base 10 Assets, LLC. `@TeleOp(name = "Mec BioBuzz StarterBot Teleop")`, iterative `OpMode`.
- Hardware names (`:104–112`): `left_front_drive`, `right_front_drive`, `left_back_drive`,
  `right_back_drive`, `intake` (DcMotor), `launcher` (DcMotorEx), `windmillServo`,
  `left_intake_servo`, `right_intake_servo` (CRServo).
- Directions (`:121–124`, `:160–161`), BRAKE zero-power behaviour (`:131–135`), launcher
  RUN_USING_ENCODER with PIDF (40, 0, 0, 12.5) (`:145–147`), target 1250 / min 1200 ticks/s
  (`:76–77`), mecanum mixing and controls (`:198`, `:212`).
- **Missing:** no Gradle files (SDK version unknown → we use the latest FtcRobotController,
  decision D-004) and no Autonomous (we write a simple one in Phase 6, D-005).

### Field Reset Guide — `Field Reset Guide V26-27.1.pdf`
- 1 page, 2026-09-30. Pre-match setup: 4 POLLEN per FLOWER, 8 POLLEN in each ALLIANCE AREA,
  4 POLLEN in each GARDEN along the perimeter, NECTAR placement, with a diagram. Source for the
  initial element positions (Phase 1 rules, Phase 4/10 match setup).

### Event Field Setup Guide / Initial Field Element Assembly Guide
- 30 and 42 pages, V1.0, 2026-09-12, text + photos, 45/61 bookmarks.
- How Hives, Flowers and the Frame are built and placed. Used to understand the Hive pivot and
  damper and to check the CAD-derived joints (Phase 3). Not a source of new rules.

### Field Acceptance Checklist / Field Mitigation Guide
- 2 pages each (V26-27.2 dated 2026-10-16 and V26-27.1). Checklist gives the checks a real field
  must pass (useful as validation targets in Phase 2/3). The Mitigation Guide is background.

### AprilTag printables and production art
- `PrintableAprilTag_A4/Letter.pdf`: 8 pages each (BA/BS/RA/RS = Blue/Red × Audience/Scoring,
  left and right halves), 4 tag images per page, with dimensions: 2.75 in (7.0 cm),
  6.5 in (16.5 cm), 7.0 in (17.8 cm), alignment to the Hive reference holes.
- `BIOBUZZAprilTagProduction.pdf`: 4 pages, 4 images each, no text.
- The tag images are the only complete source of the tag IDs (manual text is broken) →
  decoded in Phase 1, used as textures and ground truth in Phase 9.

## Missing (needed later)

| Item | Needed in | Plan |
|---|---|---|
| Control Hub / Expansion Hub pose, wiring, port map | 5, 6, 9 | User: "put them somewhere sensible" → plausible values marked `ASSUMED` (D-006) |
| FtcRobotController SDK version of the example | 6 | Latest official release (v12.0 at time of writing), re-checked in Phase 6 (D-004) |
| Example Autonomous | 6 | Write a simple one (D-005) |
| Physical properties: pollen/nectar mass and bounce, tile friction, damper force curve | 3, 4 | Manual first; otherwise `ASSUMED`, calibrate in Phase 11 |
| Real robot measurements (speed, mass, wheel slip) | 11 | User measures during calibration |
| Complete AprilTag ID list | 1, 9 | Decode from the printable PDFs |

## Licensing notes
- The manual, field CAD, AprilTag art and goBILDA CAD/instructions are third-party material.
  They are not committed and not shipped in the app until the user confirms we may (spec 00).
- The example code is MIT licensed (keep its notice if redistributed).
