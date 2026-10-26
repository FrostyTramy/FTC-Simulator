using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;

namespace Sim.Editor;

/// <summary>git failed; the message is git's own error text.</summary>
public sealed class GitException(string message) : Exception(message);

/// <summary>One changed file from <c>git status</c>.</summary>
/// <param name="Index">Staged state (M, A, D, R, ?, U, or space).</param>
/// <param name="WorkTree">Unstaged state.</param>
public sealed record GitFileStatus(string Path, char Index, char WorkTree)
{
    public bool Untracked => Index == '?';
    public bool Conflicted => Index == 'U' || WorkTree == 'U' || (Index == 'A' && WorkTree == 'A') || (Index == 'D' && WorkTree == 'D');
    public bool Staged => Index is not (' ' or '?') && !Conflicted;
}

/// <summary>Branch state: name, upstream and how far ahead/behind it is.</summary>
public sealed record GitBranchStatus(string Branch, string? Upstream, int Ahead, int Behind);

/// <summary>One commit from the log.</summary>
public sealed record GitCommit(string Hash, string Author, DateTimeOffset Date, string Subject);

/// <summary>Why a commit or push needs the user's explicit confirmation (spec 07 "Safety").</summary>
public sealed record GitWarning(string Kind, string Message);

/// <summary>
/// Git for the editor (spec 07 "Git and GitHub", D-026: the git CLI, so the user's credential helper and config apply).
/// Safety rules from the spec are built in: no force push exists here; pushing to main/master and committing files that
/// look like secrets or are very large return warnings the UI must confirm first.
/// </summary>
public sealed partial class GitClient
{
    public const long LargeFileBytes = 10 * 1024 * 1024;

    public GitClient(string repository, string? accessToken = null)
    {
        Repository = repository;
        AccessToken = accessToken;
    }

    public string Repository { get; }

    /// <summary>
    /// GitHub token from the device-flow sign-in (from <see cref="CredentialStore"/>), or null to use the system's git
    /// credentials. Given to git only through the environment of its own process, never as an argument or in a log.
    /// </summary>
    public string? AccessToken { get; set; }

    /// <param name="branch">Branch to check out (null: the remote's default branch).</param>
    public static GitClient Clone(string url, string targetDir, string? accessToken = null, TimeSpan? timeout = null, string? branch = null)
    {
        var args = branch != null ? new[] { "clone", "--branch", branch, "--", url, targetDir } : ["clone", "--", url, targetDir];
        Run(Path.GetDirectoryName(Path.GetFullPath(targetDir))!, timeout ?? TimeSpan.FromMinutes(10), accessToken, args);
        return new GitClient(targetDir, accessToken);
    }

    /// <summary>
    /// What to clone for an address pasted from the browser: a GitHub page (https://github.com/OWNER/REPO, …/tree/BRANCH,
    /// …/tree/BRANCH/folder, …/blob/BRANCH/file) gives the repository's clone URL and the text after tree/ or blob/ (the
    /// branch, maybe followed by a path: <see cref="ResolveBranch"/>). Any other address (a .git URL, a local folder)
    /// is cloned as it is. Name: the repository name for the workspace folder.
    /// </summary>
    public static (string Url, string? BranchAndPath, string Name) CloneAddress(string address)
    {
        address = address.Trim();
        var m = GitHubPage().Match(address);
        if (!m.Success)
        {
            return (address, null, Path.GetFileNameWithoutExtension(address.TrimEnd('/', '\\')));
        }
        var repo = m.Groups["repo"].Value;
        var rest = m.Groups["rest"].Success ? Uri.UnescapeDataString(m.Groups["rest"].Value.TrimEnd('/')) : null;
        return ($"https://github.com/{m.Groups["owner"].Value}/{repo}.git", string.IsNullOrEmpty(rest) ? null : rest, repo);
    }

    [GeneratedRegex(@"^https?://(www\.)?github\.com/(?<owner>[^/\s]+)/(?<repo>[^/\s]+?)(\.git)?(/(tree|blob|commits)/(?<rest>[^?#\s]+))?/?([?#].*)?$")]
    private static partial Regex GitHubPage();

    /// <summary>
    /// The branch named at the start of <paramref name="branchAndPath"/> (branch names may contain '/'): the longest
    /// branch of the remote that it equals or starts with followed by '/'.
    /// </summary>
    public static string ResolveBranch(string url, string branchAndPath, string? accessToken = null)
    {
        var heads = Run(Path.GetTempPath(), TimeSpan.FromMinutes(2), accessToken, "ls-remote", "--heads", "--", url)
            .Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.Split('\t').Last().Trim())
            .Where(r => r.StartsWith("refs/heads/", StringComparison.Ordinal))
            .Select(r => r["refs/heads/".Length..]);
        return heads.Where(b => branchAndPath == b || branchAndPath.StartsWith(b + "/", StringComparison.Ordinal)).MaxBy(b => b.Length)
               ?? throw new GitException($"the repository has no branch '{branchAndPath.Split('/')[0]}'");
    }

    public static GitClient Init(string dir)
    {
        Directory.CreateDirectory(dir);
        Run(dir, TimeSpan.FromMinutes(1), null, "init");
        return new GitClient(dir);
    }

    public (GitBranchStatus Branch, IReadOnlyList<GitFileStatus> Files) Status()
    {
        var output = Git("status", "--porcelain=v2", "--branch", "-z");
        string branch = "", upstream = "";
        int ahead = 0, behind = 0;
        var files = new List<GitFileStatus>();
        var entries = output.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        for (var i = 0; i < entries.Length; i++)
        {
            var e = entries[i];
            if (e.StartsWith("# branch.head ", StringComparison.Ordinal))
            {
                branch = e["# branch.head ".Length..];
            }
            else if (e.StartsWith("# branch.upstream ", StringComparison.Ordinal))
            {
                upstream = e["# branch.upstream ".Length..];
            }
            else if (e.StartsWith("# branch.ab ", StringComparison.Ordinal))
            {
                var parts = e["# branch.ab ".Length..].Split(' ');
                ahead = int.Parse(parts[0].TrimStart('+'), System.Globalization.CultureInfo.InvariantCulture);
                behind = int.Parse(parts[1].TrimStart('-'), System.Globalization.CultureInfo.InvariantCulture);
            }
            else if (e.StartsWith("1 ", StringComparison.Ordinal))
            {
                files.Add(new GitFileStatus(Field(e, 8), e[2], e[3]));
            }
            else if (e.StartsWith("2 ", StringComparison.Ordinal))
            {
                files.Add(new GitFileStatus(Field(e, 9), e[2], e[3]));
                i++;   // the original path of a rename follows
            }
            else if (e.StartsWith("u ", StringComparison.Ordinal))
            {
                files.Add(new GitFileStatus(Field(e, 10), 'U', 'U'));
            }
            else if (e.StartsWith("? ", StringComparison.Ordinal))
            {
                files.Add(new GitFileStatus(e[2..], '?', '?'));
            }
        }
        return (new GitBranchStatus(branch, upstream.Length > 0 ? upstream : null, ahead, behind), files);
    }

    /// <summary>Unified diff of the working tree (or of the staged changes) for one file or all files.</summary>
    public string Diff(string? path = null, bool staged = false)
    {
        var args = new List<string> { "diff", "--no-color" };
        if (staged)
        {
            args.Add("--cached");
        }
        if (path != null)
        {
            args.AddRange(["--", path]);
        }
        return Git([.. args]);
    }

    public void Stage(params string[] paths) => Git(["add", "--", .. paths]);

    public void Unstage(params string[] paths) => Git(["restore", "--staged", "--", .. paths]);

    /// <summary>Things the user must confirm before committing the staged changes: likely secrets and large files.</summary>
    public IReadOnlyList<GitWarning> CommitWarnings()
    {
        var warnings = new List<GitWarning>();
        var staged = Git("diff", "--cached", "--no-color", "-U0");
        foreach (Match m in SecretPattern().Matches(staged))
        {
            warnings.Add(new GitWarning("secret", $"looks like a secret ({m.Groups["kind"].Value}); never commit tokens or keys"));
        }
        foreach (var file in Git("diff", "--cached", "--name-only", "-z").Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var full = Path.Combine(Repository, file);
            if (File.Exists(full) && new FileInfo(full).Length > LargeFileBytes)
            {
                warnings.Add(new GitWarning("large", $"{file} is larger than {LargeFileBytes / (1024 * 1024)} MB"));
            }
        }
        return warnings;
    }

    /// <summary>Commits the staged changes. Call <see cref="CommitWarnings"/> first and let the user confirm them.</summary>
    public string Commit(string message)
    {
        if (string.IsNullOrWhiteSpace(message))
        {
            throw new GitException("a commit needs a message");
        }
        Git("commit", "-m", message);
        return Git("rev-parse", "HEAD").Trim();
    }

    /// <summary>Things the user must confirm before pushing (shared main/master branch).</summary>
    public IReadOnlyList<GitWarning> PushWarnings()
    {
        var branch = Status().Branch.Branch;
        return branch is "main" or "master"
            ? [new GitWarning("main", $"you are pushing straight to '{branch}', which your whole team shares")]
            : [];
    }

    /// <summary>Pushes the current branch (sets the upstream the first time). There is deliberately no force option.</summary>
    public void Push(string remote = "origin")
    {
        var status = Status().Branch;
        if (status.Upstream == null)
        {
            Git("push", "--set-upstream", remote, status.Branch);
        }
        else
        {
            Git("push", remote, status.Branch);
        }
    }

    /// <summary>Fetch + merge (or rebase) from the upstream. Conflicts are left for the conflict view.</summary>
    public void Pull(bool rebase = false) => Git("pull", rebase ? "--rebase" : "--no-rebase");

    /// <summary>Local branches, then the remote's branches without a local one (switching to one creates it, tracking
    /// the remote branch), each once and by its plain name.</summary>
    public IReadOnlyList<string> Branches()
    {
        var local = Git("for-each-ref", "--format=%(refname:short)", "refs/heads")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();
        var remote = Git("for-each-ref", "--format=%(refname)", "refs/remotes")
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(r => r.Split('/', 4))                     // refs, remotes, REMOTE, BRANCH
            .Where(p => p.Length == 4 && p[3] != "HEAD")
            .Select(p => p[3]);
        return local.Concat(remote.Where(b => !local.Contains(b)).Distinct().OrderBy(b => b, StringComparer.Ordinal)).ToList();
    }

    public void CreateBranch(string name, bool switchTo = true) => Git(switchTo ? ["switch", "-c", name] : ["branch", name]);

    /// <summary>Switches branch; refuses when there are uncommitted changes (the UI offers to stash first).</summary>
    public void Switch(string name)
    {
        if (Status().Files.Any(f => !f.Untracked))
        {
            throw new GitException("you have uncommitted changes: commit or stash them before switching branch");
        }
        Git("switch", name);
    }

    public void Stash(string message) => Git("stash", "push", "-m", message);

    public IReadOnlyList<GitCommit> Log(int max = 50)
    {
        var output = Git("log", $"-n{max}", "--pretty=format:%H%x1f%an%x1f%aI%x1f%s%x1e");
        return output.Split('\u001e', StringSplitOptions.RemoveEmptyEntries).Select(r =>
        {
            var f = r.Trim('\n').Split('\u001f');
            return new GitCommit(f[0], f[1], DateTimeOffset.Parse(f[2], System.Globalization.CultureInfo.InvariantCulture), f[3]);
        }).ToList();
    }

    /// <summary>The three sides of a conflicted file: base, ours, theirs (empty when a side does not exist).</summary>
    public (string Base, string Ours, string Theirs) ConflictSides(string path) =>
        (TryGit("show", $":1:{path}"), TryGit("show", $":2:{path}"), TryGit("show", $":3:{path}"));

    /// <summary>Marks a conflicted file resolved with the given content.</summary>
    public void Resolve(string path, string content)
    {
        File.WriteAllText(Path.Combine(Repository, path), content);
        Git("add", "--", path);
    }

    public void AbortMerge() => Git("merge", "--abort");

    /// <summary>Repository-local setting (e.g. the commit author from the GitHub profile).</summary>
    public void SetConfig(string key, string value) => Git("config", key, value);

    public void AddRemote(string name, string url) => Git("remote", "add", name, url);

    /// <summary>Merges another branch into the current one; returns false when it stopped with conflicts.</summary>
    public bool Merge(string branch)
    {
        try
        {
            Git("merge", "--no-edit", branch);
            return true;
        }
        catch (GitException) when (Status().Files.Any(f => f.Conflicted))
        {
            return false;
        }
    }

    private string Git(params string[] args) => Run(Repository, TimeSpan.FromMinutes(5), AccessToken, args);

    private string TryGit(params string[] args)
    {
        try
        {
            return Git(args);
        }
        catch (GitException)
        {
            return "";
        }
    }

    private static string Field(string entry, int index)
    {
        var parts = entry.Split(' ', index + 1);
        return parts[^1];
    }

    private static string Run(string workingDir, TimeSpan timeout, string? token, params string[] args)
    {
        var info = new ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            WorkingDirectory = workingDir,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8,
        };
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";   // credentials come from the helper, never from a hidden prompt
        if (token != null)
        {
            // Only this helper answers (the empty value resets the configured ones); the token stays in the environment.
            info.Environment["FTCSIM_GH_TOKEN"] = token;
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("credential.helper=");
            info.ArgumentList.Add("-c");
            info.ArgumentList.Add("credential.helper=!f() { echo username=x-access-token; echo \"password=$FTCSIM_GH_TOKEN\"; }; f");
        }
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }
        using var process = Process.Start(info) ?? throw new GitException("git is not installed");
        var stdout = process.StandardOutput.ReadToEndAsync();
        var stderr = process.StandardError.ReadToEndAsync();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new GitException($"git {args[0]} did not finish in time");
        }
        if (process.ExitCode != 0)
        {
            throw new GitException(stderr.Result.Trim() is { Length: > 0 } err ? err : $"git {args[0]} failed ({process.ExitCode})");
        }
        return stdout.Result;
    }

    [GeneratedRegex(@"(?<kind>ghp_[A-Za-z0-9]{20,}|github_pat_[A-Za-z0-9_]{20,}|gho_[A-Za-z0-9]{20,}|AKIA[0-9A-Z]{16}|-----BEGIN [A-Z ]*PRIVATE KEY-----|xox[baprs]-[A-Za-z0-9-]{10,})")]
    private static partial Regex SecretPattern();
}
