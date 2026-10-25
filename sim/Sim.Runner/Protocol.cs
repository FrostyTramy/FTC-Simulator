using Sim.Core.Config;
using YamlDotNet.Serialization;

namespace Sim.Runner;

/// <summary>Message type bytes of docs/ipc_protocol.md.</summary>
public static class MessageType
{
    public const byte Hello = 1, ListOpModes = 2, Configure = 3, Init = 4, Start = 5, Stop = 6, Tick = 7, Quit = 8;
    public const byte HelloAck = 101, OpModes = 102, ConfigureAck = 103, InitAck = 104, StartAck = 105, StopAck = 106, State = 107;
    public const byte Error = 199;
}

/// <summary>OpMode state reported in STATE (OpModeDriver.State ordinals, plus Idle when no OpMode is loaded).</summary>
public enum RunnerState : byte
{
    Init = 0,
    Running = 1,
    Stopping = 2,
    Stopped = 3,
    Crashed = 4,
    Idle = 5,
}

/// <summary>A registered OpMode (@TeleOp/@Autonomous, not @Disabled) as the Driver Station lists it.</summary>
public sealed record OpModeInfo(string ClassName, string Name, string Group, bool Autonomous, string PreselectTeleOp);

/// <summary>One configured device in CONFIGURE. Motor type fields are used for motors only.</summary>
public sealed record RunnerDevice(
    string Name,
    string Type,
    int Hub,
    int Port,
    string? MotorTypeName = null,
    double TicksPerRev = 0,
    double Gearing = 0,
    double MaxRpm = 0,
    double AchievableFraction = 0,
    string? Orientation = null);

/// <summary>Virtual time SDK calls take, in nanoseconds (config/hardware_latency.yaml).</summary>
public sealed record HardwareCosts(long ControlHubCommand, long ExpansionHubCommand, long ControlHubI2cRead, long LoopOverhead,
    long TrivialCall, long GamepadPeriod)
{
    /// <summary>Reads config/hardware_latency.yaml (default: the repository's).</summary>
    public static HardwareCosts Load(string? path = null)
    {
        path ??= RepoPaths.Find(Path.Combine("config", "hardware_latency.yaml"));
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(path));
        var hubs = (Dictionary<object, object>)root["hubs"];
        var control = (Dictionary<object, object>)hubs["control_hub"];
        var expansion = (Dictionary<object, object>)hubs["expansion_hub"];
        var opmode = (Dictionary<object, object>)root["opmode"];
        return new HardwareCosts(Ms(control, "lynx_command"), Ms(expansion, "lynx_command"), Ms(control, "i2c_read"),
            Ms(opmode, "loop_overhead"), Ms(opmode, "trivial_sdk_call"), Ms(opmode, "driver_station_gamepad_period"));
    }

    private static long Ms(Dictionary<object, object> section, string key)
    {
        var q = (Dictionary<object, object>)section[key];
        if ((string)q["unit"] != "ms")
        {
            throw new InvalidDataException($"hardware_latency.yaml {key}: expected unit ms");
        }
        var ms = double.Parse((string)q["value"], System.Globalization.CultureInfo.InvariantCulture);
        return (long)Math.Round(ms * 1_000_000);
    }
}

/// <summary>Gamepad buttons, bit numbers as in the protocol (PlayStation names are aliases).</summary>
[Flags]
public enum GamepadButtons
{
    None = 0,
    A = 1 << 0,
    B = 1 << 1,
    X = 1 << 2,
    Y = 1 << 3,
    DpadUp = 1 << 4,
    DpadDown = 1 << 5,
    DpadLeft = 1 << 6,
    DpadRight = 1 << 7,
    LeftBumper = 1 << 8,
    RightBumper = 1 << 9,
    LeftStickButton = 1 << 10,
    RightStickButton = 1 << 11,
    Back = 1 << 12,
    Start = 1 << 13,
    Guide = 1 << 14,
    Touchpad = 1 << 15,
}

/// <summary>One gamepad in SDK conventions: sticks −1..1 with up = −1, triggers 0..1. Id −1 = not associated.</summary>
public readonly record struct GamepadState(
    float LeftStickX = 0,
    float LeftStickY = 0,
    float RightStickX = 0,
    float RightStickY = 0,
    float LeftTrigger = 0,
    float RightTrigger = 0,
    GamepadButtons Buttons = GamepadButtons.None,
    int Id = -1)
{
    public static GamepadState Neutral => new();
}

/// <summary>Raw state of one motor port (motor's own direction).</summary>
public sealed record MotorInput(string Key, int Position, double Velocity, double CurrentAmps, bool Busy);

/// <summary>Everything the runner gets for one simulation step.</summary>
/// <param name="ImuOrientation">IMU chip frame in a Z-up world, (w, x, y, z).</param>
/// <param name="ImuAngularVelocity">Rad/s about the IMU chip axes.</param>
public sealed record TickInput(
    long Tick,
    long EndNanos,
    GamepadState Gamepad1,
    GamepadState Gamepad2,
    double BatteryVolts,
    Quat ImuOrientation,
    Vec3d ImuAngularVelocity,
    IReadOnlyList<MotorInput> Motors);

/// <summary>Raw command of one motor port after the SDK applied directions (DeviceBus.MotorCommand).</summary>
/// <param name="VelocityPidf">Null = firmware default.</param>
/// <param name="PositionP">NaN = firmware default.</param>
public sealed record MotorCommand(
    string Key,
    string Mode,
    double Power,
    bool VelocitySet,
    double Velocity,
    int TargetPosition,
    string ZeroPower,
    int Tolerance,
    bool Enabled,
    int ResetCount,
    double[]? VelocityPidf,
    double PositionP);

/// <summary>Raw command of one servo port: pulse width (NaN = never set) and PWM enable.</summary>
public sealed record ServoCommand(string Key, double PulseUs, bool PwmEnabled);

/// <summary>The runner's answer to one TICK.</summary>
public sealed record TickResult(
    long NowNanos,
    RunnerState State,
    string? Error,
    IReadOnlyList<MotorCommand> Motors,
    IReadOnlyList<ServoCommand> Servos,
    long TelemetryTransmissions,
    IReadOnlyList<string> Telemetry,
    IReadOnlyList<string> Log);

/// <summary>Double-precision vector.</summary>
public readonly record struct Vec3d(double X, double Y, double Z);

/// <summary>Double-precision unit quaternion (w, x, y, z); a ⊗ b applies b first, then a.</summary>
public readonly record struct Quat(double W, double X, double Y, double Z)
{
    public static Quat Identity => new(1, 0, 0, 0);

    /// <summary>Rotation by <paramref name="angleRad"/> about +Z (counter-clockwise seen from above in a Z-up world).</summary>
    public static Quat FromYaw(double angleRad) => new(Math.Cos(angleRad / 2), 0, 0, Math.Sin(angleRad / 2));

    public static Quat operator *(Quat a, Quat b) => new(
        a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z,
        a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
        a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
        a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W);

    public Quat Conjugate => new(W, -X, -Y, -Z);

    /// <summary>Rotates a vector: q v q*.</summary>
    public Vec3d Rotate(Vec3d v)
    {
        var r = this * new Quat(0, v.X, v.Y, v.Z) * Conjugate;
        return new Vec3d(r.X, r.Y, r.Z);
    }

    /// <summary>Quaternion of the rotation matrix whose columns are the given axes (orthonormal, right-handed).</summary>
    public static Quat FromAxes(Vec3d x, Vec3d y, Vec3d z)
    {
        double m00 = x.X, m10 = x.Y, m20 = x.Z, m01 = y.X, m11 = y.Y, m21 = y.Z, m02 = z.X, m12 = z.Y, m22 = z.Z;
        var trace = m00 + m11 + m22;
        if (trace > 0)
        {
            var s = Math.Sqrt(trace + 1) * 2;
            return new Quat(s / 4, (m21 - m12) / s, (m02 - m20) / s, (m10 - m01) / s);
        }
        if (m00 > m11 && m00 > m22)
        {
            var s = Math.Sqrt(1 + m00 - m11 - m22) * 2;
            return new Quat((m21 - m12) / s, s / 4, (m01 + m10) / s, (m02 + m20) / s);
        }
        if (m11 > m22)
        {
            var s = Math.Sqrt(1 + m11 - m00 - m22) * 2;
            return new Quat((m02 - m20) / s, (m01 + m10) / s, s / 4, (m12 + m21) / s);
        }
        var t = Math.Sqrt(1 + m22 - m00 - m11) * 2;
        return new Quat((m10 - m01) / t, (m02 + m20) / t, (m12 + m21) / t, t / 4);
    }
}
