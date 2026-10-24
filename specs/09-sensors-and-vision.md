# 09 — Sensors and Vision

All sensor parameters in `config/sensors.yaml`, each with a source or
`ASSUMED`. Every sensor has: update rate, latency, noise model, failure
modes (for fault injection), and mounting pose from the robot YAML.

## Sensor models
- **Encoders**: integer ticks, quantization, counts per rev from motor model;
  optional slip of dead wheels on fast turns.
- **Dead-wheel odometry pods** (goBILDA swingarm/4-bar pods): spring-loaded
  wheel contact, slip at high acceleration, mounting offsets.
- **goBILDA Pinpoint**: fuses two pods + internal IMU, outputs pose/velocity
  at its rate; replicate its API (offsets, encoder resolution, directions,
  reset, status).
- **SparkFun OTOS**: optical tracking on the floor; scale and angular
  scalar calibration; drift; fails if lifted (tilted robot).
- **IMU (BHI260AP)**: yaw/pitch/roll from true orientation + bias drift +
  white noise; output rate; correct axes for the hub orientation.
- **Color sensors (REV V3 / others)**: compute RGB/alpha + "distance" from the
  rendered/known material color and distance of the nearest surface in a cone;
  gain; ambient light influence; detect pollen color reliably only within
  real range (a few cm, ASSUMED).
- **Distance sensors (REV 2m ToF)**: raycast cone, range limits, noise,
  surface reflectivity effects (simplified), invalid readings outside range.
- **Touch/limit switches, magnetic limit switches**: contact/proximity
  triggers on joint positions or geometry.
- **Analog inputs** (e.g. Axon servo feedback, potentiometers): voltage with
  noise.
- **Voltage sensor**: battery model output.
- **Current sensing**: motor currents from the motor model.

## Robot cameras
- Each webcam = a Godot camera on the robot at its CAD/YAML pose, rendered to
  an off-screen viewport at the configured resolution and FPS (e.g. 640×480 @
  30 fps), independent of the screen frame rate.
- Realism: lens field of view and distortion (camera intrinsics fx, fy, cx,
  cy and distortion coefficients from a known calibration for common webcams,
  ASSUMED otherwise), exposure/gain, motion blur from robot motion, rolling
  shutter (optional), noise, auto white balance, latency (frame timestamp vs
  sim time), dropped frames under load.
- Frames passed to the JVM via shared memory; the shim's `VisionPortal`
  delivers them to processors as OpenCV `Mat`s, like on the robot.
- Camera stream preview in the DS panel (like the real camera stream).
- Performance: render camera viewports at lower priority, reuse buffers;
  measure cost per camera.

## AprilTags
- Field tags (positions, IDs, sizes, family) from the manual / season tag
  library; placed on the field from the field STEP if modeled, else from the
  manual's field drawings. Validate positions against `AprilTagLibrary` of the
  season.
- Tag textures rendered sharp enough to be detectable at realistic distances
  (texture size, mipmaps, anisotropic filtering).
- Detection: run the real AprilTag algorithm on the rendered frames (native
  AprilTag C library via JNI built for Windows x64, or a maintained Java
  port; decide and document). Same outputs as `AprilTagDetection`
  (ftcPose, robotPose with camera offset, decision margin, metadata).
- Test: place robot at known poses, compare detected robot pose with ground
  truth; report error vs distance/angle.

## Limelight 3A (if used by teams)
- Emulate the `Limelight3A` API: pipelines (AprilTag/fiducial, color), results
  (tx, ty, ta, botpose), update rate, latency. Implemented on top of the same
  rendered frames with a second camera at the Limelight's pose and FOV.

## Ground truth and debugging
- Ground-truth pose stream for every robot (never visible to robot code).
- Debug overlay: sensor rays/cones, camera frustums, detected tags, IMU axes.
- Analytics: estimated pose (from user telemetry/Dashboard) vs ground truth
  plotted over time.

## Fault injection (dev/practice menu and scenario files)
Unplugged encoder (reads 0), reversed encoder, stuck servo, noisy IMU, IMU
reset mid-match, camera disconnect, I2C sensor timeout, motor port dead,
brownout, loose wheel (lower friction), battery low. Each mimics how real
hardware fails so teams can test defensive code.
