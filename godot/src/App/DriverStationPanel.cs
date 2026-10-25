using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.DriverStation;
using Sim.Robot;
using Sim.Runner;

namespace Sim.App;

/// <summary>
/// Driver Station panel (spec 08): OpMode selector (TeleOp and Autonomous sections), INIT / ▶ / ■ with the real DS
/// availability, autonomous 30 s timer toggle and countdown, telemetry, error banner, battery voltage, gamepad
/// assignment, Restart Robot, and a switch to Phase 5 direct drive (no code). All logic is in
/// <see cref="DriverStationApp"/>; this class only shows it. Buttons never take keyboard focus, so the keyboard
/// gamepad cannot press them.
/// </summary>
public partial class DriverStationPanel : PanelContainer
{
    private readonly RobotProgram _program;
    private readonly RobotBody _robot;
    private readonly GamepadInput _input;
    private readonly DirectDrive _directDrive;
    private readonly List<OpModeInfo?> _items = new();
    private OptionButton _opModes = null!;
    private Button _init = null!;
    private Button _play = null!;
    private Button _stop = null!;
    private CheckButton _timer = null!;
    private CheckButton _direct = null!;
    private Label _status = null!;
    private Label _timerLabel = null!;
    private Label _battery = null!;
    private Label _gamepads = null!;
    private Label _error = null!;
    private RichTextLabel _telemetry = null!;
    private int _listVersion = -1;

    public DriverStationPanel(RobotProgram program, RobotBody robot, GamepadInput input)
    {
        _program = program;
        _robot = robot;
        _input = input;
        _directDrive = new DirectDrive(input);
    }

    public override void _Ready()
    {
        AnchorLeft = 1;
        AnchorRight = 1;
        AnchorBottom = 1;
        OffsetLeft = -380;
        OffsetRight = -8;
        OffsetTop = 8;
        OffsetBottom = -8;
        var box = new VBoxContainer();
        AddChild(box);
        box.AddChild(new Label { Text = "Driver Station", HorizontalAlignment = HorizontalAlignment.Center });
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_status);

        _opModes = new OptionButton { FocusMode = FocusModeEnum.None };
        _opModes.ItemSelected += index => Select((int)index);
        box.AddChild(_opModes);

        var buttons = new HBoxContainer();
        _init = MakeButton("INIT", () => _program.Ds.Init());
        _play = MakeButton("▶", () => _program.Ds.Play(_robot.NowNanos));
        _stop = MakeButton("■", () => _program.Ds.Stop());
        buttons.AddChild(_init);
        buttons.AddChild(_play);
        buttons.AddChild(_stop);
        box.AddChild(buttons);

        var timerRow = new HBoxContainer();
        _timer = new CheckButton { Text = "30 s timer", FocusMode = FocusModeEnum.None };
        _timer.Toggled += on => _program.Ds.AutonomousTimerEnabled = on;
        _timerLabel = new Label();
        timerRow.AddChild(_timer);
        timerRow.AddChild(_timerLabel);
        box.AddChild(timerRow);

        _battery = new Label();
        _gamepads = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        _error = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1f, 0.35f, 0.35f) };
        box.AddChild(_battery);
        box.AddChild(_gamepads);
        box.AddChild(_error);

        _telemetry = new RichTextLabel { FitContent = false, ScrollFollowing = true, SizeFlagsVertical = SizeFlags.ExpandFill, CustomMinimumSize = new Vector2(0, 220) };
        box.AddChild(_telemetry);

        var bottom = new HBoxContainer();
        bottom.AddChild(MakeButton("Restart Robot", () => _program.RestartRobot()));
        _direct = new CheckButton { Text = "Direct drive (no code)", FocusMode = FocusModeEnum.None, ButtonPressed = !_program.Connected };
        _direct.Toggled += on => _robot.Driver = on ? _directDrive : _program;
        bottom.AddChild(_direct);
        box.AddChild(bottom);
        _robot.Driver = _direct.ButtonPressed ? _directDrive : _program;
    }

    public override void _Process(double delta)
    {
        var ds = _program.Ds;
        var listVersion = ds.TeleOps.Count * 1000 + ds.Autonomous.Count;
        if (listVersion != _listVersion)
        {
            RebuildList(ds);
            _listVersion = listVersion;
        }
        var selectedIndex = ds.Selected is null ? -1 : _items.IndexOf(ds.Selected);
        if (_opModes.Selected != selectedIndex)
        {
            _opModes.Select(selectedIndex);
        }
        var codeMode = !_direct.ButtonPressed;
        _opModes.Disabled = !codeMode || !ds.CanSelect;
        _init.Disabled = !codeMode || !ds.CanInit;
        _play.Disabled = !codeMode || !ds.CanPlay;
        _stop.Disabled = !codeMode || !ds.CanStop;
        _direct.Disabled = ds.Phase is DsPhase.Initialized or DsPhase.Running or DsPhase.Stopping;
        _timer.Disabled = ds.Selected is not { Autonomous: true } || ds.Phase is DsPhase.Running or DsPhase.Stopping;
        _timer.SetPressedNoSignal(ds.AutonomousTimerEnabled);
        _timerLabel.Text = ds.TimerRemainingSeconds(_robot.NowNanos) is { } left ? $"{left:0.0} s" : "";
        _status.Text = codeMode ? $"Robot: {_program.Status}" : "Direct drive: gamepad1 → mecanum mixing, no robot code";
        _battery.Text = $"Battery {_robot.Powertrain.Battery.BusVolts:0.00} V";
        _gamepads.Text = $"Gamepad 1: {(_input.KeyboardActive ? "keyboard (not realistic)" : _input.Describe(_input.Assignment.Gamepad1))}\n" +
                         $"Gamepad 2: {_input.Describe(_input.Assignment.Gamepad2)}   (Start+A / Start+B to assign)";
        _error.Text = ds.Error ?? "";
        var last = _program.Last;
        _telemetry.Text = codeMode && last != null ? string.Join("\n", last.Telemetry) : "";
    }

    private void Select(int index)
    {
        if (index >= 0 && index < _items.Count && _items[index] is { } op && _program.Ds.CanSelect)
        {
            _program.Ds.Select(op);
        }
    }

    private void RebuildList(DriverStationApp ds)
    {
        _opModes.Clear();
        _items.Clear();
        void Section(string title, IReadOnlyList<OpModeInfo> list)
        {
            _opModes.AddSeparator(title);
            _items.Add(null);
            foreach (var op in list)
            {
                _opModes.AddItem(op.Group.Length > 0 && op.Group != "$$$$$$$" ? $"{op.Name}  ({op.Group})" : op.Name);
                _items.Add(op);
            }
        }
        Section("TeleOp", ds.TeleOps);
        Section("Autonomous", ds.Autonomous);
    }

    private static Button MakeButton(string text, System.Action action)
    {
        var button = new Button { Text = text, FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        button.Pressed += action;
        return button;
    }
}
