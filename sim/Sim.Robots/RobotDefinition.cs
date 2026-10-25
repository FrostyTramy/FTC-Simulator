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
    double SpeedRatio);

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
    double? ContactStiffness = null);

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

    public static RobotDefinition Load(string path)
    {
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(path))
            ?? throw new RobotConfigException($"{path} is empty");
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
                d.TryGetValue("speed_ratio", out var ratio) ? Num(ratio) : 1.0);
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
                    m.ContainsKey("contact_stiffness") ? Q(m, "contact_stiffness") : null);
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
        };
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
