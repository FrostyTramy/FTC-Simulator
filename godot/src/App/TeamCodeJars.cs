using System.IO;
using Godot;
using Sim.Robots;
using Sim.Runner;

namespace Sim.App;

/// <summary>
/// The TeamCode a robot runs when nobody chose one: a robot with its own TeamCode (tests/compat/&lt;robot&gt;, e.g. the
/// RoboDojo TeleOp) builds it into generated/build/&lt;robot&gt; whenever its sources are newer; other robots run the goBILDA
/// example built by java/build-adapter (scripts/test_java.ps1).
/// </summary>
public static class TeamCodeJars
{
    /// <summary>The CAD name of a robot (variants share their base robot's code and CAD).</summary>
    public static string CodeName(string robot) =>
        Path.GetFileName(RobotDefinition.Load(RepoPaths.Config(Path.Combine("robots", robot + ".yaml"))).CadPath.TrimEnd('/'));

    public static bool HasOwnCode(string robot) => CodeName(robot) != "gobilda_starter" && Directory.Exists(OwnSources(robot));

    /// <summary>The jar for <paramref name="robot"/>, built first if needed; null when the build fails.</summary>
    public static string? DefaultFor(string robot)
    {
        if (!HasOwnCode(robot))
        {
            return Path.Combine(RepoPaths.RepoRoot, "java", "build-adapter", "build", "libs", "teamcode.jar");
        }
        var name = CodeName(robot);
        var buildDir = Path.Combine(RepoPaths.RepoRoot, "generated", "build", name);
        var jar = Path.Combine(buildDir, "libs", "teamcode.jar");
        if (TeamCodeBuilder.IsUpToDate(OwnSources(robot), jar))
        {
            return jar;
        }
        var built = TeamCodeBuilder.Build(OwnSources(robot), buildDir);
        GD.Print(built.Success ? $"built {jar}" : $"TeamCode build failed: {built.Output}");
        return built.Success ? jar : null;
    }

    private static string OwnSources(string robot) =>
        Path.Combine(RepoPaths.RepoRoot, "tests", "compat", CodeName(robot), "TeamCode", "src", "main", "java");
}
