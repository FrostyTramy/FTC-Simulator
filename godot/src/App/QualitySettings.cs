using Godot;

namespace Sim.App;

public enum QualityPreset
{
    Low,
    Medium,
}

/// <summary>Rendering presets for weak GPUs (D-007: Intel Iris Xe at ≈30 fps). All quality
/// switches live here. High (Phase 12) adds shadows of every light, SSAO, glow and MSAA.</summary>
public static class QualitySettings
{
    public static void Apply(Viewport viewport, DirectionalLight3D sun, Godot.Environment env, QualityPreset preset)
    {
        env.SsaoEnabled = false;
        env.SsrEnabled = false;
        env.SdfgiEnabled = false;
        env.VolumetricFogEnabled = false;
        env.GlowEnabled = false;
        viewport.Msaa3D = Viewport.Msaa.Disabled;
        viewport.UseTaa = false;

        switch (preset)
        {
            case QualityPreset.Low:
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Disabled;
                viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
                viewport.Scaling3DScale = 0.75f;
                viewport.MeshLodThreshold = 4.0f;
                sun.ShadowEnabled = false;
                break;
            default:
                viewport.ScreenSpaceAA = Viewport.ScreenSpaceAAEnum.Fxaa;
                viewport.Scaling3DMode = Viewport.Scaling3DModeEnum.Bilinear;
                viewport.Scaling3DScale = 1.0f;
                viewport.MeshLodThreshold = 1.0f;
                sun.ShadowEnabled = true;
                sun.DirectionalShadowMode = DirectionalLight3D.ShadowMode.Parallel2Splits;
                sun.DirectionalShadowMaxDistance = 14f;
                break;
        }
    }
}
