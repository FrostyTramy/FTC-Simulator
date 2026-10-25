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
            var pose = new Transform3D(new Basis(Vector3.Up, -Mathf.Pi / 2), PracticeStart + new Vector3(0, field.GroundY, 0));
            var robot = RobotBody.Spawn(this, "gobilda_starter", pose, physics);
            robot.Elements = field.Elements;
            var jar = args.TeamCodeJar ?? System.IO.Path.Combine(RepoPaths.RepoRoot, "java", "build-adapter", "build", "libs", "teamcode.jar");
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
            layer.AddChild(scoreboard);
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

    public override void _ExitTree()
    {
        _program?.Dispose();   // QUIT to the runner JVM
    }
}
