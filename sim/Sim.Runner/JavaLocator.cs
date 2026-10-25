using Sim.Core.Config;

namespace Sim.Runner;

/// <summary>Finds the Java 17 runtime and the runner jars (development layout: the Gradle build outputs under java/).</summary>
public static class JavaLocator
{
    private static readonly string[] InstallRoots =
    [
        @"C:\Program Files\Java",
        @"C:\Program Files\Eclipse Adoptium",
        @"C:\Program Files\Microsoft",
        @"C:\Program Files\Zulu",
        @"C:\Program Files\BellSoft",
        @"C:\Program Files\Amazon Corretto",
    ];

    /// <summary>
    /// java.exe of a Java 17 installation: JAVA17_HOME, then JAVA_HOME, then the usual install folders
    /// (newest 17.x first). The runner needs exactly 17: its sandbox uses the SecurityManager, which later
    /// versions removed.
    /// </summary>
    public static string FindJava17()
    {
        var homes = new List<string>();
        foreach (var variable in new[] { "JAVA17_HOME", "JAVA_HOME" })
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } home)
            {
                homes.Add(home);
            }
        }
        foreach (var root in InstallRoots.Where(Directory.Exists))
        {
            homes.AddRange(Directory.GetDirectories(root).OrderByDescending(d => d, StringComparer.Ordinal));
        }
        foreach (var home in homes)
        {
            var java = Path.Combine(home, "bin", OperatingSystem.IsWindows() ? "java.exe" : "java");
            if (File.Exists(java) && IsJava17(home))
            {
                return java;
            }
        }
        throw new RunnerException("Java 17 was not found. Install a JDK 17 (e.g. Eclipse Temurin 17) or set JAVA17_HOME.");
    }

    /// <summary>The SDK shim and runner jars built by Gradle (scripts/test_java.ps1 builds them).</summary>
    public static (string ShimJar, string RunnerJar) FindRunnerJars()
    {
        var shimDir = RepoPaths.Find(Path.Combine("java", "ftc-sdk-shim", "build", "libs"));
        var shims = Directory.GetFiles(shimDir, "ftc-sdk-shim-*.jar").Where(f => !f.EndsWith("-sources.jar", StringComparison.Ordinal)).ToArray();
        if (shims.Length != 1)
        {
            throw new RunnerException($"expected one ftc-sdk-shim jar in {shimDir}, found {shims.Length}; run the Java build");
        }
        var runner = RepoPaths.Find(Path.Combine("java", "runner", "build", "libs", "ftc-sim-runner.jar"));
        return (shims[0], runner);
    }

    private static bool IsJava17(string home)
    {
        var release = Path.Combine(home, "release");
        return File.Exists(release) && File.ReadLines(release).Any(l => l.StartsWith("JAVA_VERSION=\"17", StringComparison.Ordinal));
    }
}
