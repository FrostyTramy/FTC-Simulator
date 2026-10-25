using System.Globalization;
using YamlDotNet.Serialization;

namespace Sim.Hardware;

/// <summary>Raised when a hardware config file is missing a value or has the wrong shape.</summary>
public sealed class HardwareConfigException(string message) : Exception(message);

/// <summary>Datasheet description of a DC gear motor at its output shaft (config/motors.yaml models).</summary>
public sealed record MotorSpec(
    string Key,
    string Name,
    double GearRatio,
    double NominalVoltage,
    double FreeSpeedRadPerS,
    double FreeCurrentA,
    double StallCurrentA,
    double StallTorqueNm,
    double CountsPerOutputRev,
    double RotorInertiaKgM2,
    string SdkMotorType);

/// <summary>SDK @MotorType constants (config/motors.yaml sdk_motor_types).</summary>
public sealed record SdkMotorType(string Key, string DisplayName, double TicksPerRev, double Gearing, double MaxRpm,
    double AchievableMaxRpmFraction, string Orientation)
{
    /// <summary>MotorConfigurationType.getAchieveableMaxTicksPerSecondRounded(): ticks/rev × max RPM × fraction / 60, rounded.</summary>
    public double AchievableMaxTicksPerSecond => Math.Round(TicksPerRev * MaxRpm * AchievableMaxRpmFraction / 60.0);
}

/// <summary>REV Control/Expansion Hub motor controller firmware constants (config/motors.yaml hub_motor_controller).</summary>
public sealed record HubControllerSpec(
    double PowerFullScale,
    int TargetPositionTolerance,
    Pidf DefaultVelocityPidf,
    Pidf DefaultPositionPidf,
    double IntegralTimeUnitS,
    double VelocityWindowS);

/// <summary>P, I, D and F gains in the firmware's units (output full scale = 32767, errors in counts or counts/s).</summary>
public readonly record struct Pidf(double P, double I, double D, double F);

/// <summary>Datasheet description of a hobby servo (config/servos.yaml models).</summary>
public sealed record ServoSpec(
    string Key,
    string Name,
    double[] SupplyVoltages,
    double[] NoLoadSpeedRpm,
    double[] StallTorqueNm,
    double[] StallCurrentA,
    double[] StandardPwmRangeUs,
    double[] ContinuousPwmRangeUs,
    double TravelDeg,
    double DegreesPerUs,
    double DeadbandUs,
    double ProportionalBandDeg)
{
    /// <summary>Linear interpolation of a per-voltage datasheet row (clamped to the listed voltages).</summary>
    public double At(double[] row, double volts)
    {
        if (volts <= SupplyVoltages[0])
        {
            return row[0] * volts / SupplyVoltages[0];   // below the lowest rating: scale down proportionally
        }
        for (var i = 1; i < SupplyVoltages.Length; i++)
        {
            if (volts <= SupplyVoltages[i])
            {
                var t = (volts - SupplyVoltages[i - 1]) / (SupplyVoltages[i] - SupplyVoltages[i - 1]);
                return row[i - 1] + t * (row[i] - row[i - 1]);
            }
        }
        return row[^1];
    }
}

/// <summary>SDK servo PWM mapping and the hub servo ports (config/servos.yaml sdk_servo, hub_servo_ports).</summary>
public sealed record ServoPortSpec(double PulseLowerUs, double PulseUpperUs, double SupplyVoltage);

/// <summary>Battery pack and wiring (config/battery.yaml).</summary>
public sealed record BatterySpec(
    string Key,
    double CapacityAh,
    double PackResistanceOhm,
    double WiringResistanceOhm,
    double QuiescentCurrentA,
    double[][] OcvCurve,
    double BrownoutVoltage,
    double BrownoutHysteresisV,
    double OutputDisableTimeS);

/// <summary>Typed view of the hardware YAML files. Units are converted to SI (rad/s, N·m, s).</summary>
public sealed class HardwareConfig
{
    private const double KgfCmToNm = 0.0980665;   // 1 kgf·cm = 9.80665 N × 0.01 m
    private const double RpmToRadPerS = 2 * Math.PI / 60;

    public required IReadOnlyDictionary<string, MotorSpec> Motors { get; init; }
    public required IReadOnlyDictionary<string, SdkMotorType> SdkMotorTypes { get; init; }
    public required HubControllerSpec HubController { get; init; }
    public required IReadOnlyDictionary<string, ServoSpec> Servos { get; init; }
    public required ServoPortSpec ServoPorts { get; init; }
    public required IReadOnlyDictionary<string, BatterySpec> Batteries { get; init; }

    public MotorSpec Motor(string key) =>
        Motors.TryGetValue(key, out var m) ? m : throw new HardwareConfigException($"motor model '{key}' not in config/motors.yaml");

    public ServoSpec Servo(string key) =>
        Servos.TryGetValue(key, out var s) ? s : throw new HardwareConfigException($"servo model '{key}' not in config/servos.yaml");

    public SdkMotorType SdkTypeOf(MotorSpec m) =>
        SdkMotorTypes.TryGetValue(m.SdkMotorType, out var t) ? t : throw new HardwareConfigException($"sdk motor type '{m.SdkMotorType}' missing");

    /// <summary>Loads the four hardware files from a config directory (default: the repository's config/).</summary>
    public static HardwareConfig Load(string? configDir = null)
    {
        var dir = configDir ?? FindConfigDir();
        var motors = Read(Path.Combine(dir, "motors.yaml"));
        var servos = Read(Path.Combine(dir, "servos.yaml"));
        var battery = Read(Path.Combine(dir, "battery.yaml"));

        var motorSpecs = new SortedDictionary<string, MotorSpec>(StringComparer.Ordinal);
        foreach (var (key, raw) in Map(motors, "models"))
        {
            var m = AsMap(raw, key);
            motorSpecs[key] = new MotorSpec(
                key,
                Str(m, "name"),
                Q(m, "gear_ratio"),
                Q(m, "nominal_voltage"),
                Q(m, "free_speed") * RpmToRadPerS,
                Q(m, "free_current"),
                Q(m, "stall_current"),
                Q(m, "stall_torque") * KgfCmToNm,
                Q(m, "counts_per_output_rev"),
                Q(m, "rotor_inertia"),
                Str(m, "sdk_motor_type"));
        }

        var sdkTypes = new SortedDictionary<string, SdkMotorType>(StringComparer.Ordinal);
        foreach (var (key, raw) in Map(motors, "sdk_motor_types"))
        {
            var t = AsMap(raw, key);
            sdkTypes[key] = new SdkMotorType(key, Str(t, "display_name"), Q(t, "ticks_per_rev"), Q(t, "gearing"), Q(t, "max_rpm"),
                Q(t, "achievable_max_rpm_fraction"), QStr(t, "orientation"));
        }

        var hub = AsMap(motors["hub_motor_controller"], "hub_motor_controller");
        var hubSpec = new HubControllerSpec(
            Q(hub, "power_full_scale"),
            (int)Q(hub, "default_target_position_tolerance"),
            PidfOf(QList(hub, "default_velocity_pidf")),
            PidfOf(QList(hub, "default_position_pidf")),
            Q(hub, "integral_time_unit"),
            Q(hub, "velocity_window"));

        var servoSpecs = new SortedDictionary<string, ServoSpec>(StringComparer.Ordinal);
        foreach (var (key, raw) in Map(servos, "models"))
        {
            var s = AsMap(raw, key);
            servoSpecs[key] = new ServoSpec(
                key,
                Str(s, "name"),
                QList(s, "supply_voltages"),
                QList(s, "no_load_speed"),
                QList(s, "stall_torque").Select(t => t * KgfCmToNm).ToArray(),
                QList(s, "stall_current"),
                QList(s, "standard_pwm_range"),
                QList(s, "continuous_pwm_range"),
                Q(s, "travel"),
                Q(s, "degrees_per_us"),
                Q(s, "deadband"),
                Q(s, "proportional_band"));
        }
        var sdkServo = AsMap(servos["sdk_servo"], "sdk_servo");
        var ports = AsMap(servos["hub_servo_ports"], "hub_servo_ports");
        var portSpec = new ServoPortSpec(Q(sdkServo, "pulse_lower_default"), Q(sdkServo, "pulse_upper_default"), Q(ports, "supply_voltage"));

        var wiring = AsMap(battery["wiring"], "wiring");
        var brownout = AsMap(battery["brownout"], "brownout");
        var batteries = new SortedDictionary<string, BatterySpec>(StringComparer.Ordinal);
        foreach (var (key, raw) in Map(battery, "models"))
        {
            var b = AsMap(raw, key);
            var curve = ((List<object>)AsMap(b["ocv_curve"], "ocv_curve")["value"])
                .Select(p => ((List<object>)p).Select(Num).ToArray()).ToArray();
            batteries[key] = new BatterySpec(key, Q(b, "capacity"), Q(b, "pack_resistance"), Q(wiring, "resistance"),
                Q(wiring, "hub_quiescent_current"), curve, Q(brownout, "threshold"), Q(brownout, "recovery_hysteresis"),
                Q(brownout, "output_disable_time"));
        }

        return new HardwareConfig
        {
            Motors = motorSpecs,
            SdkMotorTypes = sdkTypes,
            HubController = hubSpec,
            Servos = servoSpecs,
            ServoPorts = portSpec,
            Batteries = batteries,
        };
    }

    private static string FindConfigDir()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, "config", "motors.yaml");
            if (File.Exists(candidate))
            {
                return Path.GetDirectoryName(candidate)!;
            }
        }
        throw new HardwareConfigException($"config/motors.yaml not found above {AppContext.BaseDirectory}");
    }

    private static Dictionary<object, object> Read(string path) =>
        new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(path))
        ?? throw new HardwareConfigException($"{path} is empty");

    private static IEnumerable<(string Key, object Value)> Map(Dictionary<object, object> root, string key) =>
        AsMap(root.TryGetValue(key, out var v) ? v : throw new HardwareConfigException($"missing section '{key}'"), key)
            .Select(kv => (kv.Key.ToString()!, kv.Value)).OrderBy(kv => kv.Item1, StringComparer.Ordinal);

    private static Dictionary<object, object> AsMap(object? raw, string what) =>
        raw as Dictionary<object, object> ?? throw new HardwareConfigException($"'{what}' must be a mapping");

    private static string Str(Dictionary<object, object> m, string key) =>
        m.TryGetValue(key, out var v) ? v.ToString()! : throw new HardwareConfigException($"missing '{key}'");

    private static object QValue(Dictionary<object, object> m, string key)
    {
        var q = AsMap(m.TryGetValue(key, out var v) ? v : throw new HardwareConfigException($"missing '{key}'"), key);
        if (!q.ContainsKey("source"))
        {
            throw new HardwareConfigException($"'{key}' has no source");
        }
        return q.TryGetValue("value", out var value) ? value : throw new HardwareConfigException($"'{key}' has no value");
    }

    private static double Q(Dictionary<object, object> m, string key) => Num(QValue(m, key));

    private static string QStr(Dictionary<object, object> m, string key) => QValue(m, key).ToString()!;

    private static double[] QList(Dictionary<object, object> m, string key) =>
        ((List<object>)QValue(m, key)).Select(Num).ToArray();

    private static double Num(object raw) =>
        double.Parse(raw.ToString()!, NumberStyles.Float, CultureInfo.InvariantCulture);

    private static Pidf PidfOf(double[] v) => new(v[0], v[1], v[2], v[3]);
}
