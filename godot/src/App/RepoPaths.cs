using Godot;

namespace Sim.App;

/// <summary>Locations outside the Godot project (repo root, config/, tests/, reports/).
/// Valid when running from the repository; packaging for release is Phase 17.</summary>
public static class RepoPaths
{
    public static string ProjectDir => ProjectSettings.GlobalizePath("res://");

    public static string RepoRoot => System.IO.Path.GetFullPath(System.IO.Path.Combine(ProjectDir, ".."));

    public static string Config(string fileName) => System.IO.Path.Combine(RepoRoot, "config", fileName);

    public static string Scenarios => System.IO.Path.Combine(RepoRoot, "tests", "scenarios");

    public static string Reports(string sub) => System.IO.Path.Combine(RepoRoot, "reports", sub);

    /// <summary>"field" → res://generated/field/layout.json, "synthetic" → the test layout,
    /// anything starting with res:// is used as is.</summary>
    public static string Layout(string name)
    {
        if (name.StartsWith("res://", System.StringComparison.Ordinal))
        {
            return name;
        }
        return name == "synthetic"
            ? "res://test_assets/synthetic/layout.json"
            : $"res://generated/{name}/layout.json";
    }
}
