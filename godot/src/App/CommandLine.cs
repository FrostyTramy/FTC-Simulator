using System;
using System.Globalization;

namespace Sim.App;

/// <summary>User arguments after "--" on the Godot command line.
/// <code>
/// godot --path godot -- [--layout field|synthetic|res://...] [--quality low|medium]
///                       [--screenshot DIR] [--run-scenarios [FILTER]] [--seconds N]
///                       [--no-robot] [--teamcode JAR]
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

    /// <summary>Seconds of fps measurement per view in screenshot mode.</summary>
    public double MeasureSeconds { get; private set; } = 3.0;

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
