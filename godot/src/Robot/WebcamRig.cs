using System;
using Godot;
using Sim.Robots;
using Sim.Sensors;

namespace Sim.Robot;

/// <summary>
/// Renders one robot webcam (spec 09 "Robot cameras"): a Camera3D in its own SubViewport of the model's frame size,
/// a pinhole with the model's intrinsics (frustum height from fy, principal point as frustum offset), placed at the
/// robot YAML pose. When its <see cref="WebcamDevice"/> asks for a capture, the camera is moved to the robot's current
/// pose and the viewport is drawn at once (RenderingServer.ForceDraw with the main window inactive), so the picture
/// shows exactly the scene state of that physics step in every run; the pixels go to the device. No temporal effects
/// (TAA) in the camera viewport, so a picture depends only on the scene.
/// </summary>
public partial class WebcamRig : SubViewport
{
    private const float Near = 0.01f;
    private const float Far = 20f;

    /// <summary>Rendered at this multiple of the frame size, then averaged down (a webcam samples a finer sensor).</summary>
    private const int Supersample = 2;
    private RobotBody _robot = null!;
    private Camera3D _camera = null!;
    private Transform3D _mount;
    private float _exposureGain = 1f;

    /// <summary>Auto exposure aims at this mean brightness (0..1) with a gain of 0.5..4 (UVC auto exposure, ASSUMED).</summary>
    private const float TargetBrightness = 0.42f;

    public WebcamDevice Device { get; private set; } = null!;

    /// <summary>Where the camera was at the last capture (Godot camera: +X right, +Y up, looking along −Z).</summary>
    public Transform3D CameraTransform => _camera.GlobalTransform;

    /// <summary>The last captured frame (for the Driver Station camera stream preview).</summary>
    public Texture2D Picture => GetTexture();

    public static WebcamRig Create(RobotBody robot, WebcamDevice device)
    {
        var m = device.Model;
        var rig = new WebcamRig
        {
            Name = "Webcam_" + device.Index,
            Size = new Vector2I(m.Width * Supersample, m.Height * Supersample),
            Scaling3DMode = Scaling3DModeEnum.Bilinear,
            Scaling3DScale = 1f,
            RenderTargetUpdateMode = UpdateMode.Disabled,
            Msaa3D = Viewport.Msaa.Msaa2X,
            UseTaa = false,
            ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled,
            TransparentBg = false,
            _robot = robot,
            Device = device,
        };
        var def = device.Def;
        var forward = ToGodot(def.Forward).Normalized();
        var up = ToGodot(def.Up).Normalized();
        var right = forward.Cross(up).Normalized();
        rig._mount = new Transform3D(new Basis(right, up, -forward), ToGodot(def.Position));
        rig._camera = new Camera3D { Current = true, KeepAspect = Camera3D.KeepAspectEnum.Height };
        rig._camera.SetFrustum(Near * m.Height / (float)m.Fy,
            new Vector2(Near * (m.Width / 2f - (float)m.Cx) / (float)m.Fx, Near * ((float)m.Cy - m.Height / 2f) / (float)m.Fy), Near, Far);
        rig.AddChild(rig._camera);
        robot.AddChild(rig);
        return rig;
    }

    /// <summary>Called each physics step after the sensors stepped: takes the picture if the device wants one.</summary>
    public void CaptureIfDue(long captureNanos)
    {
        if (!Device.CaptureDue)
        {
            return;
        }
        _camera.GlobalTransform = _robot.GlobalTransform * _mount;
        RenderTargetUpdateMode = UpdateMode.Once;
        var main = GetTree().Root.GetViewportRid();
        RenderingServer.ViewportSetActive(main, false);
        RenderingServer.ForceDraw(false);
        RenderingServer.ViewportSetActive(main, true);
        var image = GetTexture().GetImage();
        if (image == null || image.IsEmpty())
        {
            return;   // no renderer (headless)
        }
        if (image.GetFormat() != Image.Format.Rgba8)
        {
            image.Convert(Image.Format.Rgba8);
        }
        image.Resize(Device.Model.Width, Device.Model.Height, Image.Interpolation.Bilinear);
        var rgba = image.GetData();
        Expose(rgba);
        Device.Deliver(rgba, image.GetWidth(), image.GetHeight(), captureNanos);
    }

    /// <summary>
    /// Brightness like a webcam: automatic exposure moves the gain half way to the target each frame; manual exposure
    /// (robot code ExposureControl/GainControl) scales with exposure time / 16 ms and gain / 100 (ASSUMED response).
    /// </summary>
    private void Expose(byte[] rgba)
    {
        float wanted;
        if (Device.ManualExposure is { } manual)
        {
            wanted = (float)(manual.ExposureMs / 16.0 * manual.Gain / 100.0);
        }
        else
        {
            double sum = 0;
            var n = 0;
            for (var i = 0; i < rgba.Length; i += 64)
            {
                sum += 0.299 * rgba[i] + 0.587 * rgba[i + 1] + 0.114 * rgba[i + 2];
                n++;
            }
            var mean = (float)(sum / Math.Max(1, n) / 255.0);   // the rendered brightness, before the gain
            wanted = Math.Clamp(TargetBrightness / Math.Max(mean, 1e-3f), 0.5f, 4f);
        }
        _exposureGain += 0.5f * (wanted - _exposureGain);
        if (Math.Abs(_exposureGain - 1f) < 1e-3f)
        {
            return;
        }
        for (var i = 0; i < rgba.Length; i += 4)
        {
            rgba[i] = (byte)Math.Min(255, rgba[i] * _exposureGain);
            rgba[i + 1] = (byte)Math.Min(255, rgba[i + 1] * _exposureGain);
            rgba[i + 2] = (byte)Math.Min(255, rgba[i + 2] * _exposureGain);
        }
    }

    private static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);
}
