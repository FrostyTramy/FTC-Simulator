using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using Sim.Core.Config;

namespace Sim.Runner;

/// <summary>One compiler message, clickable in the problems panel (spec 07 "Problems panel").</summary>
/// <param name="Column">1-based column from javac's caret line, when it printed one.</param>
public sealed record JavaDiagnostic(string File, int Line, int? Column, string Severity, string Message);

/// <summary>Outcome of compiling a TeamCode folder.</summary>
public sealed record BuildResult(bool Success, string? JarPath, IReadOnlyList<JavaDiagnostic> Diagnostics, string Output, TimeSpan Duration)
{
    public IEnumerable<JavaDiagnostic> Errors => Diagnostics.Where(d => d.Severity == "error");
}

/// <summary>
/// Compiles a team's TeamCode sources (read-only) into a jar for the runner with java/build-adapter (spec 07 "Build
/// adapter"): Gradle with a warm daemon, Java 17, one build folder per project so compilation is incremental. javac
/// messages become <see cref="JavaDiagnostic"/>s.
/// </summary>
public static partial class TeamCodeBuilder
{
    /// <param name="teamCodeJavaDir">The folder that holds org/firstinspires/ftc/teamcode (TeamCode/src/main/java).</param>
    /// <param name="buildDir">Where the build writes (never inside the team's repository).</param>
    public static BuildResult Build(string teamCodeJavaDir, string buildDir, TimeSpan? timeout = null)
    {
        var started = Stopwatch.StartNew();
        var javaRoot = RepoPaths.Find("java");
        var gradle = FindGradle(javaRoot);
        var java = JavaLocator.FindJava17();
        var info = new ProcessStartInfo(gradle)
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = javaRoot,
        };
        info.Environment["JAVA_HOME"] = Path.GetDirectoryName(Path.GetDirectoryName(java))!;
        foreach (var arg in new[]
        {
            "--console=plain", "-q", ":build-adapter:jar",
            "-PteamCode=" + Path.GetFullPath(teamCodeJavaDir), "-PbuildOut=" + Path.GetFullPath(buildDir),
        })
        {
            info.ArgumentList.Add(arg);
        }
        var output = new StringBuilder();
        using var process = Process.Start(info) ?? throw new RunnerException("could not start Gradle");
        process.OutputDataReceived += (_, e) => Append(output, e.Data);
        process.ErrorDataReceived += (_, e) => Append(output, e.Data);
        process.BeginOutputReadLine();
        process.BeginErrorReadLine();
        if (!process.WaitForExit((int)(timeout ?? TimeSpan.FromMinutes(5)).TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new RunnerException("the build did not finish in time");
        }
        process.WaitForExit();   // drains the output events
        var text = output.ToString();
        Directory.CreateDirectory(buildDir);
        File.WriteAllText(LogPath(buildDir), text);   // the whole output, to read or send when a build fails
        var jar = Path.Combine(buildDir, "libs", "teamcode.jar");
        var success = process.ExitCode == 0 && File.Exists(jar);
        return new BuildResult(success, success ? jar : null, ParseDiagnostics(text), text, started.Elapsed);
    }

    /// <summary>The last build's full Gradle/javac output in <paramref name="buildDir"/>.</summary>
    public static string LogPath(string buildDir) => Path.Combine(buildDir, "build.log");

    /// <summary>Libraries teams add to TeamCode (build.dependencies.gradle), by the package prefix their code imports.</summary>
    private static readonly (string Prefix, string Library)[] KnownLibraries =
    [
        ("com.pedropathing", "Pedro Pathing"),
        ("com.acmerobotics.roadrunner", "Road Runner"),
        ("com.acmerobotics.dashboard", "FTC Dashboard"),
        ("com.bylazar", "Panels"),
        ("com.arcrobotics.ftclib", "FTCLib"),
        ("com.seattlesolvers.solverslib", "SolversLib"),
        ("dev.frozenmilk", "Dairy / Mercurial"),
        ("org.openftc.easyopencv", "EasyOpenCV"),
        ("org.openftc.apriltag", "OpenFTC AprilTag"),
        ("page.j5155", "Road Runner (j5155 quickstart)"),
        ("android.", "Android API (not in the simulator)"),
        ("androidx.", "AndroidX (not in the simulator)"),
        ("org.firstinspires.ftc.robotcontroller", "FtcRobotController module"),
    ];

    /// <summary>
    /// What a failed build is missing: the packages javac could not find ("package X does not exist", "cannot access
    /// X"), as known library names (or the package itself), most errors first. Empty when the errors are ordinary code
    /// errors.
    /// </summary>
    public static IReadOnlyList<string> MissingLibraries(IEnumerable<JavaDiagnostic> diagnostics) =>
        diagnostics.Where(d => d.Severity == "error")
            .Select(d => MissingPackage().Match(d.Message))
            .Where(m => m.Success)
            .Select(m => m.Groups["package"].Value)
            .Select(package => KnownLibraries.FirstOrDefault(k => package.StartsWith(k.Prefix, StringComparison.Ordinal)) is { Library: { } name }
                ? name
                : string.Join('.', package.Split('.').Take(3)))
            .GroupBy(name => name)
            .OrderByDescending(g => g.Count())
            .ThenBy(g => g.Key, StringComparer.Ordinal)
            .Select(g => g.Key)
            .ToList();

    [GeneratedRegex(@"^(package (?<package>[\w.]+) does not exist|cannot access (?<package>[\w.]+))")]
    private static partial Regex MissingPackage();

    /// <summary>True when <paramref name="jar"/> exists and is newer than every file under <paramref name="sourceDir"/>.</summary>
    public static bool IsUpToDate(string sourceDir, string jar)
    {
        if (!File.Exists(jar))
        {
            return false;
        }
        var built = File.GetLastWriteTimeUtc(jar);
        return Directory.GetFiles(sourceDir, "*", SearchOption.AllDirectories).All(f => File.GetLastWriteTimeUtc(f) <= built);
    }

    /// <summary>javac lines "FILE.java:LINE: error: MESSAGE", the source line, then a caret under the column.</summary>
    public static IReadOnlyList<JavaDiagnostic> ParseDiagnostics(string output)
    {
        var lines = output.Replace("\r\n", "\n").Split('\n');
        var result = new List<JavaDiagnostic>();
        for (var i = 0; i < lines.Length; i++)
        {
            var m = DiagnosticLine().Match(lines[i]);
            if (!m.Success)
            {
                continue;
            }
            int? column = null;
            if (i + 2 < lines.Length && lines[i + 2].TrimEnd().EndsWith('^') && lines[i + 2].Trim() == "^")
            {
                column = lines[i + 2].IndexOf('^') + 1;
            }
            result.Add(new JavaDiagnostic(m.Groups["file"].Value, int.Parse(m.Groups["line"].Value, System.Globalization.CultureInfo.InvariantCulture),
                column, m.Groups["sev"].Value, m.Groups["msg"].Value.Trim()));
        }
        return result;
    }

    private static string FindGradle(string javaRoot)
    {
        // The verified local distribution (no download), else the wrapper (scripts/test_java.ps1 does the same).
        var local = Path.Combine(Path.GetDirectoryName(javaRoot)!, "generated", "tools", "gradle-8.14.3", "bin",
            OperatingSystem.IsWindows() ? "gradle.bat" : "gradle");
        return File.Exists(local) ? local : Path.Combine(javaRoot, OperatingSystem.IsWindows() ? "gradlew.bat" : "gradlew");
    }

    private static void Append(StringBuilder sb, string? line)
    {
        if (line is null)
        {
            return;
        }
        lock (sb)
        {
            sb.AppendLine(line);
        }
    }

    [GeneratedRegex(@"^(?<file>[A-Za-z]:[\\/].+?\.java|/.+?\.java):(?<line>\d+): (?<sev>error|warning): (?<msg>.*)$")]
    private static partial Regex DiagnosticLine();
}
