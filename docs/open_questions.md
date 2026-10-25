# Open questions

Questions for the user. Each has an ID, the phase that needs the answer, and a status.

| ID | Phase | Question | Status |
|---|---|---|---|
| Q-001 | 2 | Godot (both builds) hangs at process start. Symantec Endpoint Protection is active on the laptop and is the likely cause (programs held before they load). Please add an exception for the Godot folder (or allow it in Symantec), or tell me if the PC is managed by someone else. | workaround: each Godot launch waits ~10 min for the scanner; an exclusion is still needed for fast Godot work |
| Q-002 | 1 | AprilTag IDs missing from the manual text. | resolved: 30–45, read from page image and decoded from the printable PDFs |
| Q-003 | 1–2 | 2 × `am-5706 Artifact Tray` in the field CAD. | resolved: off-field holders of the 5 reserve NECTAR per alliance (Fig 10-2, user: "it has 2 artifacts, pollen and nectar"); confirm position in Phase 2 |
| Q-004 | 5 | Robot CAD launcher motor 19.2:1 vs instructions/code 1:1. | planned: use 1:1 |
| Q-005 | 3 | Physical properties not in the manual (ball mass/bounce, tile friction, damper curve). | planned: `ASSUMED`, calibrate in Phase 11 |
| Q-006 | 17 | May the shipped app contain the field CAD, AprilTag art and goBILDA CAD (converted meshes)? | open (needed only for release) |
| Q-007 | 10 | Human player in the sim: NECTAR entry by a key/button press by the driver, or automatic as soon as allowed? | planned: both (manual button + "auto human player" option) |
| Q-008 | 11 | After STOP the SDK sends the REV hub a failsafe command every 100 ms (SDK bytecode). What the firmware does then is not documented: the sim turns motor outputs off (motors coast, BRAKE no longer holds) and servos go limp. On your robot: after STOP, does a BRAKE drivetrain or arm hold or coast? | planned: ASSUMED coast; check on the real robot in Phase 11 |
