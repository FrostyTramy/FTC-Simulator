using System.IO;
using System.Linq;
using Godot;
using Sim.Field;
using Sim.Match;
using Sim.Robot;
using Sim.Testing;

namespace Sim.App;

/// <summary>Entry scene. Interactive by default; <c>--screenshot DIR</c> renders the preset views and quits;
/// <c>--run-scenarios [FILTER]</c> runs tests/scenarios headless; <c>--match-test</c> plays a scripted 2v2 MATCH and
/// <c>--verify-replay FILE</c> re-simulates a replay (see CommandLine). A new match setup, a rematch or a replay reloads
/// the scene so every MATCH starts from a fresh physics world.</summary>
public partial class Main : Node3D
{
    /// <summary>Setup for the next scene load (match setup dialog, rematch); null = from the command line.</summary>
    private static MatchSetup? _nextSetup;

    /// <summary>Replay to show at the next scene load.</summary>
    private static string? _nextReplay;

    private MatchSession? _session;

    /// <summary>Where match records and replays are kept (user://matches = %APPDATA%/BiobuzzSim/matches).</summary>
    public static string HistoryDir => ProjectSettings.GlobalizePath("user://matches");

    public override void _Ready()
    {
        var args = CommandLine.Parse(OS.GetCmdlineUserArgs());
        if (args.RunScenarios)
        {
            AddChild(new ScenarioRunner { Filter = args.ScenarioFilter });
            return;
        }
        if (args.VerifyReplay is { } verify)
        {
            AddChild(new VerifyReplay(verify, args.Layout));
            return;
        }
        if (args.MatchTest)
        {
            AddChild(new ScriptedMatch(args.Layout, args.MatchTestOut));
            return;
        }

        var (sun, env) = Arena.Create(this);
        QualitySettings.Apply(GetViewport(), sun, env, args.Quality);
        var hud = new Hud { Name = "Hud" };
        AddChild(hud);

        var physics = PhysicsConfig.Load(RepoPaths.Config("physics.yaml"));
        var layoutPath = RepoPaths.Layout(args.Layout);
        if (!FieldLoader.LayoutExists(layoutPath))
        {
            hud.Message = $"{layoutPath} not found (run tools/cad_pipeline/run.py or tools/standin/make_standin.py); showing the synthetic test layout";
            GD.PushWarning(hud.Message);
            layoutPath = RepoPaths.Layout("synthetic");
        }
        else if (RepoPaths.IsStandIn(layoutPath))
        {
            hud.Message = "STAND-IN field and robots (simple shapes, no CAD): run tools/cad_pipeline/run.py --all for the real ones";
        }
        var replay = _nextReplay ?? args.Replay;
        _nextReplay = null;
        var field = LoadField(this, layoutPath, physics, mechanisms: args.ScreenshotDir == null && replay == null);
        GD.Print($"loaded {layoutPath}: {field.StaticBodies.Count} static bodies, {field.RigidGroups.Count} rigid groups, " +
                 $"{field.Elements.Count} elements, {field.ColliderCount} collider shapes, ground at y={field.GroundY:F4}");

        var camera = new FreeCamera { Name = "FreeCamera" };
        AddChild(camera);
        camera.MakeCurrent();
        hud.Track(camera);

        if (replay != null)
        {
            AddChild(new ReplayViewer(field, replay, physics) { Name = "ReplayViewer" });
            return;
        }

        if (args.ScreenshotDir != null)
        {
            AddChild(new ScreenshotRunner
            {
                OutputDir = args.ScreenshotDir,
                MeasureSeconds = args.MeasureSeconds,
                Quality = args.Quality,
                Camera = camera,
                Field = field,
                LayoutPath = layoutPath,
            });
            return;
        }

        AddChild(new PracticeTools(field, camera, () => _session) { Name = "PracticeTools" });
        if (args.NoRobot)
        {
            return;
        }
        var input = new GamepadInput { Name = "GamepadInput" };
        AddChild(input);
        var setup = _nextSetup ?? (args.SetupFile is { } file ? MatchSetup.FromJson(File.ReadAllText(file)) : PracticeSetup(args));
        _nextSetup = null;
        _session = new MatchSession(field, setup, physics, input, HistoryDir);
        AddChild(_session);
        if (args.RobotPose is { } rp && _session.Stations.Count > 0)
        {
            _session.Stations[0].Robot.ResetTo(new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(rp.YawDeg)), new Vector3(rp.X, field.TileTopY, rp.Z)));
        }
        input.Focused = _session.Stations.FirstOrDefault()?.Station.Key();
        BuildUi(setup, args);
    }

    /// <summary>Loads the field with its hive hinges and AprilTag decals (the same way for live play and replays).</summary>
    public static LoadedField LoadField(Node parent, string layoutPath, PhysicsConfig physics, bool mechanisms = true)
    {
        var field = FieldLoader.Load(parent, layoutPath, physics);
        if (mechanisms)
        {
            // Hive hinges, dampers and tip detection (Phase 4); screenshots keep the static CAD pose.
            FieldMechanisms.Apply(field, RepoPaths.Config("field_mechanisms.yaml"), physics, RepoPaths.FieldManifest(layoutPath));
        }
        AprilTagDecals.Build(field, RepoPaths.Config("field_mechanisms.yaml"));
        if (physics.AssumedValues.Count > 0)
        {
            GD.Print("ASSUMED physics values: " + string.Join(", ", physics.AssumedValues));
        }
        return field;
    }

    /// <summary>Practice: one robot at Red 1 running its code (or driven by gamepad when the code is not available).</summary>
    private static MatchSetup PracticeSetup(CommandLine args)
    {
        var jar = args.TeamCodeJar ?? TeamCodeJars.DefaultFor(args.Robot);
        var kind = jar != null && File.Exists(jar) ? StationKind.Code : StationKind.Gamepad;
        return new MatchSetup([new StationSetup(Station.Red1, kind, args.Robot, "SIM 1", kind == StationKind.Code ? jar : null)],
            Label: "Practice");
    }

    private void BuildUi(MatchSetup setup, CommandLine args)
    {
        var session = _session!;
        var layer = new CanvasLayer { Name = "UiLayer" };
        AddChild(layer);
        var input = GetNode<GamepadInput>("GamepadInput");
        layer.AddChild(new DriverStationPanel(session, input) { Name = "DriverStation" });
        var scoreboard = new Scoreboard(session) { Name = "Scoreboard" };
        layer.AddChild(scoreboard);
        layer.AddChild(new FoulReviewPanel(session) { Name = "FoulReview" });
        var history = new MatchHistory(HistoryDir);
        scoreboard.RematchRequested += () => Reload(setup, null);
        scoreboard.HistoryRequested += () =>
        {
            var panel = new HistoryPanel(history, session.Controller.Rules);
            panel.ReplayRequested += path => Reload(null, path);
            layer.AddChild(panel);
        };
        scoreboard.SetupRequested += () =>
        {
            var robots = Directory.GetFiles(RepoPaths.Config("robots"), "*.yaml").Select(Path.GetFileNameWithoutExtension).OfType<string>().OrderBy(n => n).ToList();
            var panel = new MatchSetupPanel(setup, robots, TeamCodeJars.DefaultFor);
            panel.Applied += next => Reload(next, null);
            layer.AddChild(panel);
        };
        session.MatchFinished += record =>
        {
            var panel = new ResultsPanel(record, history, session.Controller.Rules);
            panel.ReplayRequested += path => Reload(null, path);
            layer.AddChild(panel);
        };

        // Code editor (F4) and git (F6) for the first station with robot code. The team project lives in workspace/
        // (git-ignored); the first run copies the robot's TeamCode there so the original resources are never edited.
        if (session.Stations.FirstOrDefault(s => s.Program != null) is { Program: { } program } coded)
        {
            var robot = coded.Setup.Robot;
            var ownCode = TeamCodeJars.HasOwnCode(robot);
            var codeName = ownCode ? TeamCodeJars.CodeName(robot) : "starter";
            var project = args.Project != null ? Path.GetFullPath(args.Project) : Path.Combine(RepoPaths.RepoRoot, "workspace", codeName);
            var tools = Path.Combine(RepoPaths.RepoRoot, "workspace", ".ftcsim", Path.GetFileName(project));
            if (args.Project == null)
            {
                SyncStarterProject(project, ownCode ? codeName : "gobilda_starter", tools);
            }
            layer.AddChild(new Sim.EditorUi.CodePanel(program, project, tools) { Name = "CodeEditor" });
            layer.AddChild(new Sim.EditorUi.GitPanel(project) { Name = "Git" });
            if (args.OpMode != null)
            {
                var check = new VisionCheck { Name = "VisionCheck", Program = program, Robot = coded.Robot, OpMode = args.OpMode };
                AddChild(check);
                if (args.CapturePath is { } capture)
                {
                    AddChild(new CaptureRunner { Path = capture, Seconds = args.CaptureSeconds, ShowPanels = args.CapturePanels, BeforeQuit = () => check.Report(capture) });
                    return;
                }
            }
        }
        if (args.CapturePath != null)
        {
            AddChild(new CaptureRunner { Path = args.CapturePath, Seconds = args.CaptureSeconds, ShowPanels = args.CapturePanels });
        }
    }

    /// <summary>Reloads the scene with a new setup or a replay to watch.</summary>
    private void Reload(MatchSetup? setup, string? replay)
    {
        _nextSetup = setup;
        _nextReplay = replay;
        GetTree().CallDeferred(SceneTree.MethodName.ReloadCurrentScene);
    }

    /// <summary>Copies the robot's TeamCode into the workspace project on first use and later brings in template
    /// updates for files the team has not edited (edited files are kept and listed).</summary>
    private static void SyncStarterProject(string project, string sourceName, string toolsDir)
    {
        var source = Path.Combine(RepoPaths.RepoRoot, "tests", "compat", sourceName, "TeamCode");
        var result = Sim.Editor.ProjectTemplate.Sync(source, Path.Combine(project, "TeamCode"), Path.Combine(toolsDir, "template.json"));
        if (result.Updated.Count > 0)
        {
            GD.Print($"{project}: updated from the template: {string.Join(", ", result.Updated)}");
        }
        if (result.KeptEdited.Count > 0)
        {
            GD.Print($"{project}: kept your edited files (the template has a newer version): {string.Join(", ", result.KeptEdited)}");
        }
    }
}
