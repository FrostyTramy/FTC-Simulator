# 05 — Robots

## Principle
A robot = generated CAD + one YAML file. No robot-specific code. The YAML is
proposed by you from evidence and reviewed by me.

## Evidence sources for the goBILDA starter robot
Use all of them, cross-check, and report contradictions:
1. **STEP** → geometry, part names, positions, `detected.yaml` (wheels,
   motors, servos, joints, gear pairs).
2. **Build instructions** → which motor drives what, gear/belt ratios,
   servo models and modes, wheel types and orientation, mechanism ranges,
   hub mounting, port wiring if shown.
3. **Example code** → hardwareMap names, device types (DcMotor, DcMotorEx,
   Servo, CRServo, IMU, sensors), motor directions (`setDirection`), modes,
   expected ranges (servo positions used, encoder targets), IMU orientation
   (`RevHubOrientationOnRobot`), drive math (which stick does what).
   Parse it automatically (find `hardwareMap.get(...)` /
   `hardwareMap.<type>.get(...)` calls, `setDirection`, `setMode`,
   `setZeroPowerBehavior`, servo `setPosition` constants) and list results
   with file:line references.

## Robot YAML schema (`config/robots/<name>.yaml`)
```yaml
version: 1
name: gobilda_starter
display_name: "goBILDA Starter Bot"
cad: generated/gobilda_starter
mass_kg: auto            # or measured value
start_size_check: true   # verify it fits the manual's starting size limit
hubs:
  - id: control_hub
    type: control_hub
    part: "Control Hub"
    imu: { model: BHI260AP, orientation: { logo: UP, usb: FORWARD } }  # from code
  - id: expansion_hub
    type: expansion_hub
    optional: true
drivetrain:
  type: mecanum          # mecanum | tank | omni_x | swerve
  wheels:
    - { id: fl, part: "Mecanum Wheel FL", radius_m: auto, roller_handedness: auto,
        motor: front_left }
    # ...
devices:
  - name: front_left             # EXACT hardwareMap name from the example code
    type: motor
    model: gobilda_5203_19.2     # from instructions/CAD
    hub: control_hub
    port: 0
    drives: wheel:fl
    gearing_extra: 1.0           # belts/gears after the motor
    source: "instructions p.12; TeleOp.java:34"
  - name: arm
    type: motor
    model: gobilda_5203_50.9
    drives: joint:arm_pivot
    gearing_extra: 5.0           # ASSUMED, check
  - name: claw
    type: servo
    model: gobilda_2000_torque
    drives: joint:claw_hinge
    mapping: { position_0: -30deg, position_1: 60deg }  # ASSUMED
  - name: imu
    type: imu
    hub: control_hub
joints:
  - { id: arm_pivot, type: hinge, body_a: chassis, body_b: arm, axis: auto,
      limits: { min_deg: -10, max_deg: 120 }, source: "..." }
sensors:
  - { name: webcam, type: webcam, model: logitech_c270, part: "Webcam",
      pose: auto, resolution: [640, 480], fov_deg: ASSUMED }
intakes:
  - { id: roller_intake, part: "Intake Roller", surface_speed_from: device:intake }
bumpers: []
```
- Unknown/uncertain fields: `ASSUMED`. The Phase 5 review lists them all.
- Schema validated (JSON Schema). Loader errors show the YAML line.

## Hubs and ports (realistic constraints)
- Control Hub / Expansion Hub: 4 motor ports, 6 servo ports, 8 digital, 4
  analog, 4 I2C buses each. Enforce port limits and duplicate names.
- Optional hardware-config screen identical in spirit to the Robot Controller
  "Configure Robot" screen; saved as an XML like the real one so teams can
  import their real robot configuration file if they have it.
- Device type mismatches produce the same kind of error as the real SDK
  (e.g. asking for a Servo where a motor is configured).

## Robot library (default robots)
1. **goBILDA Starter Bot** — from my STEP + instructions + example code.
   The reference robot; must be the most accurate.
2. **Basic mecanum chassis** — derived from the starter bot, no mechanisms,
   for learning.
3. **Turret shooter** — later, from our team robot CAD (I'll provide it).
4. **Tank-drive beginner bot** and **defense pusher** — later, simple.
Each robot has a thumbnail, description, and a bundled sample OpMode set.

## Robot import for other robots (later phase)
In-app wizard: pick STEP → run pipeline → show detected wheels/motors/joints
→ user confirms/edits in a form (with 3D highlighting of parts) → choose
hardwareMap names → saves YAML. Users never need to write YAML by hand, but
can.

## Robot checks
- Starting size inside the manual's sizing limit; expansion limits tracked
  during the match (for fouls).
- Weight and CoG report.
- Motor/servo count vs rules (if the manual limits them).
