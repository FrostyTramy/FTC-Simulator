using Godot;
using Sim.Field;
using Sim.Testing;

namespace Sim.App;

/// <summary>Entry scene. Interactive by default; <c>--screenshot DIR</c> renders the preset views
/// and quits; <c>--run-scenarios [FILTER]</c> runs tests/scenarios headless (see CommandLine).</summary>
public partial class Main : Node3D
{
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
}
