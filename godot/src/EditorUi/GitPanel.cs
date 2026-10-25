using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using Sim.App;
using Sim.Editor;
using YamlDotNet.Serialization;

namespace Sim.EditorUi;

/// <summary>
/// Git panel (spec 07 "Git and GitHub", Phase 8), toggled with F6: branch with ahead/behind, changed files and their
/// diff, stage/unstage, commit, pull, push, branches, and GitHub sign-in by device flow (token only in Windows
/// Credential Manager). Commits that look like they contain secrets or huge files and pushes to main/master ask for
/// confirmation first; there is no force push. git runs on worker threads; results come back on the main thread.
/// </summary>
public partial class GitPanel : PanelContainer
{
    private readonly string _repository;
    private readonly ConcurrentQueue<Action> _mainThread = new();
    private GitClient _git = null!;
    private Label _branch = null!;
    private Label _status = null!;
    private ItemList _files = null!;
    private TextEdit _diff = null!;
    private LineEdit _message = null!;
    private OptionButton _branches = null!;
    private LineEdit _newBranch = null!;
    private ConfirmationDialog _confirm = null!;
    private List<GitFileStatus> _changes = new();
    private Action? _confirmed;
    private bool _busy;

    public GitPanel(string repository)
    {
        _repository = repository;
    }

    public override void _Ready()
    {
        Visible = false;
        AnchorBottom = 1;
        AnchorRight = 0.66f;
        OffsetLeft = 8;
        OffsetTop = 120;
        OffsetBottom = -8;
        AddThemeStyleboxOverride("panel", new StyleBoxFlat { BgColor = new Color(0.11f, 0.12f, 0.14f, 0.98f) });
        var token = CredentialStore.Load(CredentialStore.GitHubTarget)?.Secret;
        _git = new GitClient(_repository, token);

        var root = new VBoxContainer();
        AddChild(root);
        var top = new HBoxContainer();
        _branch = new Label { SizeFlagsHorizontal = SizeFlags.ExpandFill };
        top.AddChild(_branch);
        top.AddChild(Button("Refresh", Refresh));
        top.AddChild(Button("Pull", () => Work("Pulling...", () => _git.Pull(), "Pulled")));
        top.AddChild(Button("Push", Push));
        top.AddChild(Button(token != null ? "Signed in to GitHub ✓" : "Sign in to GitHub", SignIn));
        root.AddChild(top);

        var repoRow = new HBoxContainer();
        var url = new LineEdit { PlaceholderText = "https://github.com/team/FtcRobotController.git", SizeFlagsHorizontal = SizeFlags.ExpandFill };
        repoRow.AddChild(url);
        repoRow.AddChild(Button("Clone into workspace", () =>
        {
            var address = url.Text.Trim();
            var name = System.IO.Path.GetFileNameWithoutExtension(address.TrimEnd('/'));
            if (address.Length == 0 || name.Length == 0)
            {
                return;
            }
            var target = System.IO.Path.Combine(System.IO.Path.GetDirectoryName(_repository)!, name);
            Work($"Cloning {address}...", () => GitClient.Clone(address, target, _git.AccessToken),
                $"Cloned into {target}. Open it with: --project {target}");
        }));
        repoRow.AddChild(Button("Init repository here", () => Work("Initializing...", () => GitClient.Init(_repository), "Initialized an empty repository")));
        root.AddChild(repoRow);

        var split = new HSplitContainer { SizeFlagsVertical = SizeFlags.ExpandFill, SplitOffset = 260 };
        var left = new VBoxContainer();
        _files = new ItemList { SizeFlagsVertical = SizeFlags.ExpandFill, SelectMode = ItemList.SelectModeEnum.Multi, FocusMode = FocusModeEnum.None };
        _files.ItemSelected += index => ShowDiff((int)index);
        left.AddChild(_files);
        var stageRow = new HBoxContainer();
        stageRow.AddChild(Button("Stage", () => ForSelected(paths => _git.Stage(paths))));
        stageRow.AddChild(Button("Unstage", () => ForSelected(paths => _git.Unstage(paths))));
        left.AddChild(stageRow);
        _message = new LineEdit { PlaceholderText = "Commit message" };
        left.AddChild(_message);
        left.AddChild(Button("Commit staged", Commit));
        var branchRow = new HBoxContainer();
        _branches = new OptionButton { FocusMode = FocusModeEnum.None, SizeFlagsHorizontal = SizeFlags.ExpandFill };
        _branches.ItemSelected += index => SwitchBranch(_branches.GetItemText((int)index));
        _newBranch = new LineEdit { PlaceholderText = "new-branch", CustomMinimumSize = new Vector2(100, 0) };
        branchRow.AddChild(_branches);
        branchRow.AddChild(_newBranch);
        branchRow.AddChild(Button("Create", () =>
        {
            var name = _newBranch.Text.Trim();
            if (name.Length > 0)
            {
                Work("Creating branch...", () => _git.CreateBranch(name), $"On new branch {name}");
            }
        }));
        left.AddChild(branchRow);
        split.AddChild(left);
        _diff = new TextEdit { Editable = false, SizeFlagsVertical = SizeFlags.ExpandFill };
        split.AddChild(_diff);
        root.AddChild(split);
        _status = new Label { ClipText = true };
        root.AddChild(_status);

        _confirm = new ConfirmationDialog { Title = "Please confirm" };
        _confirm.Confirmed += () => _confirmed?.Invoke();
        AddChild(_confirm);
    }

    public override void _Input(InputEvent e)
    {
        if (e is InputEventKey { Pressed: true, Echo: false, Keycode: Key.F6 })
        {
            Visible = !Visible;
            if (Visible)
            {
                Refresh();
            }
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

    private void Refresh()
    {
        Run(() => (_git.Status(), _git.Branches()), result =>
        {
            var ((branch, files), branches) = result;
            _branch.Text = $"{branch.Branch}" + (branch.Upstream != null ? $"  ↑{branch.Ahead} ↓{branch.Behind}  ({branch.Upstream})" : "  (not pushed yet)");
            _changes = files.ToList();
            _files.Clear();
            foreach (var f in _changes)
            {
                var state = f.Conflicted ? "C!" : f.Untracked ? "??" : $"{f.Index}{f.WorkTree}";
                _files.AddItem($"{state}  {f.Path}");
            }
            _branches.Clear();
            foreach (var b in branches)
            {
                _branches.AddItem(b);
                if (b == branch.Branch)
                {
                    _branches.Select(_branches.ItemCount - 1);
                }
            }
        });
    }

    private void ShowDiff(int index)
    {
        if (index < 0 || index >= _changes.Count)
        {
            return;
        }
        var file = _changes[index];
        Run(() => file.Untracked ? "(new file)" : _git.Diff(file.Path, staged: file.Staged && file.WorkTree == ' '), text => _diff.Text = text);
    }

    private void ForSelected(Action<string[]> action)
    {
        var paths = _files.GetSelectedItems().Select(i => _changes[i].Path).ToArray();
        if (paths.Length > 0)
        {
            Work("Updating...", () => action(paths), "Done");
        }
    }

    private void Commit()
    {
        var message = _message.Text.Trim();
        if (message.Length == 0)
        {
            _status.Text = "Write a commit message first";
            return;
        }
        Run(() => _git.CommitWarnings(), warnings =>
        {
            void DoCommit() => Work("Committing...", () => _git.Commit(message), "Committed", () => _message.Text = "");
            if (warnings.Count == 0)
            {
                DoCommit();
            }
            else
            {
                Ask(string.Join("\n", warnings.Select(w => "• " + w.Message)) + "\n\nCommit anyway?", DoCommit);
            }
        });
    }

    private void Push()
    {
        Run(() => _git.PushWarnings(), warnings =>
        {
            void DoPush() => Work("Pushing...", () => _git.Push(), "Pushed");
            if (warnings.Count == 0)
            {
                DoPush();
            }
            else
            {
                Ask(string.Join("\n", warnings.Select(w => "• " + w.Message)) + "\n\nPush anyway?", DoPush);
            }
        });
    }

    private void SwitchBranch(string name) => Work($"Switching to {name}...", () => _git.Switch(name), $"On {name}");

    private void SignIn()
    {
        var clientId = ClientId();
        if (clientId.Length == 0)
        {
            _status.Text = "GitHub sign-in needs an OAuth App client ID in config/github.yaml (Q-010); git uses your system credentials meanwhile";
            return;
        }
        var flow = new GitHubDeviceFlow(new System.Net.Http.HttpClient(), clientId);
        Run(() => flow.Start().GetAwaiter().GetResult(), code =>
        {
            _status.Text = $"Open {code.VerificationUri} and enter {code.UserCode}";
            OS.ShellOpen(code.VerificationUri);
            Run(() => flow.WaitForToken(code).GetAwaiter().GetResult(), token =>
            {
                CredentialStore.Save(CredentialStore.GitHubTarget, "x-access-token", token);
                _git.AccessToken = token;
                _status.Text = "Signed in to GitHub (token stored in Windows Credential Manager)";
            });
        });
    }

    private static string ClientId()
    {
        var root = new DeserializerBuilder().Build().Deserialize<Dictionary<object, object>>(System.IO.File.ReadAllText(RepoPaths.Config("github.yaml")));
        return (root["oauth_client_id"] as Dictionary<object, object>)?["value"]?.ToString()?.Trim() ?? "";
    }

    private void Ask(string text, Action onYes)
    {
        _confirm.DialogText = text;
        _confirmed = onYes;
        _confirm.PopupCentered(new Vector2I(520, 200));
    }

    private void Work(string busyText, Action action, string doneText, Action? after = null) =>
        Run(() =>
        {
            action();
            return true;
        }, _ =>
        {
            _status.Text = doneText;
            after?.Invoke();
            Refresh();
        }, busyText);

    /// <summary>Runs git work on a worker thread; errors are shown as git reported them.</summary>
    private void Run<T>(Func<T> work, Action<T> done, string? busyText = null)
    {
        if (_busy)
        {
            return;
        }
        _busy = true;
        if (busyText != null)
        {
            _status.Text = busyText;
        }
        Task.Run(work).ContinueWith(t => _mainThread.Enqueue(() =>
        {
            _busy = false;
            if (t.Exception is { } e)
            {
                _status.Text = e.InnerException?.Message ?? e.Message;
                return;
            }
            done(t.Result);
        }));
    }

    private static Button Button(string text, Action action)
    {
        var b = new Button { Text = text, FocusMode = FocusModeEnum.None };
        b.Pressed += action;
        return b;
    }
}
