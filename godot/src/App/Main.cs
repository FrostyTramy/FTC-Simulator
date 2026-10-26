using Godot;
using Sim.Field;
using Sim.Robot;
using Sim.Testing;

namespace Sim.App;

/// <summary>Entry scene. Interactive by default; <c>--screenshot DIR</c> renders the preset views
/// and quits; <c>--run-scenarios [FILTER]</c> runs tests/scenarios headless (see CommandLine).</summary>
public partial class Main : Node3D
{
    /// <summary>ASSUMED practice start pose: on the audience-side lane facing blue (+X), clear of the hive structure.</summary>
    private static readonly Vector3 PracticeStart = new(-1.3f, 0f, 1.05f);

    private RobotProgram? _program;
    private FieldReset? _reset;
    private VisionCheck? _visionCheck;

    public override void _Ready()
    {
        var args = CommandLine.Parse(OS.GetCmdlineUserArgs());
        if (args.RunScenarios)
        {
            AddChild(new ScenarioRunner { Filter = args.ScenarioFilter });
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
            hud.Message = $"{layoutPath} not found (run tools/cad_pipeline/run.py); showing the synthetic test layout";
            GD.PushWarning(hud.Message);
            layoutPath = RepoPaths.Layout("synthetic");
        }
        var field = FieldLoader.Load(this, layoutPath, physics);
        if (args.ScreenshotDir == null)
        {
            // Hive hinges, dampers and tip detection (Phase 4); screenshots keep the static CAD pose.
            FieldMechanisms.Apply(field, RepoPaths.Config("field_mechanisms.yaml"), physics,
                System.IO.Path.Combine(RepoPaths.RepoRoot, "generated", "field", "manifest.json"));
        }
        AprilTagDecals.Build(field, RepoPaths.Config("field_mechanisms.yaml"));
        if (physics.AssumedValues.Count > 0)
        {
            GD.Print("ASSUMED physics values: " + string.Join(", ", physics.AssumedValues));
        }
        GD.Print($"loaded {layoutPath}: {field.StaticBodies.Count} static bodies, {field.RigidGroups.Count} rigid groups, " +
                 $"{field.Elements.Count} elements, {field.ColliderCount} collider shapes, ground at y={field.GroundY:F4}");

        var camera = new FreeCamera { Name = "FreeCamera" };
        AddChild(camera);
        camera.MakeCurrent();
        hud.Track(camera);

        if (args.ScreenshotDir == null)
        {
            AddChild(new PracticeTools(field, camera) { Name = "PracticeTools" });
        }

        if (!args.NoRobot && args.ScreenshotDir == null)
        {
            var input = new GamepadInput { Name = "GamepadInput" };
            AddChild(input);
            var pose = args.RobotPose is { } rp
                ? new Transform3D(new Basis(Vector3.Up, Mathf.DegToRad(rp.YawDeg)), new Vector3(rp.X, field.TileTopY, rp.Z))
                : new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 2), PracticeStart + new Vector3(0, field.TileTopY, 0));
            var robot = RobotBody.Spawn(this, args.Robot, pose, physics);
            robot.Elements = field.Elements;
            _reset = new FieldReset(field, robot);
            // A robot with its own TeamCode (tests/compat/<robot>) runs that code (built by tools/build_teamcode.ps1).
            var codeRobot = robot.Definition.CadPath.Replace("generated/", "");
            var hasOwnCode = codeRobot != "gobilda_starter"
                && System.IO.Directory.Exists(System.IO.Path.Combine(RepoPaths.RepoRoot, "tests", "compat", codeRobot, "TeamCode"));
            var jar = args.TeamCodeJar ?? (hasOwnCode
                ? System.IO.Path.Combine(RepoPaths.RepoRoot, "generated", "build", codeRobot, "libs", "teamcode.jar")
                : System.IO.Path.Combine(RepoPaths.RepoRoot, "java", "build-adapter", "build", "libs", "teamcode.jar"));
            var ownSources = System.IO.Path.Combine(RepoPaths.RepoRoot, "tests", "compat", codeRobot, "TeamCode", "src", "main", "java");
            if (args.TeamCodeJar == null && hasOwnCode && !Sim.Runner.TeamCodeBuilder.IsUpToDate(ownSources, jar))
            {
                var built = Sim.Runner.TeamCodeBuilder.Build(ownSources, System.IO.Path.Combine(RepoPaths.RepoRoot, "generated", "build", codeRobot));
                GD.Print(built.Success ? $"built {jar}" : $"TeamCode build failed: {built.Output}");
            }
            _program = new RobotProgram(robot, input, jar);
            _program.Connect();
            var layer = new CanvasLayer { Name = "DriverStationLayer" };
            AddChild(layer);
            layer.AddChild(new DriverStationPanel(_program, robot, input) { Name = "DriverStation" });

            var match = new MatchController(field, new[] { new MatchRobot(1, Sim.Game.Alliance.Red, "SIM 1", robot) }, physics) { Name = "Match" };
            AddChild(match);
            var program = _program;
            match.AutoEnded += () =>
            {
                if (program.Ds.Selected is { Autonomous: true } && program.Ds.CanStop)
                {
                    program.Ds.Stop();
                }
            };
            var scoreboard = new Scoreboard(match) { Name = "Scoreboard" };
            scoreboard.MatchStarted += () =>
            {
                if (program.Ds.CanPlay)
                {
                    program.Ds.Play(robot.NowNanos);   // the drive team starts its INIT-ed OpMode with the match
                }
            };
            scoreboard.ResetRequested += () => _reset?.Apply();
            layer.AddChild(scoreboard);

            // Code editor (F4): the team project lives in workspace/ (git-ignored); the first run copies the robot's
            // TeamCode (tests/compat/<robot>, or the goBILDA example) there so the original resources are never edited.
            var project = args.Project != null ? System.IO.Path.GetFullPath(args.Project)
                : System.IO.Path.Combine(RepoPaths.RepoRoot, "workspace", hasOwnCode ? codeRobot : "starter");
            var tools = System.IO.Path.Combine(RepoPaths.RepoRoot, "workspace", ".ftcsim", System.IO.Path.GetFileName(project));
            if (args.Project == null)
            {
                SyncStarterProject(project, hasOwnCode ? codeRobot : "gobilda_starter", tools);
            }
            layer.AddChild(new Sim.EditorUi.CodePanel(program, project, tools) { Name = "CodeEditor" });
            layer.AddChild(new Sim.EditorUi.GitPanel(project) { Name = "Git" });
            if (args.OpMode != null)
            {
                _visionCheck = new VisionCheck { Name = "VisionCheck", Program = program, Robot = robot, OpMode = args.OpMode };
                AddChild(_visionCheck);
            }
        }

        if (args.ScreenshotDir == null)
        {
            _reset ??= new FieldReset(field, null);
        }

        if (args.CapturePath != null)
        {
            var check = _visionCheck;
            var capturePath = args.CapturePath;
            AddChild(new CaptureRunner
            {
                Path = args.CapturePath, Seconds = args.CaptureSeconds, ShowPanels = args.CapturePanels,
                BeforeQuit = () => check?.Report(capturePath),
            });
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
        }
    }

    /// <summary>Copies tests/compat/&lt;source&gt;/TeamCode into a new workspace project (once).</summary>
    /// <summary>Copies the robot's TeamCode into the workspace project on first use and later brings in template
    /// updates for files the team has not edited (edited files are kept and listed).</summary>
    private static void SyncStarterProject(string project, string sourceName, string toolsDir)
    {
        var source = System.IO.Path.Combine(RepoPaths.RepoRoot, "tests", "compat", sourceName, "TeamCode");
        var result = Sim.Editor.ProjectTemplate.Sync(source, System.IO.Path.Combine(project, "TeamCode"),
            System.IO.Path.Combine(toolsDir, "template.json"));
        if (result.Updated.Count > 0)
        {
            GD.Print($"{project}: updated from the template: {string.Join(", ", result.Updated)}");
        }
        if (result.KeptEdited.Count > 0)
        {
            GD.Print($"{project}: kept your edited files (the template has a newer version): {string.Join(", ", result.KeptEdited)}");
        }
    }

    /// <summary>R: reset the field and the robot (practice), unless a text field has the keyboard.</summary>
    public override void _UnhandledKeyInput(InputEvent @event)
    {
        if (@event is InputEventKey { Pressed: true, Echo: false, Keycode: Key.R })
        {
            _reset?.Apply();
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _ExitTree()
    {
        _program?.Dispose();   // QUIT to the runner JVM
    }
}
