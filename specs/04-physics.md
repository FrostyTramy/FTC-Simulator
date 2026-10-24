# 04 — Physics

All constants live in `config/physics.yaml`, each with a source or `ASSUMED`.
Build a calibration workflow (below) so ASSUMED values get replaced by
measurements from the real robot and field.

## Engine setup
- Jolt in Godot, fixed 240 Hz (try 480 Hz in tests to check convergence).
  Solver iterations and substeps tuned and documented.
- Continuous collision detection on pollen, nectar and fast mechanism parts
  (flywheels, launched elements).
- Collision layers: field_static, field_dynamic, game_elements, robot_chassis,
  robot_mechanisms, intake_zones, triggers, sensors_raycast.
- Sleeping enabled for resting elements, but wake reliably on contact.
- Determinism settings verified (see architecture spec).

## Physics materials (pairwise table)
Friction (static/dynamic), restitution and rolling resistance for each pair:
foam tile, field wall (polycarbonate/aluminum), pollen plastic, nectar,
mecanum roller rubber, traction wheel rubber, compliant/gecko wheel,
aluminum, 3D-printed PLA/PETG, intake surfaces. Start ASSUMED, calibrate.

## Game elements
- **Pollen**: ~3 in plastic balls (exact diameter and mass from the manual or
  real measurement). Sphere collider, CCD, restitution and rolling resistance
  tuned so they roll realistically on foam and settle against walls and in
  corners. Optional: slight compliance (squish) for intakes using a soft
  contact / larger contact radius, since intakes rely on compression.
- **Nectar** and other elements: shape from CAD, mass from manual/measurement.
- **Air drag** on launched elements: F = ½ ρ C_d A v², C_d ≈ 0.47 (sphere,
  ASSUMED); optional Magnus effect from spin (backspin from flywheels).
- Validate: drop test, roll-out distance after a push, wall bounce, shot
  trajectories vs video.

## Drivetrains
Two levels, configurable per robot:
1. **Force model (default, stable)**: each wheel produces a force at its
   contact patch from motor torque / radius, limited by friction
   (μ × normal load). Normal load per wheel from weight distribution and
   acceleration (weight transfer). Mecanum: the roller direction defines the
   free-rolling axis (45°); force only along the driven direction, with
   roller friction losses that make strafing slower. Lateral slip modeled with
   a tire-like friction curve.
2. **Roller model (experimental)**: actual roller colliders. Only if the force
   model can't match measurements.
- Support: mecanum, omni/X-drive, tank (traction and omni center), swerve
  (later), with correct handedness detection from CAD.
- Wheel inertia and gearbox friction included.
- Validate against real robot: top speed forward/strafe/rotate, 0→top speed
  time, coast-down distance in FLOAT vs BRAKE.

## DC motor model
Per motor model in `config/motors.yaml` (e.g. goBILDA 5203 series per
gear ratio, REV HD Hex, Core Hex): free speed, stall torque, stall current,
free current, encoder counts per motor revolution (28 for many FTC motors) and
gear ratio → counts per output revolution, gearbox efficiency, rotor inertia.
Equations each step:
```
V_applied = power × V_battery (power clipped to [-1,1])
I = (V_applied − Ke·ω_motor) / R        (clamped by hub current limits if any)
τ_motor = Kt·I − friction(ω)
τ_output = τ_motor × gear_ratio × efficiency
```
- BRAKE vs FLOAT zero-power behavior (BRAKE = shorted windings: back-EMF
  braking torque; FLOAT = coast).
- Encoders produce integer ticks (quantization), velocity measured like the
  hub (ticks/s over its sample window).
- Built-in velocity/position PIDF like the hub's motor controller (default
  coefficients per SDK; see SDK shim spec), running at the hub's rate
  (ASSUMED), not at the user loop rate.
- Motor heating (optional, later): sustained stall reduces performance.

## Battery and electrical
- 12 V NiMH model: open-circuit voltage vs state of charge curve, internal
  resistance (battery + wires + connectors, ASSUMED then measured).
- V_bus = V_oc(SoC) − I_total × R_internal. All motors share it → voltage sag
  under load affects every motor at once.
- State of charge drains over the match; option to start with a "fresh",
  "used" or "tired" battery.
- Brownout: below a threshold voltage the hub/servos misbehave or reset, like
  real hardware (ASSUMED threshold, document it).
- Fuse/current-limit behavior if relevant to hubs.

## Servos
`config/servos.yaml` per model (goBILDA torque/speed/super-speed, Axon,
REV Smart Servo): range of motion (e.g. 300° for many goBILDA servos in
standard mode, ASSUMED per model), no-load speed, stall torque, deadband,
PWM range. Position servos: move at limited speed toward target, torque-
limited (can be stalled by load), small overshoot/jitter. Continuous
rotation servos: speed ∝ input with torque limit.

## Mechanisms
- **Linear slides** (e.g. goBILDA Viper): sliders with friction, string/belt
  modeled as a spring-damper, gravity, hard stops.
- **Arms/turrets**: hinge joints driven by motor through gear ratio, with
  backlash (small dead zone, ASSUMED), hard stops, cable limits for turrets
  (max rotation configurable).
- **Intakes**: rotating rollers with compliant wheels pulling elements in:
  modeled as contact surfaces with tangential velocity + compression; jams
  possible when too many elements.
- **Flywheel launchers**: flywheel inertia, motor model, contact time with the
  ball, energy transfer, RPM drop per shot and recovery time, spin
  imparted; hood angle from joint. Shot consistency depends on RPM at release.
- **Belts/chains/gears**: ratio + efficiency + optional compliance; no
  per-tooth simulation.
- Mechanism limits: max torque per joint so mechanisms can stall.

## Robot body
- Mass and inertia tensor from CAD (sum of part masses, ASSUMED densities),
  overridable by measured total mass (I will weigh the robot).
- Center of gravity shown in debug view; robots can tip if pushed/accelerated
  hard with a high CoG.
- Robot–robot contact: chassis colliders, bumpers if any, friction realistic
  for pushing matches.

## Calibration workflow
- `tools/calibration/`: scripts + in-app test OpModes that measure top speed,
  acceleration, strafe ratio, turn rate, coast-down, launcher RPM drop, ball
  bounce. I run the same OpModes on the real robot (they log to telemetry/
  file), give you the numbers, and a fitter adjusts `physics.yaml` values.
- Keep a `docs/calibration.md` table: parameter, sim value, real value, error,
  date, how measured.
