using Sim.Core.Config;

namespace Sim.Sensors;

/// <summary>goBILDA Pinpoint parameters (config/sensors.yaml pinpoint.*). Angles in radians.</summary>
public sealed record PinpointModel(
    int LoopTimeUs,
    int DeviceId,
    int DeviceVersion,
    double CalibrationTime,
    double PowerOnTicksPerMm,
    double PowerOnYawScalar,
    double GyroNoiseDensity,
    double GyroBiasResidual,
    double VelocityFilter);

/// <summary>SparkFun OTOS parameters (config/sensors.yaml otos.*). Angles in radians.</summary>
public sealed record OtosModel(
    int ProductId,
    int HwVersion,
    int FwVersion,
    double ImuSampleTime,
    int PowerOnSamples,
    double MaxSpeed,
    double MaxTilt,
    double LinearError,
    double AngularError,
    double GyroNoiseDensity,
    double PositionNoise);

/// <summary>REV Color Sensor V3 parameters (config/sensors.yaml color.*).</summary>
public sealed record ColorModel(
    int DeviceId,
    double MaxDistance,
    double ConeHalfAngle,
    double FullScaleCounts,
    double WhiteTargetCounts,
    double AmbientCounts,
    double Noise);

/// <summary>REV 2m Distance Sensor parameters (config/sensors.yaml distance.*).</summary>
public sealed record DistanceModel(
    int ModelId,
    double MinRange,
    double MaxRange,
    double OutOfRangeMm,
    double ConeHalfAngle,
    double Noise,
    double UpdatePeriod);

/// <summary>Touch sensor parameters (config/sensors.yaml touch.*).</summary>
public sealed record TouchModel(double Travel);

/// <summary>Webcam parameters (config/sensors.yaml webcam.*): pinhole intrinsics in pixels, timing in seconds.</summary>
public sealed record WebcamModel(
    int Width,
    int Height,
    double Fx,
    double Fy,
    double Cx,
    double Cy,
    double[] Distortion,
    double Fps,
    double Latency);

/// <summary>All sensor models of config/sensors.yaml (spec 09: every value with a source or ASSUMED).</summary>
public sealed class SensorModels
{
    private const double Deg = Math.PI / 180;

    public required IReadOnlyDictionary<string, PinpointModel> Pinpoints { get; init; }
    public required IReadOnlyDictionary<string, OtosModel> Otos { get; init; }
    public required IReadOnlyDictionary<string, ColorModel> Colors { get; init; }
    public required IReadOnlyDictionary<string, DistanceModel> Distances { get; init; }
    public required IReadOnlyDictionary<string, TouchModel> Touches { get; init; }
    public required IReadOnlyDictionary<string, WebcamModel> Webcams { get; init; }

    /// <summary>Encoder counts per millimetre of wheel travel per pod type (GoBildaOdometryPods names).</summary>
    public required IReadOnlyDictionary<string, double> PodTicksPerMm { get; init; }

    /// <summary>Chassis tilt beyond which odometry pods lose the floor, radians.</summary>
    public required double PodLiftTilt { get; init; }

    public static SensorModels Load(string? path = null)
    {
        path ??= RepoPaths.Find(Path.Combine("config", "sensors.yaml"));
        var root = YamlObjects.Load(path);
        var pods = YamlObjects.Map(YamlObjects.Child(root, "odometry_pods", "sensors"), "odometry_pods");
        return new SensorModels
        {
            Pinpoints = Section(root, "pinpoint", (m, p) => new PinpointModel(
                (int)Q(m, p, "loop_time", "us"), (int)Q(m, p, "device_id"), (int)Q(m, p, "device_version"),
                Q(m, p, "calibration_time", "s"), Q(m, p, "power_on_ticks_per_mm", "ticks/mm"), Q(m, p, "power_on_yaw_scalar"),
                Q(m, p, "gyro_noise_density", "deg/s/sqrt(Hz)") * Deg, Q(m, p, "gyro_bias_residual", "deg/s") * Deg,
                Q(m, p, "velocity_filter", "ms") / 1000)),
            Otos = Section(root, "otos", (m, p) => new OtosModel(
                (int)Q(m, p, "product_id"), (int)Q(m, p, "hw_version"), (int)Q(m, p, "fw_version"),
                Q(m, p, "imu_calibration_sample_time", "ms") / 1000, (int)Q(m, p, "power_on_calibration_samples"),
                Q(m, p, "max_speed", "m/s"), Q(m, p, "max_tilt", "deg") * Deg, Q(m, p, "linear_error"), Q(m, p, "angular_error"),
                Q(m, p, "gyro_noise_density", "deg/s/sqrt(Hz)") * Deg, Q(m, p, "position_noise", "m"))),
            Colors = Section(root, "color", (m, p) => new ColorModel(
                (int)Q(m, p, "device_id"), Q(m, p, "max_distance", "m"), Q(m, p, "cone_half_angle", "deg") * Deg,
                Q(m, p, "full_scale_counts", "counts"), Q(m, p, "white_target_counts", "counts"), Q(m, p, "ambient_counts", "counts"),
                Q(m, p, "noise", "ratio"))),
            Distances = Section(root, "distance", (m, p) => new DistanceModel(
                (int)Q(m, p, "model_id"), Q(m, p, "min_range", "m"), Q(m, p, "max_range", "m"), Q(m, p, "out_of_range_mm", "mm"),
                Q(m, p, "cone_half_angle", "deg") * Deg, Q(m, p, "noise", "ratio"), Q(m, p, "update_period", "ms") / 1000)),
            Touches = Section(root, "touch", (m, p) => new TouchModel(Q(m, p, "travel", "m"))),
            Webcams = Section(root, "webcam", (m, p) =>
            {
                var res = YamlObjects.Doubles(YamlObjects.QuantityValue(YamlObjects.Child(m, "resolution", p), p + ".resolution", "px"), p);
                var f = YamlObjects.Doubles(YamlObjects.QuantityValue(YamlObjects.Child(m, "focal_length", p), p + ".focal_length", "px"), p);
                var c = YamlObjects.Doubles(YamlObjects.QuantityValue(YamlObjects.Child(m, "principal_point", p), p + ".principal_point", "px"), p);
                var d = YamlObjects.Doubles(YamlObjects.QuantityValue(YamlObjects.Child(m, "distortion", p), p + ".distortion"), p);
                return new WebcamModel((int)res[0], (int)res[1], f[0], f[1], c[0], c[1], d, Q(m, p, "fps", "Hz"), Q(m, p, "latency", "ms") / 1000);
            }),
            PodTicksPerMm = pods.Where(kv => kv.Value is IReadOnlyDictionary<string, object?> pod && pod.ContainsKey("ticks_per_mm"))
                .ToDictionary(kv => kv.Key, kv => Q(kv.Value, "odometry_pods." + kv.Key, "ticks_per_mm", "ticks/mm")),
            PodLiftTilt = YamlObjects.Double(YamlObjects.QuantityValue(pods["lift_tilt"], "odometry_pods.lift_tilt", "deg"), "lift_tilt") * Deg,
        };
    }

    private static IReadOnlyDictionary<string, T> Section<T>(object? root, string key, Func<object?, string, T> read) =>
        YamlObjects.Map(YamlObjects.Child(root, key, "sensors"), key)
            .ToDictionary(kv => kv.Key, kv => read(kv.Value, key + "." + kv.Key));

    private static double Q(object? model, string path, string key, string? unit = null) =>
        YamlObjects.Double(YamlObjects.QuantityValue(YamlObjects.Child(model, key, path), path + "." + key, unit), path + "." + key);
}
