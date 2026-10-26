using System.Globalization;
using System.Numerics;
using YamlDotNet.Serialization;

namespace Sim.Robots;

/// <summary>Raised for a missing or malformed value in a robot YAML.</summary>
public sealed class RobotConfigException(string message) : Exception(message);

/// <summary>One drive wheel (robot frame: +X right, +Y up, −Z forward, metres).</summary>
/// <param name="Slant">"left" or "right" roller slant (mecanum).</param>
/// <param name="DriveSign">+1 if positive motor output rotation drives the wheel forward, −1 if mirrored.</param>
public sealed record WheelDef(string Id, string Motor, Vector3 Position, string Slant, int DriveSign);

/// <summary>One hardware device (exact hardwareMap name from the robot's code).</summary>
public sealed record DeviceDef(
    string Name,
    string Type,
    string SdkClass,
    string? Model,
    string Hub,
    int? Port,
    string? Drives,
    string Direction,
    string ZeroPower,
    string? RunMode,
    double[]? Pidf,
    double SpeedRatio)
{
    /// <summary>
    /// Encoder counts per output-shaft revolution when the port's encoder input is not the motor's own encoder (e.g. a
    /// REV Through Bore Encoder geared to the mechanism), already referred to the motor output shaft; null = the motor's.
    /// </summary>
    public double? EncoderCountsPerOutputRev { get; init; }
}

/// <summary>One REV hub. Index in <see cref="RobotDefinition.Hubs"/> = SDK hub index (0 = Control Hub).
/// ImuLogo/ImuUsb: how the hub (and its IMU) is mounted, RevHubOrientationOnRobot terms (UP, FORWARD, ...).</summary>
public sealed record HubDef(string Id, string Type, string ImuLogo, string ImuUsb);

/// <summary>
/// A moving mechanism (robot YAML mechanisms). Inertia is about its own axis, kg·m² (null if not given); the axis
/// line (robot frame) is null for mechanisms that do not rotate. DriveSign: rotation about Axis for a positive raw
/// output of the driving device. Radius/Width/ContactMaterial describe a roller, wheel or flywheel surface.
/// </summary>
public sealed record MechanismDef(
    string Id,
    string Type,
    double? InertiaKgM2,
    Vector3? AxisPoint,
    Vector3? Axis,
    int DriveSign = 1,
    double? Radius = null,
    double? Width = null,
    string? ContactMaterial = null,
    double? ContactStiffness = null)
{
    /// <summary>Mechanism this one rides on (e.g. a shooter on a turret): its pivot moves with that mechanism.</summary>
    public string? MountedOn { get; init; }

    /// <summary>Hard stops of the mechanism angle about <see cref="Axis"/> from its CAD pose, degrees (min, max); null = free.</summary>
    public (double Min, double Max)? LimitsDeg { get; init; }

    /// <summary>Servo angle (degrees from the servo centre) at which the CAD shows a servo-driven arm.</summary>
    public double CadServoAngleDeg { get; init; }

    /// <summary>
    /// Device that turns this mechanism as well as the one it <c>drives</c> (chain or gear followers, e.g. transfer
    /// stages on the transfer motor); <see cref="FollowRatio"/> = mechanism speed / device output speed.
    /// </summary>
    public string? DrivenBy { get; init; }

    public double FollowRatio { get; init; } = 1;

    /// <summary>
    /// A servo arm that rests on the floor when swung down (<see cref="FloorStop"/>): how far its lowest point presses
    /// into the floor at the stop, m; null = no floor stop.
    /// </summary>
    public double? FloorStopPenetration { get; init; }
}

/// <summary>
/// One sensor (robot YAML <c>sensors:</c>, spec 05/09): exact hardwareMap name, kind (pinpoint, otos, color, distance,
/// touch, webcam), model key in config/sensors.yaml, SDK class, hub and I2C bus or digital port, and its mounting in the
/// robot frame: <see cref="Forward"/> is the sensing direction (camera optical axis; the device +X for odometry
/// computers), <see cref="Up"/> the device's up. <see cref="Params"/> keeps the other keys as raw YAML values.
/// </summary>
public sealed record SensorDef(
    string Name,
    string Type,
    string Model,
    string SdkClass,
    string Hub,
    int Port,
    Vector3 Position,
    Vector3 Forward,
    Vector3 Up,
    IReadOnlyDictionary<string, object> Params);

/// <summary>Typed view of config/robots/&lt;name&gt;.yaml (spec 05) for the simulation.</summary>
public sealed class RobotDefinition
{
    public required string Name { get; init; }
    public required string DisplayName { get; init; }
    public required double MassKg { get; init; }
    public required Vector3 CenterOfMass { get; init; }

    /// <summary>Principal moments (Ixx, Iyy = yaw, Izz) about the centre of mass, kg·m².</summary>
    public required Vector3 Inertia { get; init; }

    public required string DrivetrainType { get; init; }
    public required double WheelRadius { get; init; }
    public required double DriveGearing { get; init; }
    public required double WheelMassKg { get; init; }
    public required IReadOnlyList<WheelDef> Wheels { get; init; }
    public required IReadOnlyList<DeviceDef> Devices { get; init; }
    public required IReadOnlyList<HubDef> Hubs { get; init; }
    public required IReadOnlyList<MechanismDef> Mechanisms { get; init; }
    public required string BatteryModel { get; init; }

    /// <summary>Row-major 4×4 matrix from CAD coordinates to the robot frame (robot = M · cad), frame.cad_to_robot.</summary>
    public required double[] CadToRobot { get; init; }

    /// <summary>Guided element path (ball_path.model = guided), or null when elements only move by contact.</summary>
    public BallPathDef? BallPath { get; init; }

    /// <summary>Sensors and cameras (robot YAML sensors).</summary>
    public IReadOnlyList<SensorDef> Sensors { get; init; } = [];

    /// <summary>Folder of the CAD pipeline output, relative to the repository (cad value, e.g. generated/gobilda_starter).</summary>
    public string CadPath { get; init; } = "";

    /// <summary>Overall bounding box (width x, height y, depth z) from the YAML <c>dimensions.bbox_size</c>, m.</summary>
    public Vector3 BboxSize { get; init; }

    public DeviceDef Device(string name) =>
        Devices.FirstOrDefault(d => d.Name == name) ?? throw new RobotConfigException($"{Name}: no device '{name}'");

    /// <summary>SDK hub index of a hub id ("control_hub" → 0).</summary>
    public int HubIndex(string hubId)
    {
        for (var i = 0; i < Hubs.Count; i++)
        {
            if (Hubs[i].Id == hubId)
            {
                return i;
            }
        }
        throw new RobotConfigException($"{Name}: no hub '{hubId}'");
    }

    public MechanismDef Mechanism(string id) =>
        Mechanisms.FirstOrDefault(m => m.Id == id) ?? throw new RobotConfigException($"{Name}: no mechanism '{id}'");

    /// <summary>
    /// Loads a robot file. A variant file names a <c>base</c> robot in the same folder and replaces whole top-level
    /// sections of it (e.g. the same robot with sensors added).
    /// </summary>
    public static RobotDefinition Load(string path)
    {
        var root = LoadRaw(path, 0);
        var mass = Map(root, "mass");
        var drive = Map(root, "drivetrain");
        var wheels = ((List<object>)drive["wheels"]).Select(o =>
        {
            var w = (Dictionary<object, object>)o;
            return new WheelDef(Str(w, "id"), Str(w, "motor"), Vec(w["position"]), Str(w, "slant"), (int)Num(w["drive_sign"]));
        }).ToList();
        var devices = ((List<object>)root["devices"]).Select(o =>
        {
            var d = (Dictionary<object, object>)o;
            return new DeviceDef(
                Str(d, "name"), Str(d, "type"), Str(d, "sdk_class"),
                d.TryGetValue("model", out var model) ? model.ToString() : null,
                Str(d, "hub"),
                d.TryGetValue("port", out var port) ? (int)Num(port) : null,
                d.TryGetValue("drives", out var drives) ? drives.ToString() : null,
                d.TryGetValue("direction", out var dir) ? dir.ToString()! : "FORWARD",
                d.TryGetValue("zero_power", out var zp) ? zp.ToString()! : "FLOAT",
                d.TryGetValue("run_mode", out var rm) ? rm.ToString() : null,
                d.TryGetValue("pidf", out var pidf) ? ((List<object>)pidf).Select(Num).ToArray() : null,
                d.TryGetValue("speed_ratio", out var ratio) ? Num(ratio) : 1.0)
            {
                EncoderCountsPerOutputRev = d.ContainsKey("encoder_counts_per_output_rev") ? Q(d, "encoder_counts_per_output_rev") : null,
            };
        }).ToList();
        var hubs = ((List<object>)root["hubs"]).Select(o =>
        {
            var h = (Dictionary<object, object>)o;
            var mount = (Dictionary<object, object>)QRaw(h, "pose");
            if (h.TryGetValue("imu", out var imu) && imu is Dictionary<object, object> imuQ
                && imuQ["value"] is Dictionary<object, object> imuV && imuV.TryGetValue("orientation", out var o2))
            {
                mount = (Dictionary<object, object>)o2;   // the IMU's own mounting, when given
            }
            return new HubDef(Str(h, "id"), Str(h, "type"), Str(mount, "logo"), Str(mount, "usb"));
        }).ToList();
        if (hubs.Count == 0 || hubs[0].Type != "control_hub")
        {
            throw new RobotConfigException("the first hub must be the Control Hub (SDK hub index 0)");
        }
        var mechanisms = root.TryGetValue("mechanisms", out var mechs) && mechs is List<object> mechList
            ? mechList.Select(o =>
            {
                var m = (Dictionary<object, object>)o;
                return new MechanismDef(Str(m, "id"), Str(m, "type"), m.ContainsKey("inertia") ? Q(m, "inertia") : null,
                    m.ContainsKey("axis_point") ? Vec(QRaw(m, "axis_point")) : null, m.ContainsKey("axis") ? Vec(QRaw(m, "axis")) : null,
                    m.ContainsKey("drive_sign") ? (int)Q(m, "drive_sign") : 1,
                    m.ContainsKey("radius") ? Q(m, "radius") : null,
                    m.ContainsKey("width") ? Q(m, "width") : m.ContainsKey("length") ? Q(m, "length") : null,
                    m.ContainsKey("contact_material") ? QRaw(m, "contact_material").ToString() : null,
                    m.ContainsKey("contact_stiffness") ? Q(m, "contact_stiffness") : null)
                {
                    MountedOn = m.ContainsKey("mounted_on") ? QRaw(m, "mounted_on").ToString() : null,
                    LimitsDeg = m.ContainsKey("limits") ? Pair(QRaw(m, "limits")) : null,
                    CadServoAngleDeg = m.ContainsKey("cad_servo_angle") ? Q(m, "cad_servo_angle") : 0,
                    DrivenBy = m.ContainsKey("driven_by") ? QRaw(m, "driven_by").ToString() : null,
                    FollowRatio = m.ContainsKey("follow_ratio") ? Q(m, "follow_ratio") : 1,
                    FloorStopPenetration = m.ContainsKey("floor_stop") ? Q(m, "floor_stop") : null,
                };
            }).ToList()
            : [];
        return new RobotDefinition
        {
            Name = Str(root, "name"),
            DisplayName = Str(root, "display_name"),
            MassKg = Q(mass, "total"),
            CenterOfMass = Vec(QRaw(mass, "center_of_mass")),
            Inertia = Vec(QRaw(mass, "inertia_diagonal")),
            DrivetrainType = Str(drive, "type"),
            WheelRadius = Q(drive, "wheel_radius"),
            DriveGearing = Q(drive, "drive_gearing"),
            WheelMassKg = Q(drive, "wheel_mass"),
            Wheels = wheels,
            Devices = devices,
            Hubs = hubs,
            Mechanisms = mechanisms,
            BatteryModel = QRaw(Map(root, "battery"), "model").ToString()!,
            CadToRobot = ((List<object>)QRaw(Map(root, "frame"), "cad_to_robot")).SelectMany(row => ((List<object>)row).Select(Num)).ToArray(),
            BallPath = ReadBallPath(root),
            Sensors = ReadSensors(root),
            CadPath = QRaw(root, "cad").ToString()!,
            BboxSize = root.TryGetValue("dimensions", out var dims) ? Vec(QRaw((Dictionary<object, object>)dims, "bbox_size")) : Vector3.Zero,
        };
    }

    private static Dictionary<object, object> LoadRaw(string path, int depth)
    {
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(path))
            ?? throw new RobotConfigException($"{path} is empty");
        if (!root.TryGetValue("base", out var baseName))
        {
            return root;
        }
        if (depth > 4)
        {
            throw new RobotConfigException($"{path}: base robots nested too deeply");
        }
        var merged = LoadRaw(Path.Combine(Path.GetDirectoryName(path)!, baseName + ".yaml"), depth + 1);
        foreach (var (key, value) in root)
        {
            if (key.ToString() != "base")
            {
                merged[key] = value;
            }
        }
        return merged;
    }

    private static readonly HashSet<string> SensorKeys = ["name", "type", "model", "sdk_class", "hub", "i2c_bus", "port", "pose"];

    private static List<SensorDef> ReadSensors(Dictionary<object, object> root)
    {
        if (!root.TryGetValue("sensors", out var raw) || raw is not List<object> list)
        {
            return [];
        }
        var sensors = list.Select(o =>
        {
            var s = (Dictionary<object, object>)o;
            var name = Str(s, "name");
            var pose = (Dictionary<object, object>)QRaw(s, "pose");
            var forward = pose.TryGetValue("forward", out var f) ? Vector3.Normalize(Vec(f)) : -Vector3.UnitZ;
            var up = pose.TryGetValue("up", out var u) ? Vector3.Normalize(Vec(u)) : Vector3.UnitY;
            if (Math.Abs(Vector3.Dot(forward, up)) > 1e-3)
            {
                throw new RobotConfigException($"sensor '{name}': pose forward and up are not perpendicular");
            }
            var port = s.TryGetValue("i2c_bus", out var bus) ? (int)Num(bus) : s.TryGetValue("port", out var p) ? (int)Num(p) : 0;
            var extra = s.Where(kv => !SensorKeys.Contains(kv.Key.ToString()!)).ToDictionary(kv => kv.Key.ToString()!, kv => kv.Value);
            return new SensorDef(name, Str(s, "type"), Str(s, "model"), Str(s, "sdk_class"), Str(s, "hub"), port,
                Vec(pose["position"]), forward, up, extra);
        }).ToList();
        var devices = root.TryGetValue("devices", out var d) && d is List<object> dl
            ? dl.Select(x => ((Dictionary<object, object>)x)["name"].ToString()).ToHashSet()
            : [];
        foreach (var dup in sensors.GroupBy(x => x.Name).Where(g => g.Count() > 1 || devices.Contains(g.Key)))
        {
            throw new RobotConfigException($"hardware name '{dup.Key}' is used twice");
        }
        return sensors;
    }

    /// <summary>A sensor's extra quantity (e.g. an odometry pod) as its raw YAML value map.</summary>
    public static IReadOnlyDictionary<string, object> SensorParam(SensorDef sensor, string key)
    {
        if (!sensor.Params.TryGetValue(key, out var raw) || raw is not Dictionary<object, object> q || !q.ContainsKey("source"))
        {
            throw new RobotConfigException($"sensor '{sensor.Name}': missing '{key}' {{value, unit, source}}");
        }
        return q["value"] is Dictionary<object, object> v
            ? v.ToDictionary(kv => kv.Key.ToString()!, kv => kv.Value)
            : throw new RobotConfigException($"sensor '{sensor.Name}': '{key}' value must be a map");
    }

    /// <summary>A robot-frame vector from a raw YAML list.</summary>
    public static Vector3 VectorValue(object raw) => Vec(raw);

    private static BallPathDef? ReadBallPath(Dictionary<object, object> root)
    {
        if (!root.TryGetValue("ball_path", out var raw) || raw is not Dictionary<object, object> b
            || !b.ContainsKey("model") || QRaw(b, "model").ToString() != "guided")
        {
            return null;
        }
        var zone = (Dictionary<object, object>)QRaw(b, "capture_zone");
        HoodDef? hood = null;
        if (b.ContainsKey("hood"))
        {
            var h = (Dictionary<object, object>)QRaw(b, "hood");
            hood = new HoodDef(h["mechanism"].ToString()!, Pair(h["servo_angles"]), Pair(h["elevation"]));
        }
        SpringNipDef? spring = null;
        if (b.ContainsKey("spring_nip"))
        {
            var n = (Dictionary<object, object>)QRaw(b, "spring_nip");
            spring = new SpringNipDef(Num(n["gap"]), Num(n["travel"]), Num(n["rate"]), Num(n["preload"]), Num(n["contact_length"]));
        }
        return new BallPathDef(
            Vec(zone["min"]), Vec(zone["max"]),
            Q(b, "capture_min_surface_speed"),
            ((List<object>)QRaw(b, "storage_slots")).Select(Vec).ToList(),
            (int)Q(b, "hopper_capacity"),
            Q(b, "feed_angle"),
            (int)Q(b, "feed_power_sign"),
            Vec(QRaw(b, "launch_point")),
            Vector3.Normalize(Vec(QRaw(b, "launch_direction"))),
            Q(b, "exit_speed_ratio"),
            Q(b, "min_launch_surface_speed"))
        {
            Feeder = b.ContainsKey("feeder") ? QRaw(b, "feeder").ToString()! : "windmill",
            Launcher = b.ContainsKey("launcher") ? QRaw(b, "launcher").ToString() : null,
            Turret = b.ContainsKey("turret") ? QRaw(b, "turret").ToString() : null,
            Gate = b.ContainsKey("gate") ? GateOf((Dictionary<object, object>)QRaw(b, "gate")) : null,
            Hood = hood,
            SpringNip = spring,
        };
    }

    private static GateDef GateOf(Dictionary<object, object> g) => new(g["mechanism"].ToString()!, Pair(g["open_servo_angles"]));

    private static (double, double) Pair(object raw)
    {
        var v = ((List<object>)raw).Select(Num).ToArray();
        return v.Length == 2 ? (v[0], v[1]) : throw new RobotConfigException("expected a [min, max] pair");
    }

    public static string FindConfig(string name)
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config", "robots", $"{name}.yaml");
            if (File.Exists(candidate))
            {
                return candidate;
            }
        }
        throw new RobotConfigException($"config/robots/{name}.yaml not found above {AppContext.BaseDirectory}");
    }

    private static Dictionary<object, object> Map(Dictionary<object, object> m, string key) =>
        m.TryGetValue(key, out var v) && v is Dictionary<object, object> d ? d : throw new RobotConfigException($"missing section '{key}'");

    private static string Str(Dictionary<object, object> m, string key) =>
        m.TryGetValue(key, out var v) ? v.ToString()! : throw new RobotConfigException($"missing '{key}'");

    private static object QRaw(Dictionary<object, object> m, string key)
    {
        var q = Map(m, key);
        if (!q.ContainsKey("source"))
        {
            throw new RobotConfigException($"'{key}' has no source");
        }
        return q["value"];
    }

    private static double Q(Dictionary<object, object> m, string key) => Num(QRaw(m, key));

    private static double Num(object raw) => double.Parse(raw.ToString()!, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static Vector3 Vec(object raw)
    {
        var v = ((List<object>)raw).Select(Num).ToArray();
        return new Vector3((float)v[0], (float)v[1], (float)v[2]);
    }
}
