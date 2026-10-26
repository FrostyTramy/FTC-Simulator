using System;
using System.Linq;
using Godot;
using Sim.Game;
using Sim.Game.Match;
using Sim.Match;

namespace Sim.App;

/// <summary>
/// Audience-style scoreboard (spec 08 "Scoreboard"): phase, field timer (transition countdown), alliance totals and the
/// key game counters (tips, NECTAR left with the human players), match control (start, abort, practice, reset), the
/// human players' NECTAR entry (H red, J blue) and the reasons a MATCH cannot start (G304). Shows the live score of the
/// field outside a MATCH (practice). Colour-blind safe: alliances are also labelled in text.
/// </summary>
public partial class Scoreboard : PanelContainer
{
    private readonly MatchSession _session;
    private Label _red = null!;
    private Label _blue = null!;
    private Label _timer = null!;
    private Label _detail = null!;
    private Label _problems = null!;
    private Button _start = null!;
    private Button _abort = null!;
    private Button _redNectar = null!;
    private Button _blueNectar = null!;

    public Scoreboard(MatchSession session)
    {
        _session = session;
    }

    /// <summary>"Setup…": the match setup dialog.</summary>
    public event Action? SetupRequested;

    /// <summary>"History…": saved matches.</summary>
    public event Action? HistoryRequested;

    /// <summary>"New match": a fresh session with the same setup (one session plays one MATCH).</summary>
    public event Action? RematchRequested;

    /// <summary>"Ball path": a fresh session with the other ball path (physical ⇄ guided).</summary>
    public event Action? BallPathToggled;

    private Button _ballPath = null!;

    public override void _Ready()
    {
        // Between the HUD text (left) and the Driver Station panel (right, 380 px).
        AnchorLeft = 0;
        AnchorRight = 1;
        OffsetLeft = 300;
        OffsetRight = -396;
        OffsetTop = 8;
        var box = new VBoxContainer();
        AddChild(box);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _red = new Label { CustomMinimumSize = new Vector2(150, 0), HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 0.35f, 0.3f) };
        _timer = new Label { CustomMinimumSize = new Vector2(200, 0), HorizontalAlignment = HorizontalAlignment.Center };
        _blue = new Label { CustomMinimumSize = new Vector2(150, 0), HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.4f, 0.6f, 1f) };
        foreach (var l in new[] { _red, _timer, _blue })
        {
            l.AddThemeFontSizeOverride("font_size", 22);
            row.AddChild(l);
        }
        box.AddChild(row);
        _detail = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart };
        box.AddChild(_detail);
        _problems = new Label { HorizontalAlignment = HorizontalAlignment.Center, AutowrapMode = TextServer.AutowrapMode.WordSmart, Modulate = new Color(1f, 0.75f, 0.3f) };
        box.AddChild(_problems);

        var buttons = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.Center };
        _start = Make(buttons, "Start match", "Starts the MATCH: AUTO, transition, TELEOP (G304 setup checked)", () => _session.StartMatch());
        _abort = Make(buttons, "Abort", "Stops the MATCH (foghorn); robots are disabled", () => _session.Abort());
        Make(buttons, "Practice", "Back to practice: no timer, robots enabled", () => _session.Practice());
        Make(buttons, "Reset (R)", "Elements, hives and robots back to the start layout", () => _session.ResetField());
        Make(buttons, "New match", "A fresh field and robots with the same setup", () => RematchRequested?.Invoke());
        Make(buttons, "Setup…", "Robots, drivers, code and rules for the next MATCH", () => SetupRequested?.Invoke());
        Make(buttons, "History…", "Saved matches: results, CSV, replays", () => HistoryRequested?.Invoke());
        _ballPath = Make(buttons, "Ball path", "Physical: elements move through the robot by its rollers and can jam. Guided: the robot " +
                                              "stores and feeds them by rule. Switching starts a fresh session.", () => BallPathToggled?.Invoke());
        box.AddChild(buttons);

        var hp = new HFlowContainer { Alignment = FlowContainer.AlignmentMode.Center };
        _redNectar = Make(hp, "Red NECTAR (H)", "The red human player enters a NECTAR into the LOADING ZONE (G426/G427)", () => _session.EnterNectar(Alliance.Red));
        _blueNectar = Make(hp, "Blue NECTAR (J)", "The blue human player enters a NECTAR into the LOADING ZONE (G426/G427)", () => _session.EnterNectar(Alliance.Blue));
        box.AddChild(hp);
    }

    public override void _UnhandledKeyInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key || UiFocus.Typing(this))
        {
            return;
        }
        switch (key.Keycode)
        {
            case Key.H:
                _session.EnterNectar(Alliance.Red);
                break;
            case Key.J:
                _session.EnterNectar(Alliance.Blue);
                break;
            case Key.R:
                _session.ResetField();
                break;
            default:
                return;
        }
        GetViewport().SetInputAsHandled();
    }

    public override void _Process(double delta)
    {
        var director = _session.Director;
        var m = director.Match;
        var tick = _session.Controller.Tick;
        var live = _session.Controller.Live;
        _red.Text = live is null ? "RED –" : $"RED {live.Red.Total}";
        _blue.Text = live is null ? "– BLUE" : $"{live.Blue.Total} BLUE";
        _timer.Text = m.Phase switch
        {
            MatchPhase.PreMatch => "PRACTICE",
            MatchPhase.Transition => $"TRANSITION {m.TransitionCountdownDisplay(tick)}",
            MatchPhase.Auto => $"AUTO {m.TimerDisplay(tick)}",
            MatchPhase.Teleop => $"TELEOP {m.TimerDisplay(tick)}",
            MatchPhase.PostMatchSettle => "0:00 settling",
            MatchPhase.Results => "FINAL",
            _ => m.Phase.ToString().ToUpperInvariant(),
        };
        var hp = director.HumanPlayer;
        _detail.Text = live is null ? "" :
            $"tips  red {live.Red.Tips} · blue {live.Blue.Tips}    penalty points  red {live.Red.PenaltyPoints} · blue {live.Blue.PenaltyPoints}" +
            $"    NECTAR with the human players  red {hp.Reserve(Alliance.Red) + hp.Returned(Alliance.Red)} · blue {hp.Reserve(Alliance.Blue) + hp.Returned(Alliance.Blue)}";
        var problems = _session.SetupProblems;
        _problems.Text = m.Phase == MatchPhase.PreMatch && problems.Count > 0
            ? "Cannot start: " + string.Join("; ", problems.Take(3).Select(p => $"{p.Rule} {p.Message}")) + (problems.Count > 3 ? " …" : "")
            : "";
        _start.Disabled = m.Phase != MatchPhase.PreMatch || problems.Count > 0 || _session.LastRecord != null;
        _abort.Disabled = !m.IsRunning;
        var physical = _session.Stations.Any(s => s.Robot.PhysicalBallPath);
        _ballPath.Text = physical ? "Ball path: physical" : "Ball path: guided";
        _ballPath.Disabled = m.IsRunning || _session.Stations.All(s => !s.Robot.HasBallPath);
        var open = m.NectarAllEntryOpen(tick);
        _redNectar.Disabled = !m.IsRunning || hp.AllowedNow(Alliance.Red, open) == 0;
        _blueNectar.Disabled = !m.IsRunning || hp.AllowedNow(Alliance.Blue, open) == 0;
    }

    private static Button Make(Container parent, string text, string tooltip, Action action)
    {
        var b = new Button { Text = text, TooltipText = tooltip, FocusMode = FocusModeEnum.None };
        b.Pressed += action;
        parent.AddChild(b);
        return b;
    }
}
