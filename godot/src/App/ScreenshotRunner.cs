using System.Collections.Generic;
using System.Globalization;
using System.Text.Json;
using Godot;
using Sim.Field;

namespace Sim.App;

/// <summary><c>-- --screenshot DIR</c>: renders every preset view to DIR/&lt;n&gt;_&lt;view&gt;.png,
/// measures fps per view with vsync off, writes DIR/stats.json and quits. Needs a window
/// (rendering does not run with --headless).</summary>
public partial class ScreenshotRunner : Node
{
    private const int WarmupFrames = 90;
    private const int SettleFrames = 30;

    private int _view = -1;
    private int _frames;
    private ulong _measureStartUsec;
    private int _measureFrames;
    private bool _measuring;
    private readonly List<Dictionary<string, object>> _results = new();

    public string OutputDir { get; set; } = "";
    public double MeasureSeconds { get; set; } = 3.0;
    public QualityPreset Quality { get; set; }
    public FreeCamera Camera { get; set; } = null!;
    public LoadedField? Field { get; set; }
    public string LayoutPath { get; set; } = "";

    public override void _Ready()
    {
        System.IO.Directory.CreateDirectory(OutputDir);
        DisplayServer.WindowSetVsyncMode(DisplayServer.VSyncMode.Disabled);
        Engine.MaxFps = 0;
    }

    public override void _Process(double delta)
    {
        _frames++;
        if (_view < 0)
        {
            if (_frames >= WarmupFrames)
            {
                NextView();
            }
            return;
        }
        if (!_measuring)
        {
            if (_frames < SettleFrames)
            {
                return;
            }
            var view = FreeCamera.Presets[_view];
            var path = System.IO.Path.Combine(OutputDir, $"{_view + 1}_{view}.png");
            GetViewport().GetTexture().GetImage().SavePng(path);
            _measuring = true;
            _measureFrames = 0;
            _measureStartUsec = Time.GetTicksUsec();
            return;
        }
        _measureFrames++;
        var elapsed = (Time.GetTicksUsec() - _measureStartUsec) / 1e6;
        if (elapsed < MeasureSeconds)
        {
            return;
        }
        var stats = FrameStats.Sample().ToJson();
        stats["view"] = FreeCamera.Presets[_view].ToString();
        stats["fps"] = System.Math.Round(_measureFrames / elapsed, 1);
        stats["frames_measured"] = _measureFrames;
        _results.Add(stats);
        GD.Print(string.Format(CultureInfo.InvariantCulture, "view {0}: {1} fps, {2} draw calls, {3:N0} primitives",
            stats["view"], stats["fps"], stats["draw_calls"], stats["primitives"]));
        NextView();
    }

    private void NextView()
    {
        _view++;
        _frames = 0;
        _measuring = false;
        if (_view >= FreeCamera.Presets.Length)
        {
            WriteStats();
            GetTree().Quit(0);
            return;
        }
        Camera.ApplyPreset(FreeCamera.Presets[_view]);
    }

    private void WriteStats()
    {
        var size = DisplayServer.WindowGetSize();
        var report = new Dictionary<string, object>
        {
            ["layout"] = LayoutPath,
            ["quality"] = Quality.ToString(),
            ["gpu"] = RenderingServer.GetVideoAdapterName(),
            ["gpu_vendor"] = RenderingServer.GetVideoAdapterVendor(),
            ["rendering_api"] = RenderingServer.GetVideoAdapterApiVersion(),
            ["window"] = new[] { size.X, size.Y },
            ["vsync"] = "disabled",
            ["physics_ticks_per_second"] = Engine.PhysicsTicksPerSecond,
            ["bodies"] = Field == null ? 0 : Field.StaticBodies.Count + Field.RigidGroups.Count + Field.Elements.Count,
            ["colliders"] = Field?.ColliderCount ?? 0,
            ["views"] = _results,
        };
        var json = JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true });
        System.IO.File.WriteAllText(System.IO.Path.Combine(OutputDir, "stats.json"), json);
        GD.Print($"screenshots and stats.json written to {OutputDir}");
    }
}
