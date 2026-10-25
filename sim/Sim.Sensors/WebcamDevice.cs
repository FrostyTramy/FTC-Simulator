using Sim.Robots;

namespace Sim.Sensors;

/// <summary>A captured camera frame on its way to the robot code: RGBA pixels, detections, when it becomes visible.</summary>
/// <param name="CaptureNanos">Simulation time the image was taken.</param>
/// <param name="DueNanos">Simulation time the frame reaches the processors (capture + latency).</param>
public sealed record CameraFrame(string Camera, long CaptureNanos, long DueNanos, int Width, int Height, byte[] Rgba,
    IReadOnlyList<TagDetection>? Detections);

/// <summary>
/// A USB webcam (spec 09 "Robot cameras"): the renderer captures a frame whenever <see cref="CaptureDue"/> says so (fps
/// of the model, after the open time, while the robot code streams), hands the pixels to <see cref="Deliver"/>, and the
/// frame reaches the robot code after the model's latency. When the robot code's AprilTag processor asks for it
/// (command "apriltag"), the AprilTag 3 library runs on the grey image, as on the Control Hub.
/// Commands: "stream" (on, width, height), "apriltag" (on, decimation, family), "exposure" (manual, ms, gain).
/// </summary>
public sealed class WebcamDevice : SensorDevice, IDisposable
{
    private readonly Queue<CameraFrame> _pending = new();
    private AprilTagDetector? _detector;
    private bool _streaming;
    private double _openUntil;
    private double _nextCapture;
    private double _now;

    public WebcamDevice(SensorDef def, SensorModels models, int index) : base(def)
    {
        Model = models.Webcams.TryGetValue(def.Model, out var m) ? m : throw new RobotConfigException($"{def.Name}: unknown webcam model '{def.Model}'");
        Index = index;
    }

    public WebcamModel Model { get; }

    /// <summary>Position of this camera among the robot's webcams (its shared-memory file number).</summary>
    public int Index { get; }

    /// <summary>The robot code asked for frames (VisionPortal streaming).</summary>
    public bool Streaming => _streaming;

    /// <summary>AprilTag detection requested by the robot code: decimation (0 = off).</summary>
    public float DetectionDecimation { get; private set; }

    /// <summary>Manual exposure (ms) and gain set by the robot code; null = automatic exposure.</summary>
    public (double ExposureMs, double Gain)? ManualExposure { get; private set; }

    /// <summary>True during the step in which the renderer should take a picture.</summary>
    public bool CaptureDue { get; private set; }

    public override IReadOnlyDictionary<string, double> Parameters => new Dictionary<string, double>
    {
        ["index"] = Index, ["width"] = Model.Width, ["height"] = Model.Height, ["fx"] = Model.Fx, ["fy"] = Model.Fy,
        ["cx"] = Model.Cx, ["cy"] = Model.Cy, ["fps"] = Model.Fps, ["exposureMinMs"] = Model.ExposureMinMs,
        ["exposureMaxMs"] = Model.ExposureMaxMs, ["gainMin"] = Model.GainMin, ["gainMax"] = Model.GainMax,
    };

    public override void Step(ISensorWorld world, double time, double dt)
    {
        _now = time;
        CaptureDue = false;
        if (!_streaming || time < _openUntil)
        {
            return;
        }
        if (time + 1e-9 >= _nextCapture)
        {
            CaptureDue = true;
            _nextCapture = Math.Max(_nextCapture + 1.0 / Model.Fps, time);
        }
    }

    /// <summary>The renderer's picture for this step (RGBA, row 0 at the top).</summary>
    public void Deliver(byte[] rgba, int width, int height, long captureNanos)
    {
        IReadOnlyList<TagDetection>? detections = null;
        if (DetectionDecimation > 0)
        {
            _detector ??= new AprilTagDetector(DetectionDecimation);
            _detector.Decimation = DetectionDecimation;
            detections = _detector.Detect(Gray(rgba, width, height), width, height);
        }
        _pending.Enqueue(new CameraFrame(Name, captureNanos, captureNanos + (long)Math.Round(Model.Latency * 1e9), width, height,
            rgba, detections));
    }

    /// <summary>Frames that reach the robot code by <paramref name="nowNanos"/>, oldest first.</summary>
    public IReadOnlyList<CameraFrame> TakeDue(long nowNanos)
    {
        var due = new List<CameraFrame>();
        while (_pending.Count > 0 && _pending.Peek().DueNanos <= nowNanos)
        {
            due.Add(_pending.Dequeue());
        }
        return due;
    }

    public override double[] Snapshot() => [];

    public override void Apply(string command, IReadOnlyList<double> args)
    {
        switch (command)
        {
            case "stream" when args.Count >= 1:
                var on = args[0] != 0;
                if (on && !_streaming)
                {
                    _openUntil = _now + Model.OpenTime;
                    _nextCapture = _openUntil;
                }
                if (!on)
                {
                    _pending.Clear();
                }
                _streaming = on;
                break;
            case "apriltag" when args.Count >= 2:
                DetectionDecimation = args[0] != 0 ? (float)args[1] : 0;
                break;
            case "exposure" when args.Count >= 3:
                ManualExposure = args[0] != 0 ? (args[1], args[2]) : null;
                break;
            default:
                break;
        }
    }

    /// <summary>OpenCV COLOR_RGBA2GRAY in fixed point (Y = 0.299 R + 0.587 G + 0.114 B), as the SDK converts.</summary>
    public static byte[] Gray(byte[] rgba, int width, int height)
    {
        var gray = new byte[width * height];
        for (var i = 0; i < gray.Length; i++)
        {
            gray[i] = (byte)((rgba[4 * i] * 4899 + rgba[4 * i + 1] * 9617 + rgba[4 * i + 2] * 1868 + 8192) >> 14);
        }
        return gray;
    }

    public void Dispose() => _detector?.Dispose();
}
