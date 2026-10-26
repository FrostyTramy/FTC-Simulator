using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Godot;
using Sim.App;
using Sim.Field;
using Sim.Match.Replay;
using Sim.Robot;

namespace Sim.Match;

/// <summary>
/// Basic replay viewer (spec 10 "Replays", Phase 10 "replays (basic)"): plays the 60 Hz pose track of a replay file
/// (robots, elements, hives) with play/pause, a timeline to scrub, 0.1x–4x speed and buttons to jump to the scoring and
/// foul moments of its match record. Bodies are frozen and posed from the recording; nothing is simulated. Wheels and
/// mechanism angles are not in the pose track. Cameras: the usual free camera and view presets.
/// </summary>
public partial class ReplayViewer : Node
{
    private static readonly double[] Speeds = [0.1, 0.25, 0.5, 1, 2, 4];
    private readonly LoadedField _field;
    private readonly string _path;
    private readonly PhysicsConfig _physics;
    private readonly List<RobotBody> _robots = new();
    private List<PoseFrame> _frames = new();
    private List<RigidBody3D> _parts = new();
    private int _tickRate = 240;
    private double _time;      // seconds since the first frame
    private double _speed = 1;
    private bool _playing = true;
    private HSlider _slider = null!;
    private Label _info = null!;
    private Label _score = null!;
    private bool _dragging;

    public ReplayViewer(LoadedField field, string path, PhysicsConfig physics)
    {
        _field = field;
        _path = path;
        _physics = physics;
    }

    private double Duration => _frames.Count < 2 ? 0 : (_frames[^1].Tick - _frames[0].Tick) / (double)_tickRate;

    public override void _Ready()
    {
        using var reader = new ReplayReader(_path);
        _tickRate = reader.Header.TickRate;
        (_frames, _) = reader.ReadForViewing();
        foreach (var station in reader.Header.RobotOrder)
        {
            var setup = reader.Header.Setup.Of(station)!;
            var robot = RobotBody.Spawn(this, setup.Robot, Transform3D.Identity, _physics, nodeName: $"Replay_{station.Key()}");
            robot.Freeze = true;
            robot.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            _robots.Add(robot);
        }
        foreach (var e in _field.Elements)
        {
            e.Body.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            e.Body.Freeze = true;
        }
        // Spawned elements (practice) that are not in this field yet.
        for (var i = _field.Elements.Count; i < (_frames.FirstOrDefault()?.Elements.Count ?? 0); i++)
        {
            var group = i < reader.Header.ElementGroups.Count ? reader.Header.ElementGroups[i] : "pollen";
            var e = FieldLoader.SpawnElement(_field, group, Transform3D.Identity);
            e.Body.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            e.Body.Freeze = true;
        }
        _parts = _field.RigidGroups.OrderBy(g => g.Key, StringComparer.Ordinal).Select(g => g.Value).ToList();
        foreach (var part in _parts)
        {
            part.FreezeMode = RigidBody3D.FreezeModeEnum.Kinematic;
            part.Freeze = true;
        }
        BuildUi(LoadRecord(reader.Header));
        Show(0);
    }

    /// <summary>The match record saved next to the replay (same id), for the event buttons.</summary>
    private MatchRecord? LoadRecord(ReplayHeader header)
    {
        var json = Path.ChangeExtension(_path, ".json");
        try
        {
            return File.Exists(json) ? MatchRecord.FromJson(File.ReadAllText(json)) : null;
        }
        catch (Exception e) when (e is System.Text.Json.JsonException or InvalidDataException)
        {
            return null;
        }
    }

    private void BuildUi(MatchRecord? record)
    {
        var layer = new CanvasLayer { Name = "ReplayUi" };
        AddChild(layer);
        var panel = new PanelContainer { AnchorLeft = 0, AnchorRight = 1, AnchorTop = 1, AnchorBottom = 1, OffsetTop = -112, OffsetLeft = 8, OffsetRight = -8, OffsetBottom = -8 };
        layer.AddChild(panel);
        var box = new VBoxContainer();
        panel.AddChild(box);
        var top = new HBoxContainer();
        _score = new Label { CustomMinimumSize = new Vector2(260, 0) };
        _score.AddThemeFontSizeOverride("font_size", 20);
        top.AddChild(_score);
        _info = new Label { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        top.AddChild(_info);
        box.AddChild(top);
        _slider = new HSlider { MinValue = 0, MaxValue = Math.Max(Duration, 0.01), Step = 1.0 / 60, SizeFlagsHorizontal = Control.SizeFlags.ExpandFill, FocusMode = Control.FocusModeEnum.None };
        _slider.DragStarted += () => _dragging = true;
        _slider.DragEnded += _ => _dragging = false;
        _slider.ValueChanged += v =>
        {
            if (_dragging)
            {
                _time = v;
                Show(_time);
            }
        };
        box.AddChild(_slider);
        var buttons = new HBoxContainer();
        Add(buttons, "⏯", () => _playing = !_playing);
        Add(buttons, "⏮", () => Seek(0));
        foreach (var s in Speeds)
        {
            var speed = s;
            Add(buttons, $"{s:0.##}x", () => _speed = speed);
        }
        if (record != null)
        {
            buttons.AddChild(new Label { Text = "  jump to:" });
            foreach (var e in record.Scoring.Where(e => e.Category is Game.Scoring.ScoreCategory.HiveTip or Game.Scoring.ScoreCategory.Leave).Take(8))
            {
                var tick = e.Tick;
                Add(buttons, $"{e.Category} {e.Alliance}", () => SeekTick(tick));
            }
            foreach (var f in record.Fouls.Take(6))
            {
                var tick = f.Tick;
                Add(buttons, $"{f.Rule}", () => SeekTick(tick));
            }
        }
        Add(buttons, "Exit replay", () => GetTree().ReloadCurrentScene());
        box.AddChild(buttons);
    }

    private static void Add(Container parent, string text, Action action)
    {
        var b = new Button { Text = text, FocusMode = Control.FocusModeEnum.None };
        b.Pressed += action;
        parent.AddChild(b);
    }

    private void Seek(double seconds)
    {
        _time = Math.Clamp(seconds, 0, Duration);
        Show(_time);
    }

    private void SeekTick(long tick) => Seek(_frames.Count == 0 ? 0 : (tick - _frames[0].Tick) / (double)_tickRate - 1.0);

    public override void _Process(double delta)
    {
        if (_playing && !_dragging && _frames.Count > 1)
        {
            _time = Math.Min(_time + delta * _speed, Duration);
            Show(_time);
            if (_time >= Duration)
            {
                _playing = false;
            }
        }
        if (!_dragging)
        {
            _slider.SetValueNoSignal(_time);
        }
    }

    /// <summary>Poses every body at <paramref name="seconds"/>, interpolating between the two nearest frames.</summary>
    private void Show(double seconds)
    {
        if (_frames.Count == 0)
        {
            return;
        }
        var tick = _frames[0].Tick + seconds * _tickRate;
        var i = Math.Clamp(_frames.FindLastIndex(f => f.Tick <= tick), 0, _frames.Count - 1);
        var a = _frames[i];
        var b = _frames[Math.Min(i + 1, _frames.Count - 1)];
        var t = b.Tick == a.Tick ? 0f : (float)((tick - a.Tick) / (b.Tick - a.Tick));
        for (var k = 0; k < _robots.Count && k < a.Robots.Count; k++)
        {
            _robots[k].GlobalTransform = Lerp(a.Robots[k], b.Robots[k], t);
        }
        for (var k = 0; k < _field.Elements.Count && k < a.Elements.Count && k < b.Elements.Count; k++)
        {
            _field.Elements[k].Body.GlobalTransform = Lerp(a.Elements[k], b.Elements[k], t);
        }
        for (var k = 0; k < _parts.Count && k < a.FieldParts.Count; k++)
        {
            _parts[k].GlobalTransform = Lerp(a.FieldParts[k], b.FieldParts[k], t);
        }
        _score.Text = $"RED {a.RedScore}   {a.Timer}   {a.BlueScore} BLUE";
        _info.Text = $"replay {Path.GetFileName(_path)}  ·  {seconds:0.0} / {Duration:0.0} s  ·  {_speed:0.##}x{(_playing ? "" : "  (paused)")}";
    }

    private static Transform3D Lerp(BodyPose a, BodyPose b, float t)
    {
        var qa = new Quaternion(a.Qx, a.Qy, a.Qz, a.Qw).Normalized();
        var qb = new Quaternion(b.Qx, b.Qy, b.Qz, b.Qw).Normalized();
        var p = new Vector3(a.X, a.Y, a.Z).Lerp(new Vector3(b.X, b.Y, b.Z), t);
        return new Transform3D(new Basis(qa.Slerp(qb, t)), p);
    }
}
