using System.Globalization;
using Sim.Sensors;

namespace RunnerTests;

/// <summary>
/// The camera path without the renderer: a synthetic camera draws the RED AUDIENCE tag cluster face-on at a known
/// distance (pinhole model of the webcam), the simulator's AprilTag 3 detector runs on it, frames reach the runner
/// through shared memory after the camera latency, and the unchanged SDK samples report the cluster pose.
/// </summary>
[Collection("runner")]
public class VisionSampleTests
{
    private const double ClusterDepthInches = 36;   // camera to cluster origin, along the optical axis

    /// <summary>A white frame with the 4 tags of the cluster starting at <paramref name="firstId"/>, camera facing it.</summary>
    internal static byte[] RenderCluster(WebcamModel m, int firstId, double originDepth)
    {
        using var detector = new AprilTagDetector();
        var rgba = Enumerable.Repeat((byte)255, m.Width * m.Height * 4).ToArray();
        var depth = originDepth - 5.622;                 // tags lie 5.622 in in front of the origin (SDK 12 data)
        double[] offsets = [-6.5, -2.75, 2.75, 6.5];
        for (var i = 0; i < 4; i++)
        {
            var pattern = detector.TagPattern(firstId + i, out var cells);
            var full = m.Fx * 3.25 / depth * cells / 8.0;    // 8 cells = the black border = 3.25 in
            var u0 = m.Cx + m.Fx * offsets[i] / depth - full / 2;
            var v0 = m.Cy + m.Fy * 7.1874 / depth - full / 2;
            for (var v = (int)Math.Floor(v0); v <= (int)Math.Ceiling(v0 + full); v++)
            {
                for (var u = (int)Math.Floor(u0); u <= (int)Math.Ceiling(u0 + full); u++)
                {
                    var cx = (int)Math.Floor((u - u0) / full * cells);
                    var cy = (int)Math.Floor((v - v0) / full * cells);
                    if (cx < 0 || cy < 0 || cx >= cells || cy >= cells || u < 0 || v < 0 || u >= m.Width || v >= m.Height)
                    {
                        continue;
                    }
                    var value = pattern[cy * cells + cx];
                    var p = (v * m.Width + u) * 4;
                    rgba[p] = rgba[p + 1] = rgba[p + 2] = value;
                }
            }
        }
        return rgba;
    }

    [Fact]
    public void ConceptAprilTag_reports_the_cluster_pose_from_camera_frames()
    {
        using var robot = SensorRobot.Start(SensorSampleTests.SamplesJarPath());
        var webcam = robot.Sensors.Webcams.Single();
        var image = RenderCluster(webcam.Model, 34, ClusterDepthInches);
        robot.Renderer = _ => image;
        robot.Init("Concept: AprilTag");
        robot.Run(1.5);                                  // camera opens (1 s), first frames arrive
        robot.Session.Start();
        var r = robot.Run(0.5);
        Assert.True(r.State == RunnerState.Running, r.Error);
        Assert.Contains(r.Telemetry, l => l.Contains("# AprilTags Detected : 1"));
        Assert.Contains(r.Telemetry, l => l.Contains("==== Tag Cluster (RED AUDIENCE)"));
        Assert.Contains(r.Telemetry, l => l.Contains("Percent tags found: 100"));
        var xyz = r.Telemetry.Single(l => l.StartsWith("XYZ")).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        double N(int i) => double.Parse(xyz[i], CultureInfo.InvariantCulture);
        Assert.Equal(0, N(1), 0.3);                      // x right
        Assert.Equal(ClusterDepthInches, N(2), 0.3);     // y forward
        Assert.Equal(0, N(3), 0.3);                      // z up
    }

    [Fact]
    public void Localization_sample_counts_the_cluster_but_has_no_field_pose_for_it()
    {
        using var robot = SensorRobot.Start(SensorSampleTests.SamplesJarPath());
        var image = RenderCluster(robot.Sensors.Webcams.Single().Model, 42, ClusterDepthInches);
        robot.Renderer = _ => image;
        robot.Init("Concept: AprilTag Localization");
        robot.Run(1.5);
        robot.Session.Start();
        var r = robot.Run(0.5);
        Assert.True(r.State == RunnerState.Running, r.Error);
        Assert.Contains(r.Telemetry, l => l.Contains("# AprilTags Detected : 1"));
        Assert.DoesNotContain(r.Telemetry, l => l.Contains("Robot XYZ"));   // SDK 12: BIOBUZZ clusters do not localize
    }

    [Fact]
    public void Camera_streams_at_30_fps_after_its_open_time()
    {
        using var robot = SensorRobot.Start(SensorSampleTests.SamplesJarPath());
        var webcam = robot.Sensors.Webcams.Single();
        var image = RenderCluster(webcam.Model, 34, ClusterDepthInches);
        var captures = 0;
        robot.Renderer = _ =>
        {
            captures++;
            return image;
        };
        robot.Init("Concept: AprilTag Easy");
        robot.Run(0.9);
        Assert.Equal(0, captures);                       // open time 1 s (config/sensors.yaml)
        robot.Run(1.0);
        Assert.InRange(captures, 25, 29);                 // 30 fps from t = 1.0 s to 1.9 s
    }
}
