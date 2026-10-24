using System.Collections.Generic;
using Godot;

namespace Sim.Field;

/// <summary>Loads pipeline .glb files. Primary path: Godot's importer (run
/// <c>godot --headless --path godot --import</c> after a pipeline run), which adds LODs, shadow
/// meshes and vertex compression. Fallback: runtime glTF loading, so a freshly generated but not yet
/// imported file still shows up (without LODs) instead of failing.</summary>
public static class MeshLoader
{
    private static readonly Dictionary<string, PackedScene> Cache = new();
    private static readonly HashSet<string> WarnedNotImported = new();

    /// <summary>A new instance of the glb scene. Instances share mesh resources.</summary>
    public static Node3D Instantiate(string resPath)
    {
        if (!Cache.TryGetValue(resPath, out var scene))
        {
            scene = LoadScene(resPath);
            Cache[resPath] = scene;
        }
        return scene.Instantiate<Node3D>();
    }

    public static void ClearCache() => Cache.Clear();

    private static PackedScene LoadScene(string resPath)
    {
        if (ResourceLoader.Exists(resPath))
        {
            var imported = ResourceLoader.Load<PackedScene>(resPath);
            if (imported != null)
            {
                return imported;
            }
        }
        if (WarnedNotImported.Add(resPath))
        {
            GD.PushWarning($"{resPath} is not imported; loading it at runtime without LODs. Run scripts/godot_import.ps1.");
        }
        var doc = new GltfDocument();
        var state = new GltfState();
        var err = doc.AppendFromFile(ProjectSettings.GlobalizePath(resPath), state);
        if (err != Error.Ok)
        {
            throw new System.IO.FileNotFoundException($"cannot load {resPath}: {err}");
        }
        var root = doc.GenerateScene(state);
        var packed = new PackedScene();
        packed.Pack(root);
        root.Free();
        return packed;
    }
}
