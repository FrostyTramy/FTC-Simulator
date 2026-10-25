using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using Godot;
using Sim.Robot;

namespace Sim.App;

/// <summary>
/// Scripted Driver Station for checks (--opmode): selects the named OpMode, INITs it and presses START two seconds
/// later; at capture time prints the OpMode's telemetry, saves each webcam picture next to the capture PNG and prints
/// the ground-truth pose of every tag cluster in front of a webcam, in the SDK's ftcPose terms (inches; x right,
/// y forward, z up of the camera; cluster origin per SDK 12.0: 7.1874 in above the tag row in the sticker plane and
/// 5.622 in behind it), computed from the rendered tag quads.
/// </summary>
public partial class VisionCheck : Node
{
    private const float Inch = 0.0254f;
    private double _t;
    private bool _initDone;
    private bool _started;

    public RobotProgram Program { get; set; } = null!;
    public RobotBody Robot { get; set; } = null!;
    public string OpMode { get; set; } = "";

    public override void _Process(double delta)
    {
        _t += delta;
        if (!_initDone && _t > 0.5)
        {
            _initDone = true;
            var opMode = Program.Ds.TeleOps.Concat(Program.Ds.Autonomous).FirstOrDefault(o => o.Name == OpMode);
            if (opMode == null)
            {
                GD.PushError($"OpMode '{OpMode}' not found");
                return;
            }
            Program.Ds.Select(opMode);
            Program.Ds.Init();
        }
        if (_initDone && !_started && _t > 2.5 && Program.Ds.CanPlay)
        {
            _started = true;
            Program.Ds.Play(Robot.NowNanos);
        }
    }

    /// <summary>Called by the CaptureRunner before quitting.</summary>
    public void Report(string capturePath)
    {
        foreach (var line in Program.Last?.Telemetry ?? new List<string>())
        {
            GD.Print("telemetry: " + line.Replace("\n", " "));
        }
        for (var i = 0; i < Robot.Webcams.Count; i++)
        {
            var rig = Robot.Webcams[i];
            var image = rig.Picture.GetImage();
            if (image != null && !image.IsEmpty())
            {
                image.SavePng(capturePath.Replace(".png", $"_cam{i}.png"));
            }
            foreach (var (name, xyz) in GroundTruth(rig))
            {
                GD.Print(string.Format(CultureInfo.InvariantCulture, "groundtruth {0} XYZ {1:F1} {2:F1} {3:F1} (inch)", name, xyz.X, xyz.Y, xyz.Z));
            }
        }
    }

    private IEnumerable<(string Name, Vector3 FtcXyz)> GroundTruth(WebcamRig rig)
    {
        var cam = rig.CameraTransform;
        var quads = new Dictionary<int, Node3D>();
        Collect(GetTree().Root, quads);
        foreach (var (first, name) in new[] { (30, "RED SCORING"), (34, "RED AUDIENCE"), (38, "BLUE AUDIENCE"), (42, "BLUE SCORING") })
        {
            if (!Enumerable.Range(first, 4).All(quads.ContainsKey))
            {
                continue;
            }
            var tags = Enumerable.Range(first, 4).Select(id => quads[id].GlobalTransform).ToList();
            var row = tags.Aggregate(Vector3.Zero, (s, t) => s + t.Origin) / 4;
            var up = tags[0].Basis.Y.Normalized();
            var normal = tags[0].Basis.Z.Normalized();
            var origin = row + up * 7.1874f * Inch - normal * 5.622f * Inch;
            var d = origin - cam.Origin;
            var raw = new Vector3(d.Dot(cam.Basis.X.Normalized()), d.Dot(-cam.Basis.Y.Normalized()), d.Dot(-cam.Basis.Z.Normalized()));
            if (raw.Z > 0)
            {
                yield return (name, new Vector3(raw.X, raw.Z, -raw.Y) / Inch);
            }
        }
    }

    private static void Collect(Node node, Dictionary<int, Node3D> quads)
    {
        if (node is MeshInstance3D m && m.Name.ToString().StartsWith("AprilTag_") && int.TryParse(m.Name.ToString()[9..], out var id))
        {
            quads[id] = m;
        }
        foreach (var child in node.GetChildren())
        {
            Collect(child, quads);
        }
    }
}
