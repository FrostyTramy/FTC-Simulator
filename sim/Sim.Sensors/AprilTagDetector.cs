using System.Reflection;
using System.Runtime.InteropServices;
using Sim.Core.Config;

namespace Sim.Sensors;

/// <summary>One detected tag: pixel geometry from the detector, pose of the tag in the camera frame when requested.</summary>
/// <param name="Corners">Four corners (x, y) in pixels, in the library's order.</param>
/// <param name="Rotation">Row-major 3×3 rotation of the tag in the camera frame (x right, y down, z forward), or null.</param>
/// <param name="Translation">Tag centre in the camera frame, metres, or null.</param>
public sealed record TagDetection(
    int Id,
    int Hamming,
    float DecisionMargin,
    (double X, double Y) Center,
    (double X, double Y)[] Corners,
    double[]? Rotation,
    double[]? Translation,
    double PoseError);

/// <summary>Pinhole camera intrinsics in pixels.</summary>
public readonly record struct CameraIntrinsics(double Fx, double Fy, double Cx, double Cy);

/// <summary>
/// The real AprilTag 3 detector (spec 09 "Detection: run the real AprilTag algorithm on the rendered frames"), the native
/// library shipped by pupil-apriltags (BSD-2) in generated/tools/apriltag with its runtime DLLs. Family tag36h11 (the
/// FTC family). Not thread-safe: one detector per camera thread.
/// </summary>
public sealed unsafe class AprilTagDetector : IDisposable
{
    private const string Lib = "apriltag";
    private readonly IntPtr _detector;
    private readonly IntPtr _family;
    private bool _disposed;

    static AprilTagDetector()
    {
        NativeLibrary.SetDllImportResolver(typeof(AprilTagDetector).Assembly, Resolve);
    }

    /// <param name="decimation">quad_decimate: detection on a downscaled image (FTC default 2 when not set by code).</param>
    public AprilTagDetector(float decimation = 2f, int threads = 1)
    {
        _detector = apriltag_detector_create();
        _family = tag36h11_create();
        apriltag_detector_add_family_bits(_detector, _family, 2);
        var d = (DetectorHeader*)_detector;
        d->NThreads = threads;
        d->QuadDecimate = decimation;
    }

    public float Decimation
    {
        get => ((DetectorHeader*)_detector)->QuadDecimate;
        set => ((DetectorHeader*)_detector)->QuadDecimate = value;
    }

    /// <summary>
    /// Detects tags in an 8-bit grayscale image (row-major, stride = width). With <paramref name="tagSizeM"/> and
    /// <paramref name="camera"/>, each detection also gets its pose (the library's estimate_tag_pose).
    /// </summary>
    public IReadOnlyList<TagDetection> Detect(byte[] gray, int width, int height, double? tagSizeM = null, CameraIntrinsics? camera = null)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (gray.Length < width * height)
        {
            throw new ArgumentException("image buffer is smaller than width × height");
        }
        var result = new List<TagDetection>();
        fixed (byte* buf = gray)
        {
            var image = new ImageU8 { Width = width, Height = height, Stride = width, Buf = buf };
            var zarray = apriltag_detector_detect(_detector, &image);
            try
            {
                var z = (ZArray*)zarray;
                for (var i = 0; i < z->Size; i++)
                {
                    var det = *(Detection**)(z->Data + i * (long)z->ElementSize);
                    double[]? rotation = null, translation = null;
                    var error = double.NaN;
                    if (tagSizeM is { } size && camera is { } k)
                    {
                        var info = new DetectionInfo { Det = det, TagSize = size, Fx = k.Fx, Fy = k.Fy, Cx = k.Cx, Cy = k.Cy };
                        var pose = new Pose();
                        error = estimate_tag_pose(&info, &pose);
                        rotation = ReadMatrix(pose.R);
                        translation = ReadMatrix(pose.T);
                        matd_destroy(pose.R);
                        matd_destroy(pose.T);
                    }
                    var corners = new (double, double)[4];
                    for (var c = 0; c < 4; c++)
                    {
                        corners[c] = (det->P[c * 2], det->P[c * 2 + 1]);
                    }
                    result.Add(new TagDetection(det->Id, det->Hamming, det->DecisionMargin, (det->C[0], det->C[1]), corners, rotation, translation, error));
                }
            }
            finally
            {
                apriltag_detections_destroy(zarray);
            }
        }
        return result;
    }

    /// <summary>
    /// The tag36h11 pattern of <paramref name="id"/>: a (total width)² grid of black/white cells, 10 × 10 for tag36h11
    /// (outer white ring, black border ring, 6 × 6 data). Row-major, 0 or 255. Drawn from the family's code table the
    /// way upstream apriltag_to_image does: the bundled build has the old version of that function, which puts the white
    /// ring one cell too far in.
    /// </summary>
    public byte[] TagPattern(int id, out int cells)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        var f = (Family*)_family;
        if ((uint)id >= f->NCodes)
        {
            throw new ArgumentOutOfRangeException(nameof(id), $"tag36h11 has ids 0..{f->NCodes - 1}");
        }
        cells = f->TotalWidth;
        var pattern = new byte[cells * cells];
        var whiteWidth = f->WidthAtBorder + (f->ReversedBorder != 0 ? 0 : 2);
        var whiteStart = (cells - whiteWidth) / 2;
        var whiteEnd = whiteStart + whiteWidth - 1;
        for (var i = whiteStart; i <= whiteEnd; i++)
        {
            pattern[whiteStart * cells + i] = 255;
            pattern[whiteEnd * cells + i] = 255;
            pattern[i * cells + whiteStart] = 255;
            pattern[i * cells + whiteEnd] = 255;
        }
        var borderStart = (cells - f->WidthAtBorder) / 2;
        var code = f->Codes[id];
        for (var i = 0; i < f->NBits; i++)
        {
            if ((code & (1UL << (int)(f->NBits - i - 1))) != 0)
            {
                pattern[(f->BitY[i] + borderStart) * cells + f->BitX[i] + borderStart] = 255;
            }
        }
        return pattern;
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        apriltag_detector_destroy(_detector);
        tag36h11_destroy(_family);
    }

    private static double[] ReadMatrix(IntPtr matd)
    {
        var m = (MatdHeader*)matd;
        var data = (double*)(matd + sizeof(MatdHeader));
        var values = new double[m->Rows * m->Cols];
        for (var i = 0; i < values.Length; i++)
        {
            values[i] = data[i];
        }
        return values;
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? path)
    {
        if (name != Lib)
        {
            return IntPtr.Zero;
        }
        var library = RepoPaths.Find(Path.Combine("generated", "tools", "apriltag", Toolchain.NativeLibrary("apriltag")));
        return NativeLibrary.Load(library);   // absolute path: on Windows its runtime DLLs load from the same folder
    }

    // Layouts from pupil-apriltags 1.0.4.post11 bindings.py (AprilTag 3).
    [StructLayout(LayoutKind.Sequential)]
    private struct ImageU8
    {
        public int Width;
        public int Height;
        public int Stride;
        public byte* Buf;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Family
    {
        public uint NCodes;
        public ulong* Codes;
        public int WidthAtBorder;
        public int TotalWidth;
        public byte ReversedBorder;   // C bool
        public uint NBits;
        public uint* BitX;
        public uint* BitY;
        public uint H;
        public byte* Name;
        public IntPtr Impl;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MatdHeader
    {
        public int Rows;
        public int Cols;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct ZArray
    {
        public nuint ElementSize;
        public int Size;
        public int Alloc;
        public byte* Data;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Detection
    {
        public IntPtr Family;
        public int Id;
        public int Hamming;
        public float DecisionMargin;
        public IntPtr H;
        public fixed double C[2];
        public fixed double P[8];
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DetectorHeader
    {
        public int NThreads;
        public float QuadDecimate;
        public float QuadSigma;
        public int RefineEdges;
        public double DecodeSharpening;
        public int Debug;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DetectionInfo
    {
        public Detection* Det;
        public double TagSize;
        public double Fx;
        public double Fy;
        public double Cx;
        public double Cy;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct Pose
    {
        public IntPtr R;
        public IntPtr T;
    }

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr apriltag_detector_create();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void apriltag_detector_destroy(IntPtr detector);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void apriltag_detector_add_family_bits(IntPtr detector, IntPtr family, int bitsCorrected);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr apriltag_detector_detect(IntPtr detector, ImageU8* image);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void apriltag_detections_destroy(IntPtr detections);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern IntPtr tag36h11_create();

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void tag36h11_destroy(IntPtr family);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern double estimate_tag_pose(DetectionInfo* info, Pose* pose);

    [DllImport(Lib, CallingConvention = CallingConvention.Cdecl)]
    private static extern void matd_destroy(IntPtr matrix);
}
