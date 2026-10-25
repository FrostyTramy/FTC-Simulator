using System;
using Godot;
using Sim.Game.Match;

namespace Sim.App;

/// <summary>
/// Audience-style scoreboard (spec 08 "Scoreboard"): phase, field timer (transition countdown), alliance totals and the
/// key game counters (tips), plus match control buttons. Shows the live score of the field outside a match (practice).
/// </summary>
public partial class Scoreboard : PanelContainer
{
    private readonly MatchController _match;
    private Label _red = null!;
    private Label _blue = null!;
    private Label _timer = null!;
    private Label _detail = null!;
    private Button _start = null!;
    private Button _abort = null!;

    public Scoreboard(MatchController match)
    {
        _match = match;
    }

    /// <summary>Called when a match starts (the drive team presses PLAY on an initialized OpMode).</summary>
    public event Action? MatchStarted;

    /// <summary>The "Reset field" button: elements, hives and robot back to their start (key R does the same).</summary>
    public event Action? ResetRequested;

    public override void _Ready()
    {
        AnchorLeft = 0.5f;
        AnchorRight = 0.5f;
        OffsetLeft = -250;
        OffsetRight = 250;
        OffsetTop = 8;
        var box = new VBoxContainer();
        AddChild(box);
        var row = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _red = new Label { CustomMinimumSize = new Vector2(120, 0), HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(1f, 0.35f, 0.3f) };
        _timer = new Label { CustomMinimumSize = new Vector2(180, 0), HorizontalAlignment = HorizontalAlignment.Center };
        _blue = new Label { CustomMinimumSize = new Vector2(120, 0), HorizontalAlignment = HorizontalAlignment.Center, Modulate = new Color(0.4f, 0.6f, 1f) };
        foreach (var l in new[] { _red, _timer, _blue })
        {
            l.AddThemeFontSizeOverride("font_size", 22);
            row.AddChild(l);
        }
        box.AddChild(row);
        _detail = new Label { HorizontalAlignment = HorizontalAlignment.Center };
        box.AddChild(_detail);
        var buttons = new HBoxContainer { Alignment = BoxContainer.AlignmentMode.Center };
        _start = new Button { Text = "Start match", FocusMode = FocusModeEnum.None };
        _start.Pressed += () =>
        {
            _match.StartMatch();
            MatchStarted?.Invoke();
        };
        _abort = new Button { Text = "Abort", FocusMode = FocusModeEnum.None };
        _abort.Pressed += () => _match.Abort();
        var practice = new Button { Text = "Practice", FocusMode = FocusModeEnum.None };
        practice.Pressed += () => _match.Practice();
        buttons.AddChild(_start);
        buttons.AddChild(_abort);
        buttons.AddChild(practice);
        var reset = new Button { Text = "Reset field (R)", FocusMode = FocusModeEnum.None };
        reset.Pressed += () => ResetRequested?.Invoke();
        buttons.AddChild(reset);
        box.AddChild(buttons);
    }

    public override void _Process(double delta)
    {
        var director = _match.Director;
        var m = director.Match;
        var tick = _match.Tick;
        var live = _match.Live;
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
        _detail.Text = live is null ? "" : $"tips  red {live.Red.Tips} · blue {live.Blue.Tips}    penalty points  red {live.Red.PenaltyPoints} · blue {live.Blue.PenaltyPoints}";
        _start.Disabled = m.IsRunning || m.Phase is MatchPhase.MatchEnd or MatchPhase.PostMatchSettle;
        _abort.Disabled = !m.IsRunning;
    }
}
