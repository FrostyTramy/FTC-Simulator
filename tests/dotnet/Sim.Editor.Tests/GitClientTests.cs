namespace EditorTests;

/// <summary>Spec 07 git flow on throwaway repositories (a local bare repository stands in for GitHub).</summary>
public class GitClientTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "ftcsim-git-" + Guid.NewGuid().ToString("N")[..8]);

    private GitClient NewRepo(string name = "repo")
    {
        var git = GitClient.Init(Path.Combine(_root, name));
        git.SetConfig("user.name", "Sim Test");
        git.SetConfig("user.email", "sim@example.invalid");
        git.SetConfig("commit.gpgsign", "false");
        return git;
    }

    private static void Write(GitClient git, string file, string text) => File.WriteAllText(Path.Combine(git.Repository, file), text);

    [Fact]
    public void Status_stage_commit_and_log()
    {
        var git = NewRepo();
        Write(git, "Drive.java", "class Drive {}\n");
        var (_, files) = git.Status();
        Assert.True(Assert.Single(files).Untracked);
        git.Stage("Drive.java");
        Assert.True(Assert.Single(git.Status().Files).Staged);
        Assert.Empty(git.CommitWarnings());
        var hash = git.Commit("Add drive");
        Assert.Equal(40, hash.Length);
        Assert.Equal("Add drive", Assert.Single(git.Log()).Subject);
        Write(git, "Drive.java", "class Drive { int x; }\n");
        Assert.Contains("+class Drive { int x; }", git.Diff("Drive.java"));
    }

    [Fact]
    public void Secrets_and_main_branch_pushes_need_confirmation()
    {
        var git = NewRepo();
        Write(git, "Secrets.java", "String token = \"ghp_" + new string('a', 36) + "\";\n");
        git.Stage("Secrets.java");
        Assert.Contains(git.CommitWarnings(), w => w.Kind == "secret");
        git.Commit("oops");
        git.CreateBranch("main");
        Assert.Contains(git.PushWarnings(), w => w.Kind == "main");
        git.CreateBranch("feature");
        Assert.Empty(git.PushWarnings());
    }

    [Fact]
    public void Switching_branch_with_uncommitted_changes_is_refused()
    {
        var git = NewRepo();
        Write(git, "A.java", "class A {}\n");
        git.Stage("A.java");
        git.Commit("A");
        git.CreateBranch("other");
        Write(git, "A.java", "class A { }\n");
        Assert.Throws<GitException>(() => git.Switch("master"));
    }

    [Fact]
    public void Push_to_a_remote_sets_the_upstream_and_reports_ahead()
    {
        var remote = GitClient.Init(Path.Combine(_root, "remote.git"));
        var git = NewRepo();
        git.AddRemote("origin", remote.Repository);
        Write(git, "A.java", "class A {}\n");
        git.Stage("A.java");
        git.Commit("A");
        git.CreateBranch("work");
        git.Push();
        var status = git.Status().Branch;
        Assert.Equal("origin/work", status.Upstream);
        Write(git, "B.java", "class B {}\n");
        git.Stage("B.java");
        git.Commit("B");
        Assert.Equal(1, git.Status().Branch.Ahead);
    }

    [Fact]
    public void Merge_conflicts_show_three_sides_and_can_be_resolved()
    {
        var git = NewRepo();
        Write(git, "A.java", "int speed = 1;\n");
        git.Stage("A.java");
        git.Commit("base");
        var start = git.Status().Branch.Branch;
        git.CreateBranch("fast");
        Write(git, "A.java", "int speed = 2;\n");
        git.Stage("A.java");
        git.Commit("fast");
        git.Switch(start);
        Write(git, "A.java", "int speed = 3;\n");
        git.Stage("A.java");
        git.Commit("slow");
        Assert.False(git.Merge("fast"));
        var conflicted = Assert.Single(git.Status().Files, f => f.Conflicted);
        var (b, ours, theirs) = git.ConflictSides(conflicted.Path);
        Assert.Equal(("int speed = 1;\n", "int speed = 3;\n", "int speed = 2;\n"), (b, ours, theirs));
        git.Resolve(conflicted.Path, "int speed = 2;\n");
        git.Commit("merge fast");
        Assert.DoesNotContain(git.Status().Files, f => f.Conflicted);
    }

    public void Dispose()
    {
        try
        {
            foreach (var f in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
            {
                File.SetAttributes(f, FileAttributes.Normal);   // git object files are read-only
            }
            Directory.Delete(_root, recursive: true);
        }
        catch (IOException)
        {
        }
    }
}
