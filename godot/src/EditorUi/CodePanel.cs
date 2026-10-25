using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Sim.App;
using Sim.Editor;
using Sim.Robot;
using Sim.Runner;

namespace Sim.EditorUi;

/// <summary>
/// Built-in code editor (spec 07, Phase 7), toggled with F4 next to the live sim (Split layout): file tree of the
/// project's TeamCode, tabs, Java highlighting, completion and diagnostics from the Java language server, OpMode
/// templates, Ctrl+S save, F5 run (save all → build → restart the robot's runner with the new jar → OpMode list
/// refreshed in the Driver Station). Build errors and language-server errors go to the problems list (click = jump).
/// </summary>
public partial class CodePanel : PanelContainer
{
    private readonly RobotProgram _program;
    private readonly string _javaDir;
    private readonly string _buildDir;
    private readonly string _toolsDir;
    private readonly ConcurrentQueue<Action> _mainThread = new();
    private readonly List<OpenFile> _open = new();
    private readonly Dictionary<string, IReadOnlyList<EditorDiagnostic>> _serverProblems = new(StringComparer.OrdinalIgnoreCase);
    private IReadOnlyList<JavaDiagnostic> _buildProblems = Array.Empty<JavaDiagnostic>();
    private readonly List<(string File, int Line)> _problemTargets = new();
    private JavaLanguageServer? _server;
    private bool _serverStarting;
    private bool _building;
    private int _current = -1;
    private Tree _tree = null!;
    private TabBar _tabs = null!;
    private CodeEdit _code = null!;
    private ItemList _problems = null!;
    private Label _status = null!;
    private OptionButton _templateKind = null!;
    private LineEdit _className = null!;

    private sealed class OpenFile(string path, string text)
    {
        public string Path { get; } = path;
        public string Text { get; set; } = text;
        public bool Dirty { get; set; }
    }

    /// <param name="projectDir">Team project root (contains TeamCode/src/main/java).</param>
    /// <param name="toolsDir">App data for the build output and the language server (outside the project).</param>
    public CodePanel(RobotProgram program, string projectDir, string toolsDir)
    {
        _program = program;
        _javaDir = System.IO.Path.Combine(projectDir, "TeamCode", "src", "main", "java");
        _toolsDir = toolsDir;
        _buildDir = System.IO.Path.Combine(toolsDir, "build");
    }

    public override void _Ready()
    {
        Visible = false;
        AnchorBottom = 1;
        AnchorRight = 0.55f;
        OffsetLeft = 8;
        OffsetTop = 120;
        OffsetBottom = -8;
        var root = new VBoxContainer();
        AddChild(root);

        var bar = new HBoxContainer();
        _templateKind = new OptionButton { FocusMode = FocusModeEnum.None };
        foreach (var kind in Enum.GetValues<TemplateKind>())
        {
            _templateKind.AddItem("New " + kind);
        }
        _className = new LineEdit { PlaceholderText = "ClassName", CustomMinimumSize = new Vector2(150, 0) };
        bar.AddChild(_templateKind);
        bar.AddChild(_className);
        bar.AddChild(Button("Create", CreateFromTemplate));
        bar.AddChild(Button("Save (Ctrl+S)", () => Save(_current)));
        bar.AddChild(Button("▶ Run on robot (F5)", Run));
        _status = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill, ClipText = true };
        bar.AddChild(_status);
        root.AddChild(bar);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffset = 200 };
        _tree = new Tree { HideRoot = true, CustomMinimumSize = new Vector2(180, 0) };
        _tree.ItemActivated += () =>
        {
            if (_tree.GetSelected()?.GetMetadata(0).AsString() is { Length: > 0 } path)
            {
                OpenPath(path);
            }
        };
        split.AddChild(_tree);
        var editor = new VBoxContainer();
        _tabs = new TabBar { TabCloseDisplayPolicy = TabBar.CloseButtonDisplayPolicy.ShowActiveOnly, FocusMode = FocusModeEnum.None };
        _tabs.TabChanged += index => Show((int)index);
        _tabs.TabClosePressed += index => CloseTab((int)index);
        editor.AddChild(_tabs);
        _code = new CodeEdit
        {
            SizeFlagsVertical = SizeFlags.ExpandFill,
            GuttersDrawLineNumbers = true,
            GuttersDrawFoldGutter = true,
            LineFolding = true,
            HighlightCurrentLine = true,
            IndentUseSpaces = true,
            IndentSize = 4,
            AutoBraceCompletionEnabled = true,
            CodeCompletionEnabled = true,
            CodeCompletionPrefixes = new Godot.Collections.Array<string> { "." },
            SyntaxHighlighter = JavaHighlighter(),
        };
        _code.TextChanged += OnTextChanged;
        _code.CodeCompletionRequested += RequestCompletion;
        editor.AddChild(_code);
        split.AddChild(editor);
        root.AddChild(split);

        _problems = new ItemList { CustomMinimumSize = new Vector2(0, 110), FocusMode = FocusModeEnum.None };
        _problems.ItemActivated += index => GoToProblem((int)index);
        _problems.ItemSelected += index => GoToProblem((int)index);
        root.AddChild(_problems);
        RefreshTree();
        _status.Text = $"Project: {_javaDir}";
    }

    public override void _Input(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } key)
        {
            return;
        }
        if (key.Keycode == Key.F4)
        {
            Visible = !Visible;
            if (Visible)
            {
                StartServer();
                _code.GrabFocus();
            }
            else
            {
                _code.ReleaseFocus();
            }
            GetViewport().SetInputAsHandled();
        }
        else if (Visible && key.Keycode == Key.F5)
        {
            Run();
            GetViewport().SetInputAsHandled();
        }
        else if (Visible && key.CtrlPressed && key.Keycode == Key.S)
        {
            Save(_current);
            GetViewport().SetInputAsHandled();
        }
    }

    public override void _Process(double delta)
    {
        while (_mainThread.TryDequeue(out var action))
        {
            action();
        }
    }

    public override void _ExitTree()
    {
        _server?.Dispose();
        _server = null;
    }

    // ------------------------------------------------------------------ files and tabs

    private void RefreshTree()
    {
        _tree.Clear();
        var root = _tree.CreateItem();
        if (!Directory.Exists(_javaDir))
        {
            return;
        }
        var folders = new Dictionary<string, TreeItem>(StringComparer.OrdinalIgnoreCase) { [_javaDir] = root };
        foreach (var file in Directory.GetFiles(_javaDir, "*.java", SearchOption.AllDirectories).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var parent = Folder(System.IO.Path.GetDirectoryName(file)!, folders);
            var item = _tree.CreateItem(parent);
            item.SetText(0, System.IO.Path.GetFileName(file));
            item.SetMetadata(0, file);
        }
    }

    private TreeItem Folder(string dir, Dictionary<string, TreeItem> folders)
    {
        if (folders.TryGetValue(dir, out var item))
        {
            return item;
        }
        var parent = Folder(System.IO.Path.GetDirectoryName(dir)!, folders);
        var folder = _tree.CreateItem(parent);
        folder.SetText(0, System.IO.Path.GetFileName(dir));
        folder.SetMetadata(0, "");
        folders[dir] = folder;
        return folder;
    }

    private void OpenPath(string path, int line = -1)
    {
        var index = _open.FindIndex(f => string.Equals(f.Path, path, StringComparison.OrdinalIgnoreCase));
        if (index < 0)
        {
            var text = File.ReadAllText(path);
            _open.Add(new OpenFile(path, text));
            _tabs.AddTab(System.IO.Path.GetFileName(path));
            index = _open.Count - 1;
            _server?.Open(path, text);
        }
        _tabs.CurrentTab = index;
        Show(index);
        if (line >= 0)
        {
            _code.SetCaretLine(line);
            _code.CenterViewportToCaret();
            _code.GrabFocus();
        }
    }

    private void Show(int index)
    {
        if (index < 0 || index >= _open.Count)
        {
            return;
        }
        if (_current >= 0 && _current < _open.Count)
        {
            _open[_current].Text = _code.Text;
        }
        _current = index;
        _code.TextChanged -= OnTextChanged;
        _code.Text = _open[index].Text;
        _code.ClearUndoHistory();
        _code.TextChanged += OnTextChanged;
        PaintDiagnostics();
    }

    private void CloseTab(int index)
    {
        if (_open[index].Dirty)
        {
            Save(index);   // never lose edits silently
        }
        _server?.Close(_open[index].Path);
        _open.RemoveAt(index);
        _tabs.RemoveTab(index);
        _current = -1;
        if (_open.Count > 0)
        {
            Show(Math.Min(index, _open.Count - 1));
        }
        else
        {
            _code.Text = "";
        }
    }

    private void OnTextChanged()
    {
        if (_current < 0)
        {
            return;
        }
        var file = _open[_current];
        file.Text = _code.Text;
        if (!file.Dirty)
        {
            file.Dirty = true;
            _tabs.SetTabTitle(_current, System.IO.Path.GetFileName(file.Path) + " ●");
        }
        _server?.Change(file.Path, file.Text);
    }

    private void Save(int index)
    {
        if (index < 0 || index >= _open.Count)
        {
            return;
        }
        var file = _open[index];
        if (index == _current)
        {
            file.Text = _code.Text;
        }
        File.WriteAllText(file.Path, file.Text);
        file.Dirty = false;
        _tabs.SetTabTitle(index, System.IO.Path.GetFileName(file.Path));
        _server?.Save(file.Path);
    }

    private void CreateFromTemplate()
    {
        var name = _className.Text.Trim();
        if (!Templates.IsJavaIdentifier(name))
        {
            _status.Text = $"'{name}' is not a valid Java class name";
            return;
        }
        var kind = (TemplateKind)_templateKind.Selected;
        var path = Templates.PathFor(_javaDir, name);
        if (File.Exists(path))
        {
            _status.Text = $"{name}.java already exists";
            return;
        }
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);
        File.WriteAllText(path, Templates.Create(kind, name));
        RefreshTree();
        OpenPath(path);
        _status.Text = $"Created {name}.java ({kind})";
    }

    // ------------------------------------------------------------------ build and run

    private void Run()
    {
        if (_building)
        {
            return;
        }
        for (var i = 0; i < _open.Count; i++)
        {
            if (_open[i].Dirty || i == _current)
            {
                Save(i);
            }
        }
        _building = true;
        _status.Text = "Building...";
        Task.Run(() => TeamCodeBuilder.Build(_javaDir, _buildDir)).ContinueWith(t => _mainThread.Enqueue(() => OnBuilt(t)));
    }

    private void OnBuilt(Task<BuildResult> task)
    {
        _building = false;
        if (task.Exception is { } failure)
        {
            _status.Text = "Build could not run: " + failure.InnerException?.Message;
            return;
        }
        var result = task.Result;
        _buildProblems = result.Diagnostics;
        ShowProblems();
        if (!result.Success)
        {
            _status.Text = $"Build failed: {result.Errors.Count()} error(s) ({result.Duration.TotalSeconds:0.0} s)";
            return;
        }
        _program.ReloadTeamCode(result.JarPath!);
        _status.Text = $"Built in {result.Duration.TotalSeconds:0.0} s · robot restarted · {_program.Status}";
    }

    // ------------------------------------------------------------------ language server

    private void StartServer()
    {
        if (_server != null || _serverStarting)
        {
            return;
        }
        _serverStarting = true;
        _status.Text = "Java language server: starting (indexing)...";
        Task.Run(async () =>
        {
            var (shim, _) = JavaLocator.FindRunnerJars();
            var sources = Directory.GetFiles(System.IO.Path.GetDirectoryName(shim)!, "ftc-sdk-shim-*-sources.jar").FirstOrDefault();
            var project = EclipseProject.Create("TeamCode", System.IO.Path.Combine(_toolsDir, "eclipse"), _javaDir, shim, sources);
            return await JavaLanguageServer.Start(project, System.IO.Path.Combine(_toolsDir, "jdtls-data"));
        }).ContinueWith(t => _mainThread.Enqueue(() =>
        {
            _serverStarting = false;
            if (t.Exception is { } e)
            {
                _status.Text = "Java language server unavailable: " + e.InnerException?.Message;
                return;
            }
            _server = t.Result;
            _server.DiagnosticsChanged += (uri, list) => _mainThread.Enqueue(() =>
            {
                _serverProblems[uri] = list;
                ShowProblems();
                PaintDiagnostics();
            });
            _server.Exited += () => _mainThread.Enqueue(() =>
            {
                _server = null;
                _status.Text = "Java language server stopped; reopen the editor (F4) to restart it";
            });
            foreach (var f in _open)
            {
                _server.Open(f.Path, f.Text);
            }
            _status.Text = "Java language server ready";
        }));
    }

    private void RequestCompletion()
    {
        if (_server is not { } server || _current < 0)
        {
            return;
        }
        var path = _open[_current].Path;
        var line = _code.GetCaretLine();
        var column = _code.GetCaretColumn();
        server.Complete(path, line, column).ContinueWith(t => _mainThread.Enqueue(() =>
        {
            if (t.IsFaulted || _current < 0 || _open[_current].Path != path)
            {
                return;
            }
            foreach (var item in t.Result.Take(200))
            {
                var kind = item.Kind switch
                {
                    2 or 3 or 4 => CodeEdit.CodeCompletionKind.Function,
                    5 or 10 => CodeEdit.CodeCompletionKind.Member,
                    6 => CodeEdit.CodeCompletionKind.Variable,
                    7 or 8 or 22 => CodeEdit.CodeCompletionKind.Class,
                    14 => CodeEdit.CodeCompletionKind.PlainText,
                    _ => CodeEdit.CodeCompletionKind.PlainText,
                };
                _code.AddCodeCompletionOption(kind, item.Label, item.InsertText);
            }
            _code.UpdateCodeCompletionOptions(false);
        }));
    }

    private void ShowProblems()
    {
        _problems.Clear();
        _problemTargets.Clear();
        foreach (var d in _buildProblems.Where(d => d.Severity == "error"))
        {
            _problems.AddItem($"build  {System.IO.Path.GetFileName(d.File)}:{d.Line}  {d.Message}");
            _problemTargets.Add((d.File, d.Line - 1));
        }
        foreach (var (uri, list) in _serverProblems)
        {
            var file = new Uri(uri).LocalPath;
            foreach (var d in list.Where(d => d.Severity <= 2))
            {
                _problems.AddItem($"{(d.IsError ? "error" : "warn ")}  {System.IO.Path.GetFileName(file)}:{d.Line + 1}  {d.Message}");
                _problemTargets.Add((file, d.Line));
            }
        }
    }

    private void GoToProblem(int index)
    {
        if (index >= 0 && index < _problemTargets.Count && File.Exists(_problemTargets[index].File))
        {
            OpenPath(_problemTargets[index].File, _problemTargets[index].Line);
        }
    }

    /// <summary>Error lines get a red tint, warnings a yellow one (CodeEdit has no squiggles).</summary>
    private void PaintDiagnostics()
    {
        for (var i = 0; i < _code.GetLineCount(); i++)
        {
            _code.SetLineBackgroundColor(i, new Color(0, 0, 0, 0));
        }
        if (_current < 0)
        {
            return;
        }
        var uri = JavaLanguageServer.Uri(_open[_current].Path);
        if (!_serverProblems.TryGetValue(uri, out var list))
        {
            return;
        }
        foreach (var d in list.Where(d => d.Line < _code.GetLineCount()))
        {
            _code.SetLineBackgroundColor(d.Line, d.IsError ? new Color(0.8f, 0.1f, 0.1f, 0.25f) : new Color(0.8f, 0.7f, 0.1f, 0.18f));
        }
    }

    private static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        b.Pressed += action;
        return b;
    }

    private static CodeHighlighter JavaHighlighter()
    {
        var h = new CodeHighlighter
        {
            NumberColor = new Color(0.71f, 0.81f, 0.66f),
            SymbolColor = new Color(0.67f, 0.75f, 0.85f),
            FunctionColor = new Color(0.86f, 0.86f, 0.67f),
            MemberVariableColor = new Color(0.61f, 0.86f, 0.99f),
        };
        var keyword = new Color(0.98f, 0.55f, 0.73f);
        foreach (var k in new[]
        {
            "abstract", "assert", "break", "case", "catch", "class", "continue", "default", "do", "else", "enum", "extends",
            "final", "finally", "for", "if", "implements", "import", "instanceof", "interface", "native", "new", "package",
            "private", "protected", "public", "return", "static", "super", "switch", "synchronized", "this", "throw", "throws",
            "try", "volatile", "while", "true", "false", "null", "var", "record",
        })
        {
            h.AddKeywordColor(k, keyword);
        }
        var type = new Color(0.56f, 0.82f, 0.62f);
        foreach (var t in new[] { "boolean", "byte", "char", "double", "float", "int", "long", "short", "void", "String" })
        {
            h.AddKeywordColor(t, type);
        }
        h.AddColorRegion("\"", "\"", new Color(0.81f, 0.67f, 0.49f));
        h.AddColorRegion("'", "'", new Color(0.81f, 0.67f, 0.49f));
        h.AddColorRegion("//", "", new Color(0.5f, 0.55f, 0.5f), true);
        h.AddColorRegion("/*", "*/", new Color(0.5f, 0.55f, 0.5f));
        h.AddColorRegion("@", " ", new Color(0.9f, 0.75f, 0.4f), true);
        return h;
    }
}
