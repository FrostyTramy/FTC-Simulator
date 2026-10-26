using System.Net;
using System.Net.Http;

namespace EditorTests;

/// <summary>
/// Browser sign-in through git's credential helper (D-038), against a scripted helper in place of Git Credential
/// Manager (GIT_CONFIG_* for the git process only: the developer's real credentials are never read or changed).
/// </summary>
public sealed class GitCredentialsTests : IDisposable
{
    private readonly string _log = Path.Combine(Path.GetTempPath(), "ftcsim-cred-" + Guid.NewGuid().ToString("N")[..8] + ".log");

    private void UseHelper(bool hasToken)
    {
        var answer = hasToken ? "echo username=octocat; echo password=gho_scripted;" : "";
        GitCredentials.Environment["GIT_CONFIG_COUNT"] = "2";
        GitCredentials.Environment["GIT_CONFIG_KEY_0"] = "credential.helper";
        GitCredentials.Environment["GIT_CONFIG_VALUE_0"] = "";   // drops the configured helpers
        GitCredentials.Environment["GIT_CONFIG_KEY_1"] = "credential.helper";
        GitCredentials.Environment["GIT_CONFIG_VALUE_1"] =
            $"!f() {{ echo \"$1 $GCM_INTERACTIVE $GCM_GITHUB_AUTHMODES\" >> '{_log}'; if [ \"$1\" = get ]; then {answer} true; fi; }}; f";
    }

    private string[] Log() => File.Exists(_log) ? File.ReadAllLines(_log) : [];

    public void Dispose()
    {
        GitCredentials.Environment.Clear();
        File.Delete(_log);
    }

    [Fact]
    public void Interactive_fill_asks_the_helper_for_a_browser_sign_in_and_approve_keeps_the_token()
    {
        UseHelper(hasToken: true);
        var credential = GitCredentials.Fill(interactive: true)!;
        Assert.Equal(("github.com", "octocat", "gho_scripted"), (credential.Host, credential.User, credential.Secret));
        Assert.DoesNotContain("gho_scripted", credential.ToString());
        GitCredentials.Approve(credential);
        GitCredentials.Reject(credential);
        Assert.Equal(["get auto browser", "store never ", "erase never "], Log());
    }

    [Fact]
    public void A_silent_check_never_opens_a_window()
    {
        UseHelper(hasToken: true);
        Assert.Equal("octocat", GitCredentials.Fill(interactive: false)!.User);
        Assert.Equal(["get never "], Log());
    }

    [Fact]
    public void Without_a_helper_that_signs_in_the_user_is_told_to_install_Git_Credential_Manager()
    {
        UseHelper(hasToken: false);
        Assert.Null(GitCredentials.Fill(interactive: false));
        var e = Assert.Throws<GitException>(() => GitCredentials.Fill(interactive: true));
        Assert.Contains("Git Credential Manager", e.Message);
    }

    [Fact]
    public void Credential_protocol_lines_are_parsed()
    {
        var values = GitCredentials.Parse("protocol=https\r\nhost=github.com\nusername=a\npassword=p=q\n");
        Assert.Equal("p=q", values["password"]);
        Assert.Equal("github.com", values["host"]);
    }

    private sealed class FakeApi(HttpStatusCode status, string body) : HttpMessageHandler
    {
        public HttpRequestMessage? Request { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Request = request;
            return Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body) });
        }
    }

    [Fact]
    public async Task The_token_is_checked_with_GitHub_before_it_is_kept()
    {
        var ok = new FakeApi(HttpStatusCode.OK, """{"login":"octocat","id":1}""");
        Assert.Equal("octocat", await GitCredentials.GitHubLogin(new HttpClient(ok), "gho_scripted"));
        Assert.Equal("https://api.github.com/user", ok.Request!.RequestUri!.ToString());
        Assert.Equal("Bearer gho_scripted", ok.Request.Headers.Authorization!.ToString());
        Assert.NotEmpty(ok.Request.Headers.UserAgent);
        await Assert.ThrowsAsync<GitException>(() => GitCredentials.GitHubLogin(new HttpClient(new FakeApi(HttpStatusCode.Unauthorized, "{}")), "bad"));
    }
}
