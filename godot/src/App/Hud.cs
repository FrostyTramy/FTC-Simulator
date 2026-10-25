using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;

namespace Sim.App;

/// <summary>Frame statistics read from the rendering server and performance monitors.</summary>
public readonly record struct FrameStats(double Fps, ulong DrawCalls, ulong Primitives, ulong Objects, double PhysicsMs, double ProcessMs)
{
    public static FrameStats Sample() => new(
        Engine.GetFramesPerSecond(),
        RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalDrawCallsInFrame),
        RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalPrimitivesInFrame),
        RenderingServer.GetRenderingInfo(RenderingServer.RenderingInfo.TotalObjectsInFrame),
        Performance.GetMonitor(Performance.Monitor.TimePhysicsProcess) * 1000.0,
        Performance.GetMonitor(Performance.Monitor.TimeProcess) * 1000.0);

    public Dictionary<string, object> ToJson() => new()
    {
        ["fps"] = System.Math.Round(Fps, 1),
        ["draw_calls"] = DrawCalls,
        ["primitives"] = Primitives,
        ["objects"] = Objects,
        ["physics_ms"] = System.Math.Round(PhysicsMs, 3),
        ["process_ms"] = System.Math.Round(ProcessMs, 3),
    };
}

/// <summary>Top-left overlay: fps, draw calls, primitives, physics time, camera position.</summary>
public partial class Hud : CanvasLayer
{
    private Label _label = null!;
    private double _accum;
    private Camera3D? _camera;

    public string Message { get; set; } = "";

    public void Track(Camera3D camera) => _camera = camera;

    public override void _Ready()
    {
        _label = new Label
        {
            Position = new Vector2(10, 8),
            Modulate = new Color(1, 1, 1, 0.92f),
        };
        _label.AddThemeFontSizeOverride("font_size", 14);
        _label.AddThemeColorOverride("font_outline_color", Colors.Black);
        _label.AddThemeConstantOverride("outline_size", 4);
        AddChild(_label);
    }

    public override void _Process(double delta)
    {
        _accum += delta;
        if (_accum < 0.25)
        {
            return;
        }
        _accum = 0;
        var s = FrameStats.Sample();
        var cam = _camera?.GlobalPosition ?? Vector3.Zero;
        _label.Text = string.Format(
            CultureInfo.InvariantCulture,
            "{0:F0} fps · {1} draw calls · {2:N0} prims\nphysics {4:F2} ms/frame · objects {3}\ncamera ({5:F2}, {6:F2}, {7:F2})\n" +
            "1-4 views · RMB look · WASD/QE · Shift\nLMB drag element · P/N spawn\n" +
            "gamepad Start+A · keyboard: arrows,\nZ/X turn, C/V intake, Space launch\nF4 code · F5 run · F6 git{8}",
            s.Fps, s.DrawCalls, s.Primitives, s.Objects, s.PhysicsMs, cam.X, cam.Y, cam.Z,
            (Message.Length > 0 ? "\n" + Message : "") + "\nms/tick: " + _profile);
        _profileAccum += 0.25;
        if (_profileAccum >= 1)
        {
            _profileAccum = 0;
            _profile = TickProfiler.TakeSummary();
            if (OS.GetCmdlineUserArgs().Contains("--capture"))
            {
                GD.Print("profile ms/tick: " + _profile + $" | fps {s.Fps:F0} physics {s.PhysicsMs:F2} ms/frame prims {s.Primitives}");
            }
        }
    }

    private string _profile = "";
    private double _profileAccum;
}
