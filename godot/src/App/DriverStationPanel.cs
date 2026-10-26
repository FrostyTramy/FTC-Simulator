using System.Collections.Generic;
using System.Linq;
using Godot;
using Sim.DriverStation;
using Sim.Match;
using Sim.Robot;
using Sim.Runner;

namespace Sim.App;

/// <summary>
/// Driver Station panel (spec 08) for the station picked at the top (it also gets Start+A / Start+B and the keyboard
/// gamepad): OpMode selector (TeleOp and Autonomous sections), INIT / ▶ / ■ with the real DS availability, autonomous
/// 30 s timer toggle and countdown, telemetry, error banner, battery voltage, gamepad assignment, Restart Robot, and in
/// practice a switch to direct drive (no code). All logic is in <see cref="DriverStationApp"/>; this class only shows
/// it. Buttons never take keyboard focus, so the keyboard gamepad cannot press them.
/// </summary>
public partial class DriverStationPanel : PanelContainer
{
    private readonly MatchSession _session;
    private readonly GamepadInput _input;
    private readonly List<OpModeInfo?> _items = new();
    private OptionButton _station = null!;
    private OptionButton _opModes = null!;
    private Button _init = null!;
    private Button _play = null!;
    private Button _stop = null!;
    private Button _restart = null!;
    private CheckButton _timer = null!;
    private CheckButton _direct = null!;
    private Label _status = null!;
    private Label _timerLabel = null!;
    private Label _battery = null!;
    private Label _gamepads = null!;
    private Label _error = null!;
    private RichTextLabel _telemetry = null!;
    private int _listVersion = -1;
    private StationRuntime? _shown;

    public DriverStationPanel(MatchSession session, GamepadInput input)
    {
        _session = session;
        _input = input;
    }

    /// <summary>The station whose Driver Station is shown.</summary>
    public StationRuntime? Current => _session.Stations.FirstOrDefault(s => s.Station.Key() == _input.Focused) ?? _session.Stations.FirstOrDefault();

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
        var head = new HBoxContainer();
        head.AddChild(new Label { Text = "Driver Station", SizeFlagsHorizontal = SizeFlags.ExpandFill });
        _station = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "The station shown here gets Start+A / Start+B and the keyboard gamepad" };
        foreach (var s in _session.Stations)
        {
            _station.AddItem($"{s.Station.Label()} · {s.Setup.TeamLabel}");
        }
        _station.ItemSelected += i => _input.Focused = _session.Stations[(int)i].Station.Key();
        head.AddChild(_station);
        box.AddChild(head);
        _status = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_status);

        _opModes = new OptionButton { FocusMode = FocusModeEnum.None };
        _opModes.ItemSelected += index => Select((int)index);
        box.AddChild(_opModes);

        var buttons = new HBoxContainer();
        _init = MakeButton("INIT", () => Current?.Ds?.Init());
        _play = MakeButton("▶", () =>
        {
            if (Current is { Ds: { } ds } s)
            {
                ds.Play(s.Robot.NowNanos);
            }
        });
        _stop = MakeButton("■", () => Current?.Ds?.Stop());
        buttons.AddChild(_init);
        buttons.AddChild(_play);
        buttons.AddChild(_stop);
        box.AddChild(buttons);

        var timerRow = new HBoxContainer();
        _timer = new CheckButton { Text = "30 s timer", FocusMode = FocusModeEnum.None };
        _timer.Toggled += on =>
        {
            if (Current?.Ds is { } ds)
            {
                ds.AutonomousTimerEnabled = on;
            }
        };
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
        _restart = MakeButton("Restart Robot", () => Current?.Program?.RestartRobot());
        bottom.AddChild(_restart);
        _direct = new CheckButton { Text = "Direct drive (no code)", FocusMode = FocusModeEnum.None, TooltipText = "Practice only: drive with the gamepad without the robot code" };
        _direct.Toggled += on =>
        {
            if (Current is { } s)
            {
                _session.SetDirectDrive(s, on);
            }
        };
        bottom.AddChild(_direct);
        box.AddChild(bottom);
    }

    public override void _Process(double delta)
    {
        var s = Current;
        if (s == null)
        {
            _status.Text = "No robot on the field";
            return;
        }
        var index = _session.Stations.ToList().IndexOf(s);
        if (_station.Selected != index)
        {
            _station.Select(index);
        }
        var seat = _input.Seat(s.Station.Key());
        _battery.Text = $"Battery {s.Robot.Powertrain.Battery.BusVolts:0.00} V";
        var a = _input.Assignments;
        _gamepads.Text = $"Gamepad 1: {(seat.KeyboardActive ? "keyboard (not realistic)" : _input.Describe(a.Gamepad1(seat.Id)))}\n" +
                         $"Gamepad 2: {_input.Describe(a.Gamepad2(seat.Id))}   (Start+A / Start+B to assign)";
        var ds = s.Ds;
        var practice = !_session.Director.Match.IsRunning;
        var codeMode = ds != null && s.Robot.Driver is RobotProgram;
        if (ds != null && s != _shown)
        {
            _listVersion = -1;
        }
        _shown = s;
        if (ds != null)
        {
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
        }
        _opModes.Visible = _init.Visible = _play.Visible = _stop.Visible = _timer.Visible = _restart.Visible = _direct.Visible = ds != null;
        _opModes.Disabled = !codeMode || !ds!.CanSelect;
        _init.Disabled = !codeMode || !ds!.CanInit;
        _play.Disabled = !codeMode || !ds!.CanPlay;
        _stop.Disabled = !codeMode || !ds!.CanStop;
        _direct.Disabled = !practice || ds?.Phase is DsPhase.Initialized or DsPhase.Running or DsPhase.Stopping;
        _direct.SetPressedNoSignal(ds != null && !codeMode);
        _timer.Disabled = ds?.Selected is not { Autonomous: true } || ds.Phase is DsPhase.Running or DsPhase.Stopping;
        _timer.SetPressedNoSignal(ds?.AutonomousTimerEnabled ?? false);
        _timerLabel.Text = ds?.TimerRemainingSeconds(s.Robot.NowNanos) is { } left ? $"{left:0.0} s" : "";
        _status.Text = s.Setup.Kind switch
        {
            StationKind.Code when codeMode => $"Robot: {s.Program!.Status}",
            StationKind.Code => "Direct drive: gamepad1 → mecanum mixing, no robot code",
            StationKind.Gamepad => "Gamepad drive (no robot code): gamepad1 → mecanum mixing and the example TeleOp's buttons",
            _ => "Placeholder robot: stays where it was placed",
        };
        _error.Text = ds?.Error ?? "";
        _telemetry.Text = codeMode && s.Program!.Last is { } last ? string.Join("\n", last.Telemetry) : "";
    }

    private void Select(int index)
    {
        if (Current?.Ds is { } ds && index >= 0 && index < _items.Count && _items[index] is { } op && ds.CanSelect)
        {
            ds.Select(op);
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
