using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.App;
using Sim.DriverStation;
using Sim.Runner;

namespace Sim.Robot;

/// <summary>The two gamepads one Driver Station hands to its robot (live controllers, AI drivers, recorded input).</summary>
public interface IGamepadSource
{
    GamepadState Gamepad1 { get; }
    GamepadState Gamepad2 { get; }
}

/// <summary>A station's gamepads as <see cref="GamepadInput"/> reads them every frame.</summary>
public sealed class GamepadSeat : IGamepadSource
{
    public GamepadSeat(string id)
    {
        Id = id;
    }

    public string Id { get; }
    public GamepadState Gamepad1 { get; internal set; } = GamepadState.Neutral;
    public GamepadState Gamepad2 { get; internal set; } = GamepadState.Neutral;

    /// <summary>The keyboard stands in for gamepad1 (this station is focused and has no controller as gamepad1).</summary>
    public bool KeyboardActive { get; internal set; }
}

/// <summary>
/// Physical controllers → FTC gamepad1/gamepad2 of every Driver Station (spec 08 "Gamepads"): Godot (SDL) joypads in the
/// standard layout, mapped by <see cref="GamepadMapper"/> and assigned with Start+A / Start+B to the focused station
/// like the real Driver Station (<see cref="GamepadSeats"/>). While the focused station has no controller as gamepad1,
/// the keyboard acts as a virtual gamepad (not realistic: digital sticks): arrows = left stick, Z/X = right stick X,
/// C/V = right/left trigger, Space = right bumper, Enter = A. The free camera keeps WASD/QE.
/// </summary>
public partial class GamepadInput : Node
{
    public const int KeyboardId = -2;   // Gamepad.ID_SYNTHETIC
    private readonly HashSet<int> _known = new();
    private readonly Dictionary<string, GamepadSeat> _seats = new();
    private GamepadSettings _settings = null!;

    public GamepadSeats Assignments { get; } = new();

    /// <summary>The station whose Driver Station is on screen: Start+A/B and the keyboard go to it.</summary>
    public string? Focused
    {
        get => Assignments.Focused;
        set => Assignments.Focused = value;
    }

    /// <summary>The gamepads of station <paramref name="id"/> (created on first use).</summary>
    public GamepadSeat Seat(string id)
    {
        if (!_seats.TryGetValue(id, out var seat))
        {
            seat = _seats[id] = new GamepadSeat(id);
            Assignments.Add(id);
        }
        return seat;
    }

    public void RemoveSeat(string id)
    {
        _seats.Remove(id);
        Assignments.Remove(id);
    }

    public override void _Ready()
    {
        _settings = GamepadSettings.Load(RepoPaths.Config("gamepads.yaml"));
    }

    public override void _Process(double delta)
    {
        var connected = Input.GetConnectedJoypads().Select(d => (int)d).ToHashSet();
        foreach (var gone in _known.Where(d => !connected.Contains(d)).ToList())
        {
            Assignments.Disconnected(gone);
            _known.Remove(gone);
        }
        foreach (var device in connected)
        {
            _known.Add(device);
            Assignments.Update(device, Read(device).Buttons);
        }
        foreach (var seat in _seats.Values)
        {
            var g1 = Assignments.Gamepad1(seat.Id);
            var g2 = Assignments.Gamepad2(seat.Id);
            seat.KeyboardActive = g1 is null && seat.Id == Focused;
            seat.Gamepad1 = g1 is { } d1 ? GamepadMapper.ToGamepad(Read(d1), d1, _settings)
                : !seat.KeyboardActive ? GamepadState.Neutral
                : UiFocus.Typing(this) ? GamepadState.Neutral with { Id = KeyboardId } : Keyboard();
            seat.Gamepad2 = g2 is { } d2 ? GamepadMapper.ToGamepad(Read(d2), d2, _settings) : GamepadState.Neutral;
        }
    }

    /// <summary>Label for the Driver Station: controller name or "none".</summary>
    public string Describe(int? device) => device is { } d ? $"{Input.GetJoyName(d)} (#{d})" : "none";

    private static RawController Read(int device)
    {
        var buttons = GamepadButtons.None;
        void Map(JoyButton b, GamepadButtons g)
        {
            if (Input.IsJoyButtonPressed(device, b))
            {
                buttons |= g;
            }
        }
        Map(JoyButton.A, GamepadButtons.A);
        Map(JoyButton.B, GamepadButtons.B);
        Map(JoyButton.X, GamepadButtons.X);
        Map(JoyButton.Y, GamepadButtons.Y);
        Map(JoyButton.DpadUp, GamepadButtons.DpadUp);
        Map(JoyButton.DpadDown, GamepadButtons.DpadDown);
        Map(JoyButton.DpadLeft, GamepadButtons.DpadLeft);
        Map(JoyButton.DpadRight, GamepadButtons.DpadRight);
        Map(JoyButton.LeftShoulder, GamepadButtons.LeftBumper);
        Map(JoyButton.RightShoulder, GamepadButtons.RightBumper);
        Map(JoyButton.LeftStick, GamepadButtons.LeftStickButton);
        Map(JoyButton.RightStick, GamepadButtons.RightStickButton);
        Map(JoyButton.Back, GamepadButtons.Back);
        Map(JoyButton.Start, GamepadButtons.Start);
        Map(JoyButton.Guide, GamepadButtons.Guide);
        Map(JoyButton.Touchpad, GamepadButtons.Touchpad);
        return new RawController(
            Input.GetJoyAxis(device, JoyAxis.LeftX), Input.GetJoyAxis(device, JoyAxis.LeftY),
            Input.GetJoyAxis(device, JoyAxis.RightX), Input.GetJoyAxis(device, JoyAxis.RightY),
            Input.GetJoyAxis(device, JoyAxis.TriggerLeft), Input.GetJoyAxis(device, JoyAxis.TriggerRight),
            buttons);
    }

    private static GamepadState Keyboard()
    {
        static float Axis(Key negative, Key positive) => (Input.IsKeyPressed(positive) ? 1f : 0f) - (Input.IsKeyPressed(negative) ? 1f : 0f);
        var buttons = GamepadButtons.None;
        if (Input.IsKeyPressed(Key.Space))
        {
            buttons |= GamepadButtons.RightBumper;
        }
        if (Input.IsKeyPressed(Key.Enter))
        {
            buttons |= GamepadButtons.A;
        }
        return new GamepadState(
            LeftStickX: Axis(Key.Left, Key.Right),
            LeftStickY: Axis(Key.Up, Key.Down),   // up arrow = stick up = −1
            RightStickX: Axis(Key.Z, Key.X),
            LeftTrigger: Input.IsKeyPressed(Key.V) ? 1f : 0f,
            RightTrigger: Input.IsKeyPressed(Key.C) ? 1f : 0f,
            Buttons: buttons,
            Id: KeyboardId);
    }
}

/// <summary>
/// Phase 5 direct drive (no robot code): the example TeleOp's controls ported to C# (BioBuzzStarterbotTeleopMecanum):
/// mecanumDrive(-left_stick_y, left_stick_x, right_stick_x) (CODE:198); intake power = right_trigger − left_trigger on
/// the intake motor and both corner servos (CODE:212-226); right bumper → launcher.setVelocity(1250) else 0
/// (CODE:277-280); the windmill feeds while the bumper is held and the launcher reads more than 1200 ticks/s
/// (CODE:290-294). Directions and zero-power behaviour come from the robot YAML (what the code's init sets), the
/// launcher PIDF 40/0/0/12.5 from CODE:145-147. Robots without these devices only drive.
/// </summary>
public sealed class DirectDrive : IRobotDriver
{
    private static readonly Sim.Hardware.Pidf LauncherPidf = new(40, 0, 0, 12.5);
    private const double LauncherTarget = 1250;   // CODE:76
    private const double LauncherMin = 1200;      // CODE:77
    private readonly IGamepadSource _input;

    public DirectDrive(IGamepadSource input)
    {
        _input = input;
    }

    /// <summary>The gamepads read in the last step (recorded for replays).</summary>
    public (GamepadState Gamepad1, GamepadState Gamepad2) LastInput { get; private set; }

    public void BeforeStep(RobotBody robot, long tick)
    {
        var g = _input.Gamepad1;
        LastInput = (g, _input.Gamepad2);
        var p = robot.Powertrain;
        p.MecanumDrive(-g.LeftStickY, g.LeftStickX, g.RightStickX);
        var devices = p.Robot.Devices;
        if (!devices.Any(d => d.Name == "launcher") || !devices.Any(d => d.Name == "windmillServo"))
        {
            return;
        }
        double intake = g.RightTrigger - g.LeftTrigger;
        p.SetPowerFromSdk("intake", intake);
        p.SetCrServoFromSdk("left_intake_servo", intake);
        p.SetCrServoFromSdk("right_intake_servo", intake);
        var bumper = g.Buttons.HasFlag(GamepadButtons.RightBumper);
        p.SetVelocityFromSdk("launcher", bumper ? LauncherTarget : 0, LauncherPidf);
        p.SetCrServoFromSdk("windmillServo", bumper && p.VelocityFromSdk("launcher") > LauncherMin ? 1 : 0);
    }

    public void AfterStep(RobotBody robot, long tick)
    {
    }
}
