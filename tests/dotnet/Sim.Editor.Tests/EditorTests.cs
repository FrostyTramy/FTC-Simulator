using System.Text.Json.Nodes;
using Sim.Core.Config;

namespace EditorTests;

/// <summary>Spec 07: templates compile, the generated project links TeamCode, LSP framing, the Java language server.</summary>
public class EditorTests
{
    private static string NewTempDir(string tag)
    {
        var dir = Path.Combine(Path.GetTempPath(), $"ftcsim-{tag}-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        return dir;
    }

    [Fact]
    public void Every_template_compiles_against_the_shim()
    {
        var root = NewTempDir("templates");
        try
        {
            var src = Path.Combine(root, "java");
            var kinds = Enum.GetValues<TemplateKind>();
            foreach (var kind in kinds)
            {
                var file = Templates.PathFor(src, "My" + kind);
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                File.WriteAllText(file, Templates.Create(kind, "My" + kind, $"My {kind}"));
            }
            var build = TeamCodeBuilder.Build(src, Path.Combine(root, "build"));
            Assert.True(build.Success, build.Output);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Theory]
    [InlineData("MyTeleOp", true)]
    [InlineData("_x1", true)]
    [InlineData("1Robot", false)]
    [InlineData("class", false)]
    [InlineData("My TeleOp", false)]
    public void Class_names_must_be_java_identifiers(string name, bool valid) => Assert.Equal(valid, Templates.IsJavaIdentifier(name));

    [Fact]
    public void Generated_project_links_teamcode_and_the_shim()
    {
        var root = NewTempDir("project");
        try
        {
            var project = EclipseProject.Create("team & co", Path.Combine(root, "eclipse"), Path.Combine(root, "TeamCode", "src", "main", "java"),
                @"C:\shim\ftc-sdk-shim.jar", @"C:\shim\ftc-sdk-shim-sources.jar");
            var dotProject = File.ReadAllText(Path.Combine(project.Directory, ".project"));
            var classpath = File.ReadAllText(Path.Combine(project.Directory, ".classpath"));
            Assert.Contains("<name>team &amp; co</name>", dotProject);
            Assert.Contains(Path.Combine(root, "TeamCode", "src", "main", "java").Replace('\\', '/'), dotProject);
            Assert.Contains("JavaSE-17", classpath);
            Assert.Contains("sourcepath=\"C:/shim/ftc-sdk-shim-sources.jar\"", classpath);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void Lsp_frames_round_trip()
    {
        var message = new JsonObject { ["jsonrpc"] = "2.0", ["id"] = 3, ["method"] = "textDocument/hover", ["params"] = new JsonObject { ["x"] = "ä" } };
        var frame = LspClient.Frame(message);
        Assert.StartsWith("Content-Length: ", System.Text.Encoding.ASCII.GetString(frame, 0, 16));
        using var stream = new MemoryStream([.. frame, .. frame]);
        Assert.Equal(message.ToJsonString(), LspClient.ReadFrame(stream)!.ToJsonString());
        Assert.NotNull(LspClient.ReadFrame(stream));
        Assert.Null(LspClient.ReadFrame(stream));
    }

    /// <summary>
    /// Phase 7 acceptance steps against the real server: a template TeleOp gets completions on "telemetry." (FTC API
    /// from the shim), a syntax error shows up as a diagnostic on its line, and fixing it clears the diagnostic.
    /// </summary>
    [Fact]
    public async Task Java_language_server_completes_the_ftc_api_and_reports_errors()
    {
        var root = NewTempDir("jdtls");
        var src = Path.Combine(root, "TeamCode", "src", "main", "java");
        var file = Templates.PathFor(src, "Driver");
        Directory.CreateDirectory(Path.GetDirectoryName(file)!);
        var text = Templates.Create(TemplateKind.TeleOp, "Driver");
        File.WriteAllText(file, text);
        var (shim, _) = JavaLocator.FindRunnerJars();
        var sources = Directory.GetFiles(Path.GetDirectoryName(shim)!, "ftc-sdk-shim-*-sources.jar").FirstOrDefault();
        var project = EclipseProject.Create("Team", Path.Combine(root, "eclipse"), src, shim, sources);
        using var server = await JavaLanguageServer.Start(project, Path.Combine(root, "jdtls-data"));
        try
        {
            var errors = new TaskCompletionSource<IReadOnlyList<EditorDiagnostic>>(TaskCreationOptions.RunContinuationsAsynchronously);
            var cleared = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var uri = JavaLanguageServer.Uri(file);
            server.DiagnosticsChanged += (u, list) =>
            {
                if (!string.Equals(u, uri, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                if (list.Any(d => d.IsError))
                {
                    errors.TrySetResult(list);
                }
                else if (errors.Task.IsCompleted)
                {
                    cleared.TrySetResult();
                }
            };
            server.Open(file, text);

            // Completion after "telemetry." inside loop(): line of the Runtime addData call.
            var lines = text.Split('\n');
            var lineIndex = Array.FindIndex(lines, l => l.Contains("telemetry.addData(\"Runtime\"", StringComparison.Ordinal));
            var column = lines[lineIndex].IndexOf("telemetry.", StringComparison.Ordinal) + "telemetry.".Length;
            IReadOnlyList<CompletionItem> items = [];
            for (var attempt = 0; attempt < 30 && !items.Any(i => i.Label.StartsWith("addData", StringComparison.Ordinal)); attempt++)
            {
                items = await server.Complete(file, lineIndex, column);   // the first requests may come while indexing
                if (items.Count == 0)
                {
                    await Task.Delay(1000);
                }
            }
            Assert.Contains(items, i => i.Label.StartsWith("addData", StringComparison.Ordinal));

            var broken = text.Replace("telemetry.addData(\"Status\", \"Initialized\");", "telemetry.addData(\"Status\", \"Initialized\")");
            server.Change(file, broken);
            var diagnostics = await errors.Task.WaitAsync(TimeSpan.FromSeconds(90));
            var statusLine = Array.FindIndex(lines, l => l.Contains("\"Initialized\"", StringComparison.Ordinal));
            Assert.Contains(diagnostics, d => d.IsError && d.Line == statusLine);

            server.Change(file, text);
            await cleared.Task.WaitAsync(TimeSpan.FromSeconds(90));
        }
        finally
        {
            server.Dispose();
            try
            {
                Directory.Delete(root, recursive: true);
            }
            catch (IOException)
            {
                // the server may still hold index files for a moment
            }
        }
    }
}
