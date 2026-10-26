using System;
using System.Globalization;

namespace Sim.App;

/// <summary>User arguments after "--" on the Godot command line.
/// <code>
/// godot --path godot -- [--layout field|synthetic|res://...] [--quality low|medium]
///                       [--screenshot DIR] [--run-scenarios [FILTER]] [--seconds N]
///                       [--no-robot] [--teamcode JAR] [--project DIR]
///                       [--robot NAME] [--robot-pose X,Z,YAW_DEG] [--opmode NAME] [--ball-path guided|physical]
///                       [--capture PNG [--capture-seconds N] [--capture-panels]]
///                       [--setup MATCH.json] [--replay FILE] [--match-test [OUT_DIR]] [--verify-replay FILE]
/// </code></summary>
public sealed class CommandLine
{
    public string Layout { get; private set; } = "field";
    public QualityPreset Quality { get; private set; } = QualityPreset.Medium;
    public string? ScreenshotDir { get; private set; }
    public bool RunScenarios { get; private set; }
    public string ScenarioFilter { get; private set; } = "";

    /// <summary>Interactive mode without the robot (field only).</summary>
    public bool NoRobot { get; private set; }

    /// <summary>Compiled TeamCode for the runner (default: the example built by java/build-adapter).</summary>
    public string? TeamCodeJar { get; private set; }

    /// <summary>Robot to spawn (config/robots/NAME.yaml).</summary>
    public string Robot { get; private set; } = "gobilda_starter";

    /// <summary>"guided" or "physical" overrides the robots' ball_path.model (practice); null keeps it.</summary>
    public string? BallPath { get; private set; }

    /// <summary>Robot start on the field: x, z (m) and heading (deg, counter-clockwise seen from above); null = default.</summary>
    public (float X, float Z, float YawDeg)? RobotPose { get; private set; }

    /// <summary>OpMode the scripted Driver Station INITs and starts (checks, see VisionCheck).</summary>
    public string? OpMode { get; private set; }

    /// <summary>Team project folder (contains TeamCode/src/main/java), e.g. a cloned FtcRobotController repository.</summary>
    public string? Project { get; private set; }

    /// <summary>Interactive app screenshot (CaptureRunner): output PNG, delay, open the code editor first.</summary>
    public string? CapturePath { get; private set; }
    public double CaptureSeconds { get; private set; } = 3;
    public bool CapturePanels { get; private set; }

    /// <summary>Seconds of fps measurement per view in screenshot mode.</summary>
    public double MeasureSeconds { get; private set; } = 3.0;

    /// <summary>Match setup to load (JSON, as saved in match records).</summary>
    public string? SetupFile { get; private set; }

    /// <summary>Replay file to watch.</summary>
    public string? Replay { get; private set; }

    /// <summary>Headless: play the scripted 2v2 acceptance MATCH (Phase 10) and save its record and replay.</summary>
    public bool MatchTest { get; private set; }

    /// <summary>Where the match test writes its record and replay (default reports/match).</summary>
    public string? MatchTestOut { get; private set; }

    /// <summary>Headless: re-simulate a replay and compare it with the recording.</summary>
    public string? VerifyReplay { get; private set; }

    public static CommandLine Parse(string[] args)
    {
        var cl = new CommandLine();
        for (var i = 0; i < args.Length; i++)
        {
            var next = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal) ? args[i + 1] : null;
            switch (args[i])
            {
                case "--layout" when next != null:
                    cl.Layout = next;
                    i++;
                    break;
                case "--quality" when next != null:
                    cl.Quality = next.Equals("low", StringComparison.OrdinalIgnoreCase) ? QualityPreset.Low : QualityPreset.Medium;
                    i++;
                    break;
                case "--screenshot" when next != null:
                    cl.ScreenshotDir = next;
                    i++;
                    break;
                case "--seconds" when next != null:
                    cl.MeasureSeconds = double.Parse(next, CultureInfo.InvariantCulture);
                    i++;
                    break;
                case "--no-robot":
                    cl.NoRobot = true;
                    break;
                case "--teamcode" when next != null:
                    cl.TeamCodeJar = next;
                    i++;
                    break;
                case "--robot" when next != null:
                    cl.Robot = next;
                    i++;
                    break;
                case "--ball-path" when next != null:
                    cl.BallPath = next.ToLowerInvariant() is "guided" or "physical" ? next.ToLowerInvariant()
                        : throw new ArgumentException($"--ball-path {next}: expected guided or physical");
                    i++;
                    break;
                case "--robot-pose" when next != null:
                    var p = next.Split(',');
                    cl.RobotPose = (float.Parse(p[0], CultureInfo.InvariantCulture), float.Parse(p[1], CultureInfo.InvariantCulture),
                        float.Parse(p[2], CultureInfo.InvariantCulture));
                    i++;
                    break;
                case "--opmode" when next != null:
                    cl.OpMode = next;
                    i++;
                    break;
                case "--project" when next != null:
                    cl.Project = next;
                    i++;
                    break;
                case "--capture" when next != null:
                    cl.CapturePath = next;
                    i++;
                    break;
                case "--capture-seconds" when next != null:
                    cl.CaptureSeconds = double.Parse(next, CultureInfo.InvariantCulture);
                    i++;
                    break;
                case "--capture-panels":
                    cl.CapturePanels = true;
                    break;
                case "--setup" when next != null:
                    cl.SetupFile = next;
                    i++;
                    break;
                case "--replay" when next != null:
                    cl.Replay = next;
                    i++;
                    break;
                case "--match-test":
                    cl.MatchTest = true;
                    if (next != null)
                    {
                        cl.MatchTestOut = next;
                        i++;
                    }
                    break;
                case "--verify-replay" when next != null:
                    cl.VerifyReplay = next;
                    i++;
                    break;
                case "--run-scenarios":
                    cl.RunScenarios = true;
                    if (next != null)
                    {
                        cl.ScenarioFilter = next;
                        i++;
                    }
                    break;
            }
        }
        return cl;
    }
}
