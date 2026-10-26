using Godot;

namespace Sim.App;

/// <summary>Locations outside the Godot project (repo root, config/, tests/, reports/) and of the generated assets.
/// Valid when running from the repository; packaging for release is Phase 17.</summary>
public static class RepoPaths
{
    public static string ProjectDir => ProjectSettings.GlobalizePath("res://");

    public static string RepoRoot => System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectDir, ".."));

    public static string Config(string fileName) => System.IO.Path.Combine(RepoRoot, "config", fileName);

    public static string Scenarios => System.IO.Path.Combine(RepoRoot, "tests", "scenarios");

    public static string Reports(string sub) => System.IO.Path.Combine(RepoRoot, "reports", sub);

    /// <summary>
    /// Generated assets of NAME ("field", a robot): the CAD pipeline output res://generated/NAME/ when it exists, else
    /// the CAD-free stand-in res://generated/standin/NAME/ (tools/standin/make_standin.py; CI and checkouts without the
    /// original STEP files). Ends with a slash.
    /// </summary>
    public static string AssetDir(string name)
    {
        var real = $"res://generated/{name}/";
        if (FileAccess.FileExists(real + "layout.json"))
        {
            return real;
        }
        var standIn = $"res://generated/standin/{name}/";
        return FileAccess.FileExists(standIn + "layout.json") ? standIn : real;
    }

    /// <summary>Asset folder of a robot from its YAML <c>cad</c> path ("generated/NAME").</summary>
    public static string CadDir(string cadPath) => AssetDir(System.IO.Path.GetFileName(cadPath.TrimEnd('/')));

    /// <summary>True for stand-in assets (simple shapes from the configs, not the CAD).</summary>
    public static bool IsStandIn(string resPath) => resPath.Contains("/generated/standin/", System.StringComparison.Ordinal);

    /// <summary>"field" → the field layout (pipeline or stand-in), "synthetic" → the test layout,
    /// anything starting with res:// is used as is.</summary>
    public static string Layout(string name)
    {
        if (name.StartsWith("res://", System.StringComparison.Ordinal))
        {
            return name;
        }
        return name == "synthetic" ? "res://test_assets/synthetic/layout.json" : AssetDir(name) + "layout.json";
    }

    /// <summary>The pipeline manifest that belongs to a field layout (part masses for the hive bodies).</summary>
    public static string FieldManifest(string layoutPath) => IsStandIn(layoutPath)
        ? ProjectSettings.GlobalizePath(layoutPath[..(layoutPath.LastIndexOf('/') + 1)] + "manifest.json")
        : System.IO.Path.Combine(RepoRoot, "generated", "field", "manifest.json");
}
