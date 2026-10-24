namespace Sim.Core.Config;

/// <summary>Finds repository files (config/, generated/) from any working directory or test output folder.</summary>
public static class RepoPaths
{
    /// <summary>Walks up from <paramref name="start"/> (default: the app base directory) until <paramref name="relative"/> exists.</summary>
    public static string Find(string relative, string? start = null)
    {
        var origin = start ?? AppContext.BaseDirectory;
        var dir = new DirectoryInfo(origin);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, relative);
            if (File.Exists(candidate) || Directory.Exists(candidate))
            {
                return candidate;
            }
            dir = dir.Parent;
        }
        throw new FileNotFoundException($"'{relative}' not found above {origin}");
    }

    public static string RulesFile() => Find(Path.Combine("config", "rules_biobuzz.yaml"));
}
