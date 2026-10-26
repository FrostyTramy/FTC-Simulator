namespace Sim.Core.Config;

/// <summary>
/// Platform-specific names of the developer tools (generated/tools, scripts/setup_tools.ps1) and JDK install folders.
/// Windows is the product target; the Linux names let the test suites run in CI containers.
/// </summary>
public static class Toolchain
{
    /// <summary>"java" → "java.exe" on Windows.</summary>
    public static string Executable(string name) => OperatingSystem.IsWindows() ? name + ".exe" : name;

    /// <summary>"apriltag" → "apriltag.dll" on Windows, "libapriltag.so" on Linux.</summary>
    public static string NativeLibrary(string baseName) => OperatingSystem.IsWindows() ? baseName + ".dll" : "lib" + baseName + ".so";

    /// <summary>Folders whose sub-folders are JDK homes (vendor installers on Windows, distribution packages on Linux).</summary>
    public static IReadOnlyList<string> JdkInstallRoots { get; } = OperatingSystem.IsWindows()
        ?
        [
            @"C:\Program Files\Java",
            @"C:\Program Files\Eclipse Adoptium",
            @"C:\Program Files\Microsoft",
            @"C:\Program Files\Zulu",
            @"C:\Program Files\BellSoft",
            @"C:\Program Files\Amazon Corretto",
        ]
        : ["/usr/lib/jvm", "/opt/java"];

    /// <summary>
    /// JDK homes from the given environment variables first, then every folder under <see cref="JdkInstallRoots"/>
    /// (newest name first). Only homes that contain bin/java are returned.
    /// </summary>
    public static IEnumerable<string> JdkHomes(params string[] environmentVariables)
    {
        var homes = new List<string>();
        foreach (var variable in environmentVariables)
        {
            if (Environment.GetEnvironmentVariable(variable) is { Length: > 0 } home)
            {
                homes.Add(home);
            }
        }
        foreach (var root in JdkInstallRoots.Where(Directory.Exists))
        {
            homes.AddRange(Directory.GetDirectories(root).OrderByDescending(d => d, StringComparer.Ordinal));
        }
        return homes.Where(h => File.Exists(JavaIn(h))).Distinct(StringComparer.Ordinal);
    }

    /// <summary>The java launcher inside a JDK home.</summary>
    public static string JavaIn(string jdkHome) => Path.Combine(jdkHome, "bin", Executable("java"));

    /// <summary>JAVA_VERSION from the JDK's release file ("17.0.12"), or null (no process start needed).</summary>
    public static string? JdkVersion(string jdkHome)
    {
        var release = Path.Combine(jdkHome, "release");
        if (!File.Exists(release))
        {
            return null;
        }
        const string Key = "JAVA_VERSION=\"";
        var line = File.ReadLines(release).FirstOrDefault(l => l.StartsWith(Key, StringComparison.Ordinal));
        return line?[Key.Length..].TrimEnd('"');
    }

    /// <summary>Major version of a JDK home (17 for "17.0.12"), or 0 if unknown.</summary>
    public static int JdkMajor(string jdkHome) =>
        int.TryParse(JdkVersion(jdkHome)?.Split('.')[0], out var major) ? major : 0;
}
