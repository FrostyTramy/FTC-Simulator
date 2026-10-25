using System.IO.MemoryMappedFiles;

namespace Sim.Runner;

/// <summary>
/// Camera frames shared with the runner JVM (spec 01 "shared memory for camera frames"): one memory-mapped file per
/// webcam, <c>&lt;workdir&gt;/camera-&lt;index&gt;-&lt;tag&gt;.bin</c> (tag = the session's, so sessions never share a
/// file), holding <see cref="Slots"/> RGBA frames. A frame is written to
/// the next slot just before the TICK that hands it to the robot code; the runner reads it while handling that TICK,
/// so a slot is never rewritten while it is being read.
/// </summary>
public sealed class CameraFrameStore : IDisposable
{
    public const int Slots = 4;
    private readonly string _workDir;
    private readonly string _tag;
    private readonly Dictionary<int, (MemoryMappedFile File, MemoryMappedViewAccessor View, int FrameBytes, int Next)> _cameras = [];

    public CameraFrameStore(string workDir, string tag)
    {
        _workDir = workDir;
        _tag = tag;
        Directory.CreateDirectory(workDir);
    }

    public string PathOf(int index) => Path.Combine(_workDir, $"camera-{index}-{_tag}.bin");

    /// <summary>Copies a frame into camera <paramref name="index"/>'s next slot and returns the slot number.</summary>
    public int Write(int index, byte[] rgba, int width, int height)
    {
        var frameBytes = width * height * 4;
        if (rgba.Length < frameBytes)
        {
            throw new ArgumentException("frame buffer smaller than width × height × 4");
        }
        if (!_cameras.TryGetValue(index, out var camera) || camera.FrameBytes != frameBytes)
        {
            if (_cameras.TryGetValue(index, out var old))
            {
                old.View.Dispose();
                old.File.Dispose();
            }
            var file = MemoryMappedFile.CreateFromFile(PathOf(index), FileMode.Create, null, (long)frameBytes * Slots,
                MemoryMappedFileAccess.ReadWrite);
            camera = (file, file.CreateViewAccessor(), frameBytes, 0);
        }
        var slot = camera.Next;
        camera.View.WriteArray((long)slot * frameBytes, rgba, 0, frameBytes);
        camera.View.Flush();
        _cameras[index] = camera with { Next = (slot + 1) % Slots };
        return slot;
    }

    /// <summary>Unmaps the files and deletes them (the runner has exited or will not read them again).</summary>
    public void Dispose()
    {
        foreach (var (index, c) in _cameras)
        {
            c.View.Dispose();
            c.File.Dispose();
            try
            {
                File.Delete(PathOf(index));
            }
            catch (IOException)
            {
                // the runner still maps it; the next session uses another file
            }
        }
        _cameras.Clear();
    }
}
