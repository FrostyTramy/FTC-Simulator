using Sim.Core.Config;

namespace Sim.Runner;

/// <summary>Finds the Java 17 runtime and the runner jars (development layout: the Gradle build outputs under java/).</summary>
public static class JavaLocator
{
    /// <summary>
    /// The java launcher of a Java 17 installation: JAVA17_HOME, then JAVA_HOME, then the usual install folders
    /// (newest 17.x first). The runner needs exactly 17: its sandbox uses the SecurityManager, which later
    /// versions removed.
    /// </summary>
    public static string FindJava17()
    {
        var home = Toolchain.JdkHomes("JAVA17_HOME", "JAVA_HOME").FirstOrDefault(h => Toolchain.JdkMajor(h) == 17);
        return home is null
            ? throw new RunnerException("Java 17 was not found. Install a JDK 17 (e.g. Eclipse Temurin 17) or set JAVA17_HOME.")
            : Toolchain.JavaIn(home);
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

    /// <summary>The OpenCV Java bindings and native library (scripts/setup_tools.ps1), or null if not set up.</summary>
    public static (string Jar, string Native)? FindOpenCv()
    {
        try
        {
            var dir = RepoPaths.Find(Path.Combine("generated", "tools", "opencv"));
            var jar = Path.Combine(dir, "opencv-4.9.0-0.jar");
            var native = Path.Combine(dir, Toolchain.NativeLibrary("opencv_java490"));
            return File.Exists(jar) && File.Exists(native) ? (jar, native) : null;
        }
        catch (FileNotFoundException)
        {
            return null;
        }
    }
}
