# FTC RoboDojo #23205 robot: evidence

Sources for `config/robots/robodojo_23205.yaml` and `config/cad/robodojo_23205.yaml`.

| Id | Source |
|---|---|
| SLIDES | `FTC RoboDojo #23205 robot.pptx` (team, 2026-10-25), slides 1-6 |
| CAD | `robot assembly.step` (team Fusion export, 244 MB, 1142 parts; Z up, front = intake side = CAD -Y) |
| USER | answers in the session of 2026-10-25 (below) |

## Slides (summary)

1. Overview render (team number 23205 plate on the right side).
2. **Intake:** 5 flexible silicone wheels. A spring (not in CAD) holds the intake down; a NECTAR forces it up, then it springs back. The yellow bar ("rampa floare") can be raised and lowered by the Axon servo. When it is down it sits right in front of the intake. It reaches into the FLOWER, and its ramp guides NECTAR from the flower down into the intake.
3. **Transfer:** 2 identical arms (stages 2 and 3) pressed down by springs. A bigger ball pushes them up, and they hold it compressed. Each arm has 2 small Gecko wheels, chain-driven by a motor.
4. A small Gecko wheel at the back pushes the balls into the turret ("Bara transfer-outtake", 32 mm Gecko).
5. A servo arm ("servo gate") blocks the balls from going into the turret.
6. **Turret:**
   - It spins on one motor underneath. There are 2 small gears: one for the motor, one for the encoder.
   - A 6000 RPM motor drives Gecko wheels plus a flywheel.
   - At the back, a spring system compresses so both ball sizes fit. NECTAR and POLLEN shoot differently because they compress differently ("try to simulate real spring").
   - A servo raises and lowers the hood.

## USER answers (2026-10-25)

- Code: none to import. The simulator writes a TeleOp with the robot's device names.
- Transfer: separate motors. The intake motor drives the 5 intake wheels. The transfer motor drives stage 2, stage 3 and the back Gecko wheel.
- Turret range: "360 but not continuous". It turns through 360° between hard limits and has to unwind the other way to get from 360° back to 0°.
- "servo camera" (front left, above the intake): not used yet. It stays in the CAD as a static part and has no device.
- Drive motors: goBILDA 5203 13.7:1 (435 RPM).
- Intake, transfer and turret motors: 5203 5.2:1 (1150 RPM). Flywheel: 6000 RPM (5203 1:1, SLIDES 6).
- Hood launch-angle range: unknown, so measure it from the CAD.
- Sensors: goBILDA Pinpoint + 2 swingarm odometry pods (CAD). No camera, no colour or distance sensors.
- Robot weight: unknown, so estimate it from the CAD (ASSUMED until weighed).

## CAD inventory (2026-10-25, OCP read)

- Overall size without the broken encoder body and the CAD POLLEN: 348 × 451 × 360 mm (x, y, z from the floor). Fits the 18 in cube (R102).
- Drivetrain: 4 × goBILDA GripForce 104 mm mecanum wheels on HTD5 44T pulley belts.
  - "Roata Stanga" (left) at CAD x = +148 mm; "Roata Drepta" (right) at x = -124 mm.
  - Front axle y = -297 mm, back axle y = -41 mm; axle height z = 6 mm, so the floor is at z = -46 mm.
  - Wheelbase 256 mm, track 272 mm, robot centre x = 12 mm.
- 2 odometry pods (32 mm omni, A-arm, spring) and a Pinpoint 3110-0002-0001.
- Intake: "bara intake" has 4 × 68 mm "Intake Wheel" at y -392, z 61 plus one 90 mm wheel at y -359, z 96. Motor "assembly motor intake" is at x 96.
- Transfer: stage 2 (2 × 48 mm Gecko, y -278 mm), stage 3 (2 × 48 mm Gecko, y -182 mm), outtake 32 mm Gecko (y -27, z 102). Motor "assembly motor transfer" at x -72, with a miter gear to "bara transmisie putere".
- CAD POLLEN on the path at (12, -344, 9), (12, -281, 46), (12, -211, 52), (12, -141, 60) mm.
- Yellow bar ("rampa floare", 254 mm wide), 2 × 1105 arms and an Axon Max with a 1908 servo hub at (135, -358, 103) mm. The pivot axis is along X.
- Gate: 2000-0025-0003 speed servo in a 1802 frame at (-62, -86, 124) mm, with an 1102 beam arm.
- Turret "Turrret V7":
  - The rotating platform sits on a ball-bearing ring.
  - Turret motor (body at z 97, 5103 single-stage gearbox) with a 24T herringbone pinion at (97, -80, 182) mm.
  - Encoder pinion (24T, LH) at (97, -50, 182) mm with a REV Through Bore Encoder (REV-11-1271).
  - Shooter: 168 mm shaft along X at (y -143, z 244) carrying 2 × 72 mm Gecko and an 85 mm "Flywheel" mass. It is driven by a 1:1 24T bevel pair from the flywheel motor (axis Y).
  - Hood: "Hood Insert (slides 0-20mm)" behind the wheels (y -43). A 10T gear ("same teeth as hood") is driven by a servo (1802 frame) on top at z 269.
  - Spring parts are named "Ciocan".

## More answers (2026-10-25)
- Hood: the servo (10T gear) turns the whole hood, including the sprung hood insert. The insert is pushed into the hood
  by a spring, with a gap at the back and 3 screws.
- At turret 0 the robot shoots toward the intake side.

## Open points for the team (ASSUMED until checked)
1. **Mecanum wheels in the CAD:** both left wheels ("Stanga") have their top rollers along the same diagonal, and so
   do both right wheels. That is not the X pattern, so the robot could not strafe. The sim uses FL/BR left slant and
   FR/BL right slant. Check the CAD or the robot.
2. **Turret encoder:** the 24T encoder pinion meshes the motor pinion (centres 30.5 mm apart), not the ring, so it
   turns the opposite way to the motor. The sim assumes it is plugged into the turret motor's port. Check which port
   it uses.
3. **Hood:** pivot on the shooter shaft and gearing 10T to an arc of about 130.6 mm radius (≈ 23° of hood travel over
   the servo's 315° SDK range); the CAD shows it fully back (vertical exit). Check the real angle range.
4. **Spring:** rate 600 N/m and preload 2 N are guesses. Measure the force needed to push the hood insert back 10 mm
   and 20 mm.
5. **Flower bar:** UP (CAD) at servo -80° and DOWN at +82°, so it swings over the top to the front. Check the real
   positions.
6. **Gate:** closed at the servo centre and open beyond +40°.
7. **Ports and gear ratios:**
   - Hub ports are guesses.
   - Intake/transfer chain ratios are 1:1.
   - Drive belt is 1:1.
8. **Mass:** 12.5 kg estimated. Weigh the robot.

