using Sim.Core.Config;
using Sim.Runner;
using YamlDotNet.Serialization;

namespace Sim.DriverStation;

/// <summary>
/// One physical controller as SDL/Godot reports it (standard game-controller layout): axes −1..1 with stick down = +1
/// (SDL convention, same sign as the FTC SDK), triggers 0..1, buttons by position (South = A/Cross, East = B/Circle,
/// West = X/Square, North = Y/Triangle).
/// </summary>
public readonly record struct RawController(
    float LeftX,
    float LeftY,
    float RightX,
    float RightY,
    float LeftTrigger,
    float RightTrigger,
    GamepadButtons Buttons);

/// <summary>Gamepad settings (config/gamepads.yaml).</summary>
public sealed record GamepadSettings(double StickDeadzone)
{
    public static GamepadSettings Load(string? path = null)
    {
        path ??= RepoPaths.Find(Path.Combine("config", "gamepads.yaml"));
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(File.ReadAllText(path));
        var deadzone = (Dictionary<object, object>)root["stick_deadzone"];
        return new GamepadSettings(double.Parse((string)deadzone["value"], System.Globalization.CultureInfo.InvariantCulture));
    }
}

/// <summary>Turns a physical controller into the FTC <c>Gamepad</c> values the Driver Station sends (spec 08 "Mapping").</summary>
public static class GamepadMapper
{
    /// <summary>
    /// Sticks get a radial deadzone (inside it the stick reads 0; outside, the magnitude is rescaled so it still reaches
    /// 1 at full deflection); triggers are clamped to 0..1; buttons pass through. Stick up stays negative, as in the SDK.
    /// </summary>
    public static GamepadState ToGamepad(RawController raw, int id, GamepadSettings settings)
    {
        var (lx, ly) = Deadzone(raw.LeftX, raw.LeftY, settings.StickDeadzone);
        var (rx, ry) = Deadzone(raw.RightX, raw.RightY, settings.StickDeadzone);
        return new GamepadState(lx, ly, rx, ry, Math.Clamp(raw.LeftTrigger, 0f, 1f), Math.Clamp(raw.RightTrigger, 0f, 1f), raw.Buttons, id);
    }

    private static (float X, float Y) Deadzone(float x, float y, double deadzone)
    {
        var magnitude = Math.Sqrt((double)x * x + (double)y * y);
        if (magnitude <= deadzone || magnitude == 0)
        {
            return (0, 0);
        }
        var scaled = Math.Min(1, (magnitude - deadzone) / (1 - deadzone));
        var k = scaled / magnitude;
        return ((float)Math.Clamp(x * k, -1, 1), (float)Math.Clamp(y * k, -1, 1));
    }
}

/// <summary>
/// Which physical controller is gamepad1 and gamepad2, assigned like the real Driver Station (spec 08): hold Start and
/// press A (Options + Cross) for gamepad1, Start + B (Options + Circle) for gamepad2. A controller holds one slot;
/// assigning it to the other slot frees the first. Unplugging frees its slot.
/// </summary>
public sealed class GamepadAssignment
{
    private readonly Dictionary<int, GamepadButtons> _previous = [];

    /// <summary>Device id of gamepad1, or null.</summary>
    public int? Gamepad1 { get; private set; }

    /// <summary>Device id of gamepad2, or null.</summary>
    public int? Gamepad2 { get; private set; }

    /// <summary>Feeds one controller's buttons (every frame); returns true if the assignment changed.</summary>
    public bool Update(int deviceId, GamepadButtons buttons)
    {
        var before = _previous.GetValueOrDefault(deviceId);
        _previous[deviceId] = buttons;
        var start = buttons.HasFlag(GamepadButtons.Start);
        bool Pressed(GamepadButtons b) => buttons.HasFlag(b) && !before.HasFlag(b);
        if (start && Pressed(GamepadButtons.A))
        {
            return Assign(deviceId, first: true);
        }
        if (start && Pressed(GamepadButtons.B))
        {
            return Assign(deviceId, first: false);
        }
        return false;
    }

    /// <summary>A controller was unplugged.</summary>
    public void Disconnected(int deviceId)
    {
        _previous.Remove(deviceId);
        if (Gamepad1 == deviceId)
        {
            Gamepad1 = null;
        }
        if (Gamepad2 == deviceId)
        {
            Gamepad2 = null;
        }
    }

    private bool Assign(int deviceId, bool first)
    {
        var changed = first ? Gamepad1 != deviceId : Gamepad2 != deviceId;
        if (first)
        {
            Gamepad1 = deviceId;
            if (Gamepad2 == deviceId)
            {
                Gamepad2 = null;
            }
        }
        else
        {
            Gamepad2 = deviceId;
            if (Gamepad1 == deviceId)
            {
                Gamepad1 = null;
            }
        }
        return changed;
    }
}

/// <summary>
/// Controllers shared by several Driver Stations on one PC (spec 08 "Multiple robots: up to 4 DS instances, each with
/// its own gamepads"): Start+A / Start+B assigns the pressing controller to gamepad1 / gamepad2 of the focused station
/// and takes it away from any other station, so a controller always belongs to one station at most.
/// </summary>
public sealed class GamepadSeats
{
    private readonly Dictionary<string, GamepadAssignment> _seats = new(StringComparer.Ordinal);

    /// <summary>The station that Start+A / Start+B assigns to (the Driver Station on screen).</summary>
    public string? Focused { get; set; }

    public IReadOnlyCollection<string> Seats => _seats.Keys;

    public void Add(string seat)
    {
        _seats.TryAdd(seat, new GamepadAssignment());
        Focused ??= seat;
    }

    public void Remove(string seat)
    {
        _seats.Remove(seat);
        if (Focused == seat)
        {
            Focused = _seats.Keys.FirstOrDefault();
        }
    }

    public int? Gamepad1(string seat) => _seats.TryGetValue(seat, out var a) ? a.Gamepad1 : null;

    public int? Gamepad2(string seat) => _seats.TryGetValue(seat, out var a) ? a.Gamepad2 : null;

    /// <summary>Feeds one controller's buttons (every frame); returns true if any assignment changed.</summary>
    public bool Update(int deviceId, GamepadButtons buttons)
    {
        var changed = false;
        foreach (var (seat, assignment) in _seats)
        {
            if (seat == Focused)
            {
                changed |= assignment.Update(deviceId, buttons);
            }
            else
            {
                assignment.Update(deviceId, GamepadButtons.None);   // keeps edge detection in step without assigning
            }
        }
        if (changed && Focused != null)
        {
            foreach (var (seat, assignment) in _seats.Where(s => s.Key != Focused))
            {
                if (assignment.Gamepad1 == deviceId || assignment.Gamepad2 == deviceId)
                {
                    assignment.Disconnected(deviceId);   // the controller moved to the focused station
                }
            }
        }
        return changed;
    }

    public void Disconnected(int deviceId)
    {
        foreach (var assignment in _seats.Values)
        {
            assignment.Disconnected(deviceId);
        }
    }
}
