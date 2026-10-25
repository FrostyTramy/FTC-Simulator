namespace SensorTests;

/// <summary>The native AprilTag 3 detector on synthetic frames (spec 09 AprilTags).</summary>
public class AprilTagTests
{
    /// <summary>Tag pattern scaled to <paramref name="cellPx"/> pixels per cell, centred in a white image.</summary>
    private static (byte[] Image, int Size) Render(AprilTagDetector detector, int id, int cellPx, int margin)
    {
        var pattern = detector.TagPattern(id, out var cells);
        var size = cells * cellPx + 2 * margin;
        var image = Enumerable.Repeat((byte)255, size * size).ToArray();
        for (var y = 0; y < cells * cellPx; y++)
        {
            for (var x = 0; x < cells * cellPx; x++)
            {
                image[(y + margin) * size + x + margin] = pattern[y / cellPx * cells + x / cellPx];
            }
        }
        return (image, size);
    }

    [Fact]
    public void Tag36h11_pattern_has_the_10_cell_layout()
    {
        using var detector = new AprilTagDetector();
        var pattern = detector.TagPattern(30, out var cells);
        Assert.Equal(10, cells);
        Assert.All(Enumerable.Range(0, 10), i => Assert.Equal(255, pattern[i]));           // outer white ring
        Assert.All(Enumerable.Range(1, 8), i => Assert.Equal(0, pattern[1 * 10 + i]));      // black border ring
    }

    [Fact]
    public void Detects_id_and_corners_of_a_rendered_tag()
    {
        using var detector = new AprilTagDetector(decimation: 1);
        var (image, size) = Render(detector, 30, cellPx: 40, margin: 100);
        var d = Assert.Single(detector.Detect(image, size, size));
        Assert.Equal(30, d.Id);
        Assert.Equal(0, d.Hamming);
        Assert.InRange(d.Center.X, size / 2.0 - 0.25, size / 2.0 + 0.25);   // sub-pixel edge fit
        Assert.InRange(d.Center.Y, size / 2.0 - 0.25, size / 2.0 + 0.25);
        // Black border corners: cells 1..9 of the 10-cell pattern → 140..460 px.
        Assert.All(d.Corners, c =>
        {
            Assert.True(Math.Abs(c.X - 140) < 1.5 || Math.Abs(c.X - 460) < 1.5, $"corner x {c.X}");
            Assert.True(Math.Abs(c.Y - 140) < 1.5 || Math.Abs(c.Y - 460) < 1.5, $"corner y {c.Y}");
        });
    }

    [Fact]
    public void Pose_estimate_recovers_the_distance_from_the_intrinsics()
    {
        using var detector = new AprilTagDetector(decimation: 1);
        var (image, size) = Render(detector, 34, cellPx: 40, margin: 100);
        const double tagSize = 3.25 * 0.0254;   // SDK 12.0 BIOBUZZ library: 3.25 in
        var camera = new CameraIntrinsics(600, 600, size / 2.0, size / 2.0);
        var d = Assert.Single(detector.Detect(image, size, size, tagSize, camera));
        // The 8-cell black border spans 320 px: z = fx · size / px.
        Assert.Equal(600 * tagSize / 320, d.Translation![2], 3);
        Assert.Equal(0, d.Translation[0], 3);
        Assert.Equal(0, d.Translation[1], 3);
        Assert.Equal(1, d.Rotation![8], 3);   // facing the camera
    }

    [Fact]
    public void Blank_image_has_no_detections()
    {
        using var detector = new AprilTagDetector();
        Assert.Empty(detector.Detect(Enumerable.Repeat((byte)128, 640 * 480).ToArray(), 640, 480));
    }
}
