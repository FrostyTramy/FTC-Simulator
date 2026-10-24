using Godot;

namespace Sim.App;

/// <summary>Lighting and environment: one shadowed directional light and sky ambient light,
/// cheap enough for integrated graphics. The arena model itself comes in Phase 12.</summary>
public static class Arena
{
    public static (DirectionalLight3D Sun, Godot.Environment Env) Create(Node parent)
    {
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Sky,
            Sky = new Sky
            {
                SkyMaterial = new ProceduralSkyMaterial
                {
                    SkyTopColor = new Color(0.32f, 0.36f, 0.42f),
                    SkyHorizonColor = new Color(0.55f, 0.57f, 0.6f),
                    GroundBottomColor = new Color(0.12f, 0.12f, 0.13f),
                    GroundHorizonColor = new Color(0.4f, 0.4f, 0.42f),
                },
            },
            AmbientLightSource = Godot.Environment.AmbientSource.Sky,
            AmbientLightEnergy = 0.9f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
        };
        parent.AddChild(new WorldEnvironment { Name = "WorldEnvironment", Environment = env });

        // Venue lights are mostly overhead; a steep key light keeps the hive shadows readable.
        var sun = new DirectionalLight3D
        {
            Name = "KeyLight",
            LightEnergy = 1.1f,
            ShadowBias = 0.03f,
            RotationDegrees = new Vector3(-62f, 28f, 0f),
        };
        parent.AddChild(sun);
        return (sun, env);
    }
}
