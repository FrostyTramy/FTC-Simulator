using Godot;
using Sim.Core.Config;
using Sim.Sensors;

namespace Sim.Field;

/// <summary>
/// The AprilTag stickers under the CELLS (spec 09 "AprilTags"; §9.9 p74-76): the CAD stickers are plain plates, so the
/// four tag36h11 images of each cluster are drawn on top of them as quads, laid out as config/field_mechanisms.yaml
/// apriltag_stickers says (print right/up axes, 3.25 in tags, ids left to right). They are children of their HIVE body
/// and tip with it. Each quad covers the whole 10 × 10 cell pattern (white ring included) and is drawn 0.5 mm off
/// the sticker; textures are 32 px per cell with mipmaps and anisotropic filtering.
/// </summary>
public static class AprilTagDecals
{
    private const float Inch = 0.0254f;
    private const float TagSize = 3.25f * Inch;          // black border, SDK 12.0 BIOBUZZ library
    private const float RowOffset = 0.26f * Inch;        // tag row above the sticker middle (PrintableAprilTag)
    private const int PixelsPerCell = 32;
    private static readonly float[] TagX = [-6.5f * Inch, -2.75f * Inch, 2.75f * Inch, 6.5f * Inch];

    /// <summary>Adds the tag quads; returns how many were placed.</summary>
    public static int Build(LoadedField field, string yamlPath)
    {
        var root = YamlObjects.Load(yamlPath);
        var stickers = YamlObjects.List(YamlObjects.Child(root, "apriltag_stickers", "field_mechanisms"), "apriltag_stickers");
        using var detector = new AprilTagDetector();
        var placed = 0;
        foreach (var raw in stickers)
        {
            var path = "apriltag_stickers";
            var name = YamlObjects.String(YamlObjects.Child(raw, "name", path), path + ".name");
            var ids = YamlObjects.Doubles(YamlObjects.Child(raw, "ids", path), path + ".ids");
            var centre = Vec(raw, "centre", path);
            var normal = Vec(raw, "normal", path).Normalized();
            var right = Vec(raw, "right", path).Normalized();
            var up = Vec(raw, "up", path).Normalized();
            var group = name.StartsWith("red") ? "hive_red" : "hive_blue";
            Node3D parent = field.RigidGroups.TryGetValue(group, out var body) ? body : field.Root;
            for (var i = 0; i < 4 && i < ids.Length; i++)
            {
                var id = (int)ids[i];
                var pattern = detector.TagPattern(id, out var cells);
                var size = TagSize * cells / 8f;
                var mesh = new MeshInstance3D
                {
                    Name = $"AprilTag_{id}",
                    Mesh = new QuadMesh { Size = new Vector2(size, size) },
                    MaterialOverride = Material(pattern, cells),
                    CastShadow = GeometryInstance3D.ShadowCastingSetting.Off,
                };
                var position = centre + up * RowOffset + right * TagX[i] + normal * 0.0005f;
                var cad = new Transform3D(new Basis(right, up, normal), position);   // quad: +X right, +Y up, faces +Z
                var world = field.Root.GlobalTransform * cad;
                parent.AddChild(mesh);
                mesh.GlobalTransform = world;
                placed++;
            }
        }
        return placed;
    }

    private static StandardMaterial3D Material(byte[] pattern, int cells)
    {
        var size = cells * PixelsPerCell;
        var data = new byte[size * size];
        for (var y = 0; y < size; y++)
        {
            for (var x = 0; x < size; x++)
            {
                data[y * size + x] = pattern[y / PixelsPerCell * cells + x / PixelsPerCell];
            }
        }
        var image = Image.CreateFromData(size, size, false, Image.Format.L8, data);
        image.GenerateMipmaps();
        return new StandardMaterial3D
        {
            AlbedoTexture = ImageTexture.CreateFromImage(image),
            TextureFilter = BaseMaterial3D.TextureFilterEnum.LinearWithMipmapsAnisotropic,
            Roughness = 1f,
            MetallicSpecular = 0.1f,
        };
    }

    private static Vector3 Vec(object? node, string key, string path)
    {
        var v = YamlObjects.Doubles(YamlObjects.QuantityValue(YamlObjects.Child(node, key, path), $"{path}.{key}"), $"{path}.{key}");
        return new Vector3((float)v[0], (float)v[1], (float)v[2]);
    }
}
