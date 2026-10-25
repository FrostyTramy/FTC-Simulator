# SDK 12.0.0 vision facts (javap of generated/sdk/12.0.0/Vision-12.0.0, for Phase 9)

- `AprilTagGameDatabase.getCurrentGameTagLibrary()` returns `getBioBuzzTagLibrary()`.
- BIOBUZZ library = 4 `AprilTagClusterMetadata` built by `getBioBuzzCluster(firstId)`:
  - `RED SCORING` (30–33), `RED AUDIENCE` (34–37), `BLUE AUDIENCE` (38–41), `BLUE SCORING` (42–45);
  - cluster `fieldPosition` (0, 0, 0) in, `fieldOrientation` identity: the hives tilt, so the cluster pose is not
    fixed on the field;
  - members (id offset 0..3) at (−6.5, 7.1874, −5.622), (−2.75, …), (2.75, …), (6.5, 7.1874, −5.622) in relative to
    the cluster origin, tag size 3.25 in.
- `AprilTagDetection` is abstract (`ftcPose`, `rawPose`, `robotPose` (Pose3D), `frameAcquisitionNanoTime`,
  `distanceUnit`); subclasses `AprilTagSingleDetection` and `AprilTagClusterDetection`; the processor keeps a
  `TagClusterNotebook` and solves cluster poses from several tags (`AprilTagProcessor.PoseSolver`).
- Pinpoint: `com.qualcomm.hardware.gobilda.GoBildaPinpointDriver` (+ `DeviceStatus`, `EncoderDirection`,
  `GoBildaOdometryPods`, `ReadData`, `Register`); OTOS: `com.qualcomm.hardware.sparkfun.SparkFunOTOS` (+ `Pose2D`,
  `Status`, `SignalProcessConfig`, `SelfTestConfig`, `Version`). Both ship in Hardware-12.0.0.
