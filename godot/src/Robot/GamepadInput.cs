using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.App;
using Sim.DriverStation;
using Sim.Runner;

namespace Sim.Robot;

/// <summary>
/// Physical controllers → FTC gamepad1/gamepad2 (spec 08 "Gamepads"): Godot (SDL) joypads in the standard layout,
/// assigned with Start+A / Start+B like the Driver Station, mapped by <see cref="GamepadMapper"/>. While no controller
/// is gamepad1, the keyboard acts as a virtual gamepad (not realistic: digital sticks): arrows = left stick,
/// Z/X = right stick X, C/V = right/left trigger, Space = right bumper, Enter = A. The free camera keeps WASD/QE.
/// </summary>
public partial class GamepadInput : Node
{
    public const int KeyboardId = -2;   // Gamepad.ID_SYNTHETIC
    private readonly HashSet<int> _known = new();
    private GamepadSettings _settings = null!;

    public GamepadAssignment Assignment { get; } = new();
    public GamepadState Gamepad1 { get; private set; } = GamepadState.Neutral;
    public GamepadState Gamepad2 { get; private set; } = GamepadState.Neutral;

    /// <summary>The keyboard stands in for gamepad1 (no controller assigned).</summary>
    public bool KeyboardActive => Assignment.Gamepad1 is null;

    public override void _Ready()
    {
        _settings = GamepadSettings.Load(RepoPaths.Config("gamepads.yaml"));
    }

    public override void _Process(double delta)
    {
        var connected = Input.GetConnectedJoypads().Select(d => (int)d).ToHashSet();
        foreach (var gone in _known.Where(d => !connected.Contains(d)).ToList())
        {
            Assignment.Disconnected(gone);
            _known.Remove(gone);
        }
        foreach (var device in connected)
        {
            _known.Add(device);
            Assignment.Update(device, Read(device).Buttons);
        }
        Gamepad1 = Assignment.Gamepad1 is { } g1 ? GamepadMapper.ToGamepad(Read(g1), g1, _settings)
            : UiFocus.Typing(this) ? GamepadState.Neutral with { Id = KeyboardId } : Keyboard();
        Gamepad2 = Assignment.Gamepad2 is { } g2 ? GamepadMapper.ToGamepad(Read(g2), g2, _settings) : GamepadState.Neutral;
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
    private readonly GamepadInput _input;

    public DirectDrive(GamepadInput input)
    {
        _input = input;
    }

    public void BeforeStep(RobotBody robot, long tick)
    {
        var g = _input.Gamepad1;
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
