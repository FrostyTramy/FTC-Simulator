using System.Diagnostics;
using System.Text.Json.Nodes;
using Sim.Core.Config;

namespace Sim.Editor;

/// <summary>A compiler problem from the language server (0-based line/character as in LSP).</summary>
public sealed record EditorDiagnostic(string Uri, int Line, int Character, int EndLine, int EndCharacter, int Severity, string Message)
{
    public bool IsError => Severity == 1;
}

/// <summary>One completion proposal.</summary>
public sealed record CompletionItem(string Label, string? Detail, string InsertText, int Kind);

/// <summary>
/// Eclipse JDT Language Server (spec 07 "Language server") as a child process: started lazily on the generated
/// <see cref="EclipseProject"/>, compiled against Java 17 (the FTC language level) although the server itself needs
/// Java 21+. Diagnostics arrive asynchronously (<see cref="DiagnosticsChanged"/>); completion, hover and definition
/// are requests. Restart it with a new instance after a crash (<see cref="Exited"/>).
/// </summary>
public sealed class JavaLanguageServer : IDisposable
{
    private readonly Process _process;
    private readonly Dictionary<string, int> _versions = new();
    private readonly Dictionary<string, IReadOnlyList<EditorDiagnostic>> _diagnostics = new();
    private bool _disposed;

    private JavaLanguageServer(Process process)
    {
        _process = process;
        Client = new LspClient(process.StandardInput.BaseStream, process.StandardOutput.BaseStream);
        Client.Notification += OnNotification;
        Client.Closed += () => Exited?.Invoke();
    }

    public LspClient Client { get; }

    /// <summary>"Starting", "Ready" or an error, from the server's language/status notifications.</summary>
    public string Status { get; private set; } = "Starting";

    /// <summary>Raised (on a background thread) when a file's diagnostics change: URI and the new list.</summary>
    public event Action<string, IReadOnlyList<EditorDiagnostic>>? DiagnosticsChanged;

    public event Action? Exited;

    public IReadOnlyList<EditorDiagnostic> DiagnosticsFor(string uri)
    {
        lock (_diagnostics)
        {
            return _diagnostics.TryGetValue(uri, out var d) ? d : [];
        }
    }

    /// <summary>Starts the server for <paramref name="project"/> and completes the LSP handshake.</summary>
    /// <param name="dataDir">The server's own workspace data (index), outside the team's repository.</param>
    public static async Task<JavaLanguageServer> Start(EclipseProject project, string dataDir, TimeSpan? timeout = null)
    {
        var home = FindServer();
        var java = FindJava(21) ?? throw new LspException("the Java language server needs Java 21 or newer (JDK 26 recommended)");
        var info = new ProcessStartInfo(java)
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        var args = new List<string>
        {
            "-Declipse.application=org.eclipse.jdt.ls.core.id1",
            "-Dosgi.bundles.defaultStartLevel=4",
            "-Declipse.product=org.eclipse.jdt.ls.core.product",
            "-Dosgi.checkConfiguration=true",
            "-Dosgi.sharedConfiguration.area=" + Path.Combine(home, OperatingSystem.IsWindows() ? "config_win" : "config_linux"),
            "-Dosgi.sharedConfiguration.area.readOnly=true",
            "-Dosgi.configuration.cascaded=true",
            "-Djdk.xml.maxGeneralEntitySizeLimit=0",
            "-Djdk.xml.totalEntitySizeLimit=0",
            "-Xmx1G",
            "--add-modules=ALL-SYSTEM",
            "--add-opens", "java.base/java.util=ALL-UNNAMED",
            "--add-opens", "java.base/java.lang=ALL-UNNAMED",
            "-jar", Directory.GetFiles(Path.Combine(home, "plugins"), "org.eclipse.equinox.launcher_*.jar").Single(),
            "-data", dataDir,
        };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }
        Directory.CreateDirectory(dataDir);
        var process = Process.Start(info) ?? throw new LspException("could not start the Java language server");
        process.ErrorDataReceived += (_, _) => { };   // drained; the server logs into its data folder
        process.BeginErrorReadLine();
        var server = new JavaLanguageServer(process);
        var jdk17 = JavaHome(Sim.Runner.JavaLocator.FindJava17());
        var init = new JsonObject
        {
            ["processId"] = Environment.ProcessId,
            ["rootUri"] = Uri(project.Directory),
            ["capabilities"] = new JsonObject
            {
                ["textDocument"] = new JsonObject
                {
                    ["completion"] = new JsonObject { ["completionItem"] = new JsonObject { ["snippetSupport"] = false } },
                    ["publishDiagnostics"] = new JsonObject(),
                    ["hover"] = new JsonObject { ["contentFormat"] = new JsonArray("plaintext") },
                },
                ["workspace"] = new JsonObject { ["configuration"] = true },
            },
            ["initializationOptions"] = new JsonObject
            {
                ["extendedClientCapabilities"] = new JsonObject { ["progressReportProvider"] = false },
                ["settings"] = new JsonObject
                {
                    ["java"] = new JsonObject
                    {
                        ["configuration"] = new JsonObject
                        {
                            ["runtimes"] = new JsonArray(new JsonObject { ["name"] = "JavaSE-17", ["path"] = jdk17, ["default"] = true }),
                        },
                        ["import"] = new JsonObject { ["gradle"] = new JsonObject { ["enabled"] = false }, ["maven"] = new JsonObject { ["enabled"] = false } },
                        ["autobuild"] = new JsonObject { ["enabled"] = true },
                    },
                },
            },
        };
        await server.Client.Request("initialize", init, timeout ?? TimeSpan.FromMinutes(3)).ConfigureAwait(false);
        server.Client.Notify("initialized", new JsonObject());
        return server;
    }

    public void Open(string path, string text)
    {
        var uri = Uri(path);
        _versions[uri] = 1;
        Client.Notify("textDocument/didOpen", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri, ["languageId"] = "java", ["version"] = 1, ["text"] = text },
        });
    }

    /// <summary>Full-text change (the editor sends the whole file; simple and fast enough for TeamCode files).</summary>
    public void Change(string path, string text)
    {
        var uri = Uri(path);
        var version = _versions[uri] = _versions.GetValueOrDefault(uri) + 1;
        Client.Notify("textDocument/didChange", new JsonObject
        {
            ["textDocument"] = new JsonObject { ["uri"] = uri, ["version"] = version },
            ["contentChanges"] = new JsonArray(new JsonObject { ["text"] = text }),
        });
    }

    public void Save(string path) =>
        Client.Notify("textDocument/didSave", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = Uri(path) } });

    public void Close(string path)
    {
        var uri = Uri(path);
        _versions.Remove(uri);
        Client.Notify("textDocument/didClose", new JsonObject { ["textDocument"] = new JsonObject { ["uri"] = uri } });
    }

    /// <summary>Completion at a 0-based position.</summary>
    public async Task<IReadOnlyList<CompletionItem>> Complete(string path, int line, int character)
    {
        var result = await Client.Request("textDocument/completion", Position(path, line, character)).ConfigureAwait(false);
        var items = result is JsonArray array ? array : result?["items"] as JsonArray;
        return items?.OfType<JsonObject>().Select(i => new CompletionItem(
            i["label"]?.GetValue<string>() ?? "",
            i["detail"]?.GetValue<string>(),
            i["textEdit"]?["newText"]?.GetValue<string>() ?? i["insertText"]?.GetValue<string>() ?? i["label"]?.GetValue<string>() ?? "",
            i["kind"]?.GetValue<int>() ?? 0)).ToList() ?? [];
    }

    /// <summary>Hover text (javadoc/signature) at a 0-based position, or null.</summary>
    public async Task<string?> Hover(string path, int line, int character)
    {
        var result = await Client.Request("textDocument/hover", Position(path, line, character)).ConfigureAwait(false);
        var contents = result?["contents"];
        return contents switch
        {
            JsonArray parts => string.Join("\n", parts.Select(p => p is JsonValue v ? v.GetValue<string>() : p?["value"]?.GetValue<string>())),
            JsonObject o => o["value"]?.GetValue<string>(),
            JsonValue v => v.GetValue<string>(),
            _ => null,
        };
    }

    /// <summary>Definition location (file URI, 0-based line) of the symbol at a position, or null.</summary>
    public async Task<(string Uri, int Line)?> Definition(string path, int line, int character)
    {
        var result = await Client.Request("textDocument/definition", Position(path, line, character)).ConfigureAwait(false);
        var location = result is JsonArray a ? a.FirstOrDefault() : result;
        if (location?["uri"] is null)
        {
            return null;
        }
        return (location["uri"]!.GetValue<string>(), location["range"]?["start"]?["line"]?.GetValue<int>() ?? 0);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }
        _disposed = true;
        try
        {
            Client.Request("shutdown", null, TimeSpan.FromSeconds(5)).Wait(TimeSpan.FromSeconds(5));
            Client.Notify("exit", null);
        }
        catch (Exception e) when (e is LspException or AggregateException or IOException)
        {
            // already gone
        }
        Client.Dispose();
        if (!_process.WaitForExit(3000))
        {
            _process.Kill(entireProcessTree: true);
        }
        _process.Dispose();
    }

    public static string Uri(string path) => new Uri(Path.GetFullPath(path)).AbsoluteUri;

    private static JsonObject Position(string path, int line, int character) => new()
    {
        ["textDocument"] = new JsonObject { ["uri"] = Uri(path) },
        ["position"] = new JsonObject { ["line"] = line, ["character"] = character },
    };

    private void OnNotification(string method, JsonNode? parameters)
    {
        switch (method)
        {
            case "textDocument/publishDiagnostics":
                var uri = parameters?["uri"]?.GetValue<string>() ?? "";
                var list = (parameters?["diagnostics"] as JsonArray ?? []).OfType<JsonObject>().Select(d => new EditorDiagnostic(
                    uri,
                    d["range"]?["start"]?["line"]?.GetValue<int>() ?? 0,
                    d["range"]?["start"]?["character"]?.GetValue<int>() ?? 0,
                    d["range"]?["end"]?["line"]?.GetValue<int>() ?? 0,
                    d["range"]?["end"]?["character"]?.GetValue<int>() ?? 0,
                    d["severity"]?.GetValue<int>() ?? 1,
                    d["message"]?.GetValue<string>() ?? "")).ToList();
                lock (_diagnostics)
                {
                    _diagnostics[uri] = list;
                }
                DiagnosticsChanged?.Invoke(uri, list);
                break;
            case "language/status":
                Status = parameters?["type"]?.GetValue<string>() ?? Status;
                break;
        }
    }

    /// <summary>The unpacked server (generated/tools/jdtls-*), newest version first.</summary>
    public static string FindServer()
    {
        var tools = RepoPaths.Find(Path.Combine("generated", "tools"));
        return Directory.GetDirectories(tools, "jdtls-*")
            .Where(d => Directory.Exists(Path.Combine(d, "plugins")))
            .OrderByDescending(d => d, StringComparer.Ordinal)
            .FirstOrDefault() ?? throw new LspException($"no Java language server in {tools} (see docs/SETUP.md)");
    }

    /// <summary>java.exe of an installed JDK with at least <paramref name="minMajor"/>, newest first.</summary>
    public static string? FindJava(int minMajor)
    {
        foreach (var root in new[] { @"C:\Program Files\Java", @"C:\Program Files\Eclipse Adoptium", @"C:\Program Files\Microsoft" }.Where(Directory.Exists))
        {
            foreach (var home in Directory.GetDirectories(root).OrderByDescending(d => d, StringComparer.Ordinal))
            {
                var release = Path.Combine(home, "release");
                var java = Path.Combine(home, "bin", "java.exe");
                if (!File.Exists(release) || !File.Exists(java))
                {
                    continue;
                }
                var line = File.ReadLines(release).FirstOrDefault(l => l.StartsWith("JAVA_VERSION=\"", StringComparison.Ordinal));
                var version = line?["JAVA_VERSION=\"".Length..].TrimEnd('"');
                var major = version?.Split('.')[0];
                if (int.TryParse(major, out var m) && m >= minMajor)
                {
                    return java;
                }
            }
        }
        return null;
    }

    private static string JavaHome(string javaExe) => Path.GetDirectoryName(Path.GetDirectoryName(javaExe))!;
}
