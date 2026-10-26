using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Godot;
using Sim.App;
using Sim.Game;
using Sim.Game.Fouls;
using Sim.Game.Scoring;

namespace Sim.Match;

/// <summary>Shared look of the match dialogs: a centred panel with a title, scrollable content and a button row.</summary>
public abstract partial class MatchDialog : PanelContainer
{
    protected VBoxContainer Body { get; private set; } = null!;
    protected HBoxContainer Buttons { get; private set; } = null!;

    protected abstract string Title { get; }

    public override void _Ready()
    {
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        AnchorTop = 0.5f;
        AnchorBottom = 0.5f;
        OffsetLeft = -460;
        OffsetRight = 460;
        OffsetTop = -330;
        OffsetBottom = 330;
        var outer = new VBoxContainer();
        AddChild(outer);
        var title = new Label { Text = Title, HorizontalAlignment = HorizontalAlignment.Center };
        title.AddThemeFontSizeOverride("font_size", 20);
        outer.AddChild(title);
        var scroll = new ScrollContainer { SizeFlagsVertical = SizeFlags.ExpandFill, HorizontalScrollMode = ScrollContainer.ScrollMode.Disabled };
        Body = new VBoxContainer { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        scroll.AddChild(Body);
        outer.AddChild(scroll);
        Buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        outer.AddChild(Buttons);
        Build();
        AddButton("Close", QueueFree);
    }

    protected abstract void Build();

    protected Button AddButton(string text, Action action, Container? parent = null)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        b.Pressed += action;
        (parent ?? Buttons).AddChild(b);
        return b;
    }

    protected static Label Cell(string text, Color? colour = null, bool bold = false)
    {
        var l = new Label { Text = text, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        if (colour is { } c)
        {
            l.Modulate = c;
        }
        if (bold)
        {
            l.AddThemeFontSizeOverride("font_size", 17);
        }
        return l;
    }

    protected static readonly Color Red = new(1f, 0.45f, 0.4f);
    protected static readonly Color Blue = new(0.5f, 0.68f, 1f);
}

/// <summary>
/// Post-match screen (spec 08 "Scoreboard and results"): the score breakdown in the structure of the official sheet
/// (Table 10-2), ranking points, every foul with its MATCH time and status, robot statistics, CSV export and the replay.
/// </summary>
public partial class ResultsPanel : MatchDialog
{
    private readonly MatchRecord _record;
    private readonly MatchHistory _history;
    private readonly GameRules _rules;

    public ResultsPanel(MatchRecord record, MatchHistory history, GameRules rules)
    {
        _record = record;
        _history = history;
        _rules = rules;
    }

    /// <summary>"Watch replay" was pressed: path of the replay file.</summary>
    public event Action<string>? ReplayRequested;

    protected override string Title => $"{_record.Label}: {_record.Summary}";

    protected override void Build()
    {
        var r = _record.Score.Red;
        var b = _record.Score.Blue;
        var grid = new GridContainer { Columns = 3 };
        void Row(string what, string red, string blue, bool bold = false)
        {
            grid.AddChild(Cell(what, bold: bold));
            grid.AddChild(Cell(red, Red, bold));
            grid.AddChild(Cell(blue, Blue, bold));
        }
        Row("", "RED", "BLUE", true);
        Row("AUTO", "", "", true);
        Row("  LEAVE", $"{r.LeavePoints}", $"{b.LeavePoints}");
        Row("  PARK", $"{r.AutoParkPoints}", $"{b.AutoParkPoints}");
        Row("  HIVE TIPS", $"{r.AutoTips} ({r.AutoTipPoints})", $"{b.AutoTips} ({b.AutoTipPoints})");
        Row("  AUTO points", $"{r.AutoPoints}", $"{b.AutoPoints}", true);
        Row("TELEOP", "", "", true);
        Row("  HIVE TIPS", $"{r.TeleopTips} ({r.TeleopTipPoints})", $"{b.TeleopTips} ({b.TeleopTipPoints})");
        Row("  elements in CELLS", $"{r.CellPoints}", $"{b.CellPoints}");
        Row("  owned FLOWERS", $"{r.FlowerOwnedPoints}", $"{b.FlowerOwnedPoints}");
        Row("  bottom NECTAR bonus", $"{r.BottomNectarPoints}", $"{b.BottomNectarPoints}");
        Row("  GARDEN", $"{r.GardenPoints}", $"{b.GardenPoints}");
        Row("  PARK", $"{r.TeleopParkPoints}", $"{b.TeleopParkPoints}");
        Row("  TELEOP points", $"{r.TeleopPoints}", $"{b.TeleopPoints}", true);
        Row("PENALTIES", "", "", true);
        Row("  MINOR / MAJOR fouls by the opponent", $"{r.MinorFoulsByOpponent} / {r.MajorFoulsByOpponent}", $"{b.MinorFoulsByOpponent} / {b.MajorFoulsByOpponent}");
        Row("  penalty points", $"{r.PenaltyPoints}", $"{b.PenaltyPoints}");
        Row("TOTAL", $"{r.Total}", $"{b.Total}", true);
        Row("RANKING POINTS", $"{r.RankingPoints}", $"{b.RankingPoints}", true);
        Row("  SWARM · POLLINATOR 1 · 2", Rp(r), Rp(b));
        Body.AddChild(grid);

        Body.AddChild(new HSeparator());
        Body.AddChild(Cell($"Fouls ({_record.Fouls.Count})", bold: true));
        foreach (var f in _record.Fouls)
        {
            Body.AddChild(Cell($"{Clock(f.Tick)}  {f.Rule}  {f.Alliance.Key().ToUpperInvariant()} robot {f.RobotId?.ToString(CultureInfo.InvariantCulture) ?? "-"}  " +
                               $"{f.Penalty}{(f.Count > 1 ? " ×" + f.Count : "")}{(f.Card != Card.None ? " " + f.Card + " card" : "")}  [{f.Status}]  {f.Evidence}",
                f.Alliance == Alliance.Red ? Red : Blue));
        }
        Body.AddChild(new HSeparator());
        Body.AddChild(Cell("Scoring", bold: true));
        foreach (var e in _record.Scoring.Where(e => e.Category is ScoreCategory.Leave or ScoreCategory.HiveTip or ScoreCategory.AutoPark))
        {
            Body.AddChild(Cell($"{Clock(e.Tick)}  {e.Category}  {e.Alliance.Key().ToUpperInvariant()}  +{e.Points}  {e.Rule}", e.Alliance == Alliance.Red ? Red : Blue));
        }
        Body.AddChild(new HSeparator());
        Body.AddChild(Cell("Robots", bold: true));
        var robots = new GridContainer { Columns = 7 };
        foreach (var h in new[] { "Station", "Team", "Distance", "Intakes", "Launches", "Battery (min)", "Peak current" })
        {
            robots.AddChild(Cell(h, bold: true));
        }
        foreach (var s in _record.Robots)
        {
            var c = s.Station.Alliance() == Alliance.Red ? Red : Blue;
            robots.AddChild(Cell(s.Station.Label(), c));
            robots.AddChild(Cell(s.Team));
            robots.AddChild(Cell($"{s.DistanceM:0.0} m"));
            robots.AddChild(Cell($"{s.Intakes}"));
            robots.AddChild(Cell($"{s.Launches}"));
            robots.AddChild(Cell($"{s.BatteryEndV:0.00} V ({s.BatteryMinV:0.00})"));
            robots.AddChild(Cell($"{s.PeakCurrentA:0} A"));
        }
        Body.AddChild(robots);

        var exported = Cell("");
        Body.AddChild(exported);
        AddButton("Export CSV", () => exported.Text = "Saved " + _history.ExportCsv(_record));
        var replay = _record.ReplayFile is { } file ? Path.Combine(_history.Directory, file) : null;
        if (replay != null && File.Exists(replay))
        {
            AddButton("Watch replay", () => ReplayRequested?.Invoke(replay));
        }
    }

    private static string Rp(AllianceScore a) => $"{(a.SwarmRp ? "✔" : "–")} · {(a.Pollinator1Rp ? "✔" : "–")} · {(a.Pollinator2Rp ? "✔" : "–")}";

    private string Clock(long tick) =>
        MatchRecord.Clock(tick, _record.MatchStartTick, _record.TickRate, _rules.AutoSeconds, _rules.TransitionSeconds, _rules.TeleopSeconds);
}

/// <summary>Saved matches (spec 08 "Match history"), newest first: results, CSV export and replay for each.</summary>
public partial class HistoryPanel : MatchDialog
{
    private readonly MatchHistory _history;
    private readonly GameRules _rules;

    public HistoryPanel(MatchHistory history, GameRules rules)
    {
        _history = history;
        _rules = rules;
    }

    public event Action<string>? ReplayRequested;

    protected override string Title => "Match history";

    protected override void Build()
    {
        var list = _history.List();
        if (list.Count == 0)
        {
            Body.AddChild(Cell($"No saved matches yet ({_history.Directory})."));
            return;
        }
        foreach (var m in list)
        {
            var row = new HBoxContainer();
            row.AddChild(Cell($"{m.RecordedUtc.ToLocalTime():yyyy-MM-dd HH:mm}  {m.Label}  {m.Summary}"));
            AddButton("Results", () =>
            {
                var panel = new ResultsPanel(_history.Load(m.Id), _history, _rules);
                panel.ReplayRequested += p => ReplayRequested?.Invoke(p);
                GetParent().AddChild(panel);
            }, row);
            if (m.ReplayPath is { } replay)
            {
                AddButton("Replay", () => ReplayRequested?.Invoke(replay), row);
            }
            Body.AddChild(row);
        }
    }
}

/// <summary>
/// Match setup (spec 08 "Match setup"): per station who drives (robot code, gamepad, placeholder or nobody), the robot,
/// the team label and the OpModes; the foul mode (Strict / Assist / Off) and the automatic human player.
/// </summary>
public partial class MatchSetupPanel : MatchDialog
{
    private readonly MatchSetup _current;
    private readonly IReadOnlyList<string> _robots;
    private readonly Func<string, string?> _jarFor;
    private readonly Dictionary<Station, (OptionButton Kind, OptionButton Robot, LineEdit Team, LineEdit Auto, LineEdit TeleOp)> _rows = new();
    private OptionButton _fouls = null!;
    private CheckBox _autoHuman = null!;
    private OptionButton _ballPath = null!;
    private LineEdit _label = null!;

    public MatchSetupPanel(MatchSetup current, IReadOnlyList<string> robots, Func<string, string?> jarFor)
    {
        _current = current;
        _robots = robots;
        _jarFor = jarFor;
    }

    /// <summary>"Apply": the new setup (the field and robots are rebuilt with it).</summary>
    public event Action<MatchSetup>? Applied;

    protected override string Title => "Match setup";

    protected override void Build()
    {
        var grid = new GridContainer { Columns = 6 };
        foreach (var h in new[] { "Station", "Driver", "Robot", "Team", "AUTO OpMode", "TeleOp OpMode" })
        {
            grid.AddChild(Cell(h, bold: true));
        }
        foreach (var station in Enum.GetValues<Station>())
        {
            var s = _current.Of(station) ?? new StationSetup(station, StationKind.Empty);
            grid.AddChild(Cell(station.Label(), station.Alliance() == Alliance.Red ? Red : Blue));
            var kind = new OptionButton { FocusMode = FocusModeEnum.None };
            foreach (var k in Enum.GetValues<StationKind>())
            {
                kind.AddItem(k switch
                {
                    StationKind.Code => "Robot code",
                    StationKind.Gamepad => "Gamepad (no code)",
                    StationKind.Placeholder => "Placeholder",
                    _ => "Empty",
                });
            }
            kind.Select((int)s.Kind);
            var robot = new OptionButton { FocusMode = FocusModeEnum.None };
            foreach (var r in _robots)
            {
                robot.AddItem(r);
            }
            robot.Select(Math.Max(0, _robots.ToList().IndexOf(s.Robot)));
            var team = new LineEdit { Text = s.Team, PlaceholderText = station.Label(), CustomMinimumSize = new Vector2(110, 0) };
            var auto = new LineEdit { Text = s.AutoOpMode ?? "", PlaceholderText = "none", CustomMinimumSize = new Vector2(150, 0) };
            var teleOp = new LineEdit { Text = s.TeleOpMode ?? "", PlaceholderText = "preselected", CustomMinimumSize = new Vector2(150, 0) };
            foreach (var c in new Control[] { kind, robot, team, auto, teleOp })
            {
                grid.AddChild(c);
            }
            _rows[station] = (kind, robot, team, auto, teleOp);
        }
        Body.AddChild(grid);
        var options = new HBoxContainer();
        options.AddChild(Cell("Fouls"));
        _fouls = new OptionButton { FocusMode = FocusModeEnum.None, TooltipText = "Strict: detected fouls apply. Assist: a referee confirms each. Off: none." };
        foreach (var m in Enum.GetValues<FoulMode>())
        {
            _fouls.AddItem(m.ToString());
        }
        _fouls.Select((int)_current.FoulMode);
        options.AddChild(_fouls);
        _autoHuman = new CheckBox { Text = "Automatic human players", ButtonPressed = _current.AutoHumanPlayer, FocusMode = FocusModeEnum.None };
        options.AddChild(_autoHuman);
        options.AddChild(Cell("Ball path"));
        _ballPath = new OptionButton
        {
            FocusMode = FocusModeEnum.None,
            TooltipText = "Physical: elements move through the robot by its rollers and colliders and can jam. Guided: the robot stores " +
                          "and feeds them by rule. Robot default: each robot's config (ball_path.model).",
        };
        _ballPath.AddItem("Robot default");
        foreach (var mode in MatchSetup.BallPaths)
        {
            _ballPath.AddItem(mode);
        }
        _ballPath.Select(_current.BallPath is { } chosen ? Array.IndexOf(MatchSetup.BallPaths, chosen) + 1 : 0);
        options.AddChild(_ballPath);
        options.AddChild(Cell("Label"));
        _label = new LineEdit { Text = _current.Label, CustomMinimumSize = new Vector2(180, 0) };
        options.AddChild(_label);
        Body.AddChild(options);
        Body.AddChild(Cell("Robot code runs the robot's TeamCode (built automatically). OpMode names as the Driver Station lists them; " +
                           "an AUTO OpMode's preselected TeleOp is used when TeleOp is empty."));
        AddButton("Apply", Apply);
    }

    private void Apply()
    {
        var stations = new List<StationSetup>();
        foreach (var (station, row) in _rows)
        {
            var kind = (StationKind)row.Kind.Selected;
            var robot = _robots[Math.Max(0, row.Robot.Selected)];
            stations.Add(new StationSetup(station, kind, robot, row.Team.Text.Trim(), kind == StationKind.Code ? _jarFor(robot) : null,
                Blank(row.Auto.Text), Blank(row.TeleOp.Text)));
        }
        Applied?.Invoke(_current with
        {
            Stations = stations, FoulMode = (FoulMode)_fouls.Selected, AutoHumanPlayer = _autoHuman.ButtonPressed, Label = _label.Text.Trim(),
            BallPath = _ballPath.Selected > 0 ? MatchSetup.BallPaths[_ballPath.Selected - 1] : null,
        });
        QueueFree();
    }

    private static string? Blank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();
}

/// <summary>Assist mode (spec 03 "Fouls and penalties"): the referee confirms or rejects each detected foul.</summary>
public partial class FoulReviewPanel : PanelContainer
{
    private readonly MatchSession _session;
    private VBoxContainer _list = null!;
    private int _shownVersion = -1;

    public FoulReviewPanel(MatchSession session)
    {
        _session = session;
    }

    public override void _Ready()
    {
        AnchorTop = 1;
        AnchorBottom = 1;
        OffsetLeft = 8;
        OffsetRight = 520;
        OffsetTop = -260;
        OffsetBottom = -8;
        var box = new VBoxContainer();
        box.AddChild(new Label { Text = "Referee review (Assist mode)" });
        _list = new VBoxContainer();
        box.AddChild(_list);
        AddChild(box);
    }

    public override void _Process(double delta)
    {
        var book = _session.Director.Fouls;
        var pending = book.PendingReview.ToList();
        Visible = book.Mode == FoulMode.Assist && pending.Count > 0;
        var version = book.Records.Count * 1000 + pending.Count;
        if (!Visible || version == _shownVersion)
        {
            return;
        }
        _shownVersion = version;
        foreach (var c in _list.GetChildren())
        {
            c.QueueFree();
        }
        foreach (var f in pending.TakeLast(6))
        {
            var row = new HBoxContainer();
            row.AddChild(new Label
            {
                Text = $"{f.Rule} {f.Alliance.Key()} {f.Penalty}: {f.Evidence}",
                SizeFlagsHorizontal = SizeFlags.ExpandFill,
                ClipText = true,
                TooltipText = $"{f.Source}: {f.Evidence}",
            });
            var id = f.Id;
            foreach (var (text, confirm) in new[] { ("Confirm", true), ("Reject", false) })
            {
                var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
                b.Pressed += () => _session.DecideFoul(id, confirm);
                row.AddChild(b);
            }
            _list.AddChild(row);
        }
    }
}
