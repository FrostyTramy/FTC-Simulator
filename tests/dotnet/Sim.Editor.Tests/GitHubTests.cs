using System.Net;
using System.Net.Http;

namespace EditorTests;

/// <summary>Device-flow sign-in against a scripted fake GitHub (no network, no credential store).</summary>
public class GitHubTests
{
    private sealed class FakeGitHub(Queue<string> answers) : HttpMessageHandler
    {
        public List<string> Requests { get; } = [];

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add(request.RequestUri!.AbsolutePath + "?" + await request.Content!.ReadAsStringAsync(cancellationToken));
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(answers.Dequeue()) };
        }
    }

    [Fact]
    public async Task Device_flow_polls_until_the_user_approves_and_backs_off_when_asked()
    {
        var fake = new FakeGitHub(new Queue<string>(
        [
            """{"device_code":"dc1","user_code":"ABCD-1234","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""",
            """{"error":"authorization_pending"}""",
            """{"error":"slow_down"}""",
            """{"access_token":"gho_test","token_type":"bearer","scope":"repo"}""",
        ]));
        var flow = new GitHubDeviceFlow(new HttpClient(fake), "Iv1.testclient");
        var code = await flow.Start();
        Assert.Equal(("ABCD-1234", "https://github.com/login/device"), (code.UserCode, code.VerificationUri));
        var waits = new List<TimeSpan>();
        var token = await flow.WaitForToken(code, delay: t => { waits.Add(t); return Task.CompletedTask; });
        Assert.Equal("gho_test", token);
        Assert.Equal([TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(5), TimeSpan.FromSeconds(10)], waits);
        Assert.StartsWith("/login/device/code?client_id=Iv1.testclient&scope=repo", fake.Requests[0]);
        Assert.Contains("grant_type=urn:ietf:params:oauth:grant-type:device_code", fake.Requests[1]);
    }

    [Fact]
    public async Task Denied_sign_in_is_reported()
    {
        var fake = new FakeGitHub(new Queue<string>(
        [
            """{"device_code":"dc1","user_code":"X","verification_uri":"https://github.com/login/device","expires_in":900,"interval":5}""",
            """{"error":"access_denied"}""",
        ]));
        var flow = new GitHubDeviceFlow(new HttpClient(fake), "Iv1.testclient");
        var code = await flow.Start();
        await Assert.ThrowsAsync<GitException>(() => flow.WaitForToken(code, delay: _ => Task.CompletedTask));
    }
}
