using System.Net.Http;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json.Nodes;

namespace Sim.Editor;

/// <summary>What the user must do to finish signing in: open <see cref="VerificationUri"/> and enter <see cref="UserCode"/>.</summary>
public sealed record DeviceCode(string Code, string UserCode, string VerificationUri, TimeSpan Interval, DateTimeOffset ExpiresAt);

/// <summary>
/// GitHub OAuth device flow (spec 07 "Sign in"): no client secret, no browser redirect. Needs the OAuth App's client ID
/// (config/github.yaml, provided by the user; never a secret). The token goes to <see cref="CredentialStore"/> only.
/// </summary>
public sealed class GitHubDeviceFlow(HttpClient http, string clientId)
{
    public async Task<DeviceCode> Start(string scope = "repo", CancellationToken cancel = default)
    {
        var json = await Post("https://github.com/login/device/code", $"client_id={Uri.EscapeDataString(clientId)}&scope={Uri.EscapeDataString(scope)}", cancel);
        if (json["error"] is { } error)
        {
            throw new GitException("GitHub sign-in: " + (json["error_description"]?.GetValue<string>() ?? error.GetValue<string>()));
        }
        return new DeviceCode(
            json["device_code"]!.GetValue<string>(),
            json["user_code"]!.GetValue<string>(),
            json["verification_uri"]!.GetValue<string>(),
            TimeSpan.FromSeconds(json["interval"]?.GetValue<int>() ?? 5),
            DateTimeOffset.UtcNow.AddSeconds(json["expires_in"]?.GetValue<int>() ?? 900));
    }

    /// <summary>Polls until the user approved (returns the token), denied or the code expired (throws).</summary>
    public async Task<string> WaitForToken(DeviceCode code, CancellationToken cancel = default, Func<TimeSpan, Task>? delay = null)
    {
        delay ??= t => Task.Delay(t, cancel);
        var interval = code.Interval;
        while (DateTimeOffset.UtcNow < code.ExpiresAt)
        {
            await delay(interval);
            var json = await Post("https://github.com/login/oauth/access_token",
                $"client_id={Uri.EscapeDataString(clientId)}&device_code={Uri.EscapeDataString(code.Code)}&grant_type=urn:ietf:params:oauth:grant-type:device_code",
                cancel);
            if (json["access_token"]?.GetValue<string>() is { Length: > 0 } token)
            {
                return token;
            }
            switch (json["error"]?.GetValue<string>())
            {
                case "authorization_pending":
                    continue;
                case "slow_down":
                    interval += TimeSpan.FromSeconds(5);
                    continue;
                case "access_denied":
                    throw new GitException("GitHub sign-in was cancelled");
                case "expired_token":
                    throw new GitException("the sign-in code expired; start again");
                default:
                    throw new GitException("GitHub sign-in failed: " + (json["error_description"]?.GetValue<string>() ?? json.ToJsonString()));
            }
        }
        throw new GitException("the sign-in code expired; start again");
    }

    private async Task<JsonNode> Post(string url, string form, CancellationToken cancel)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(form, Encoding.UTF8, "application/x-www-form-urlencoded"),
        };
        request.Headers.Accept.ParseAdd("application/json");
        using var response = await http.SendAsync(request, cancel);
        var body = await response.Content.ReadAsStringAsync(cancel);
        return JsonNode.Parse(body) ?? throw new GitException("GitHub answered with an empty response");
    }
}

/// <summary>A credential git's helper gave for one host: the account and its token (never printed: see ToString).</summary>
public sealed record GitCredential(string Host, string User, string Secret)
{
    public override string ToString() => $"{User}@{Host}";
}

/// <summary>
/// GitHub sign-in in the browser through Git Credential Manager (USER 2026-10-26, D-038). GCM ships with Git for Windows
/// and is git's credential helper there: <c>git credential fill</c> for github.com makes it open the browser on GitHub's
/// own sign-in page (GCM_GITHUB_AUTHMODES=browser) when it has no token yet, and it keeps the token in Windows Credential
/// Manager (git:https://github.com). No OAuth App of ours, no client ID (Q-010), and the simulator never stores the
/// token: git asks the helper for it on every pull and push, and the API check here only holds it in memory.
/// </summary>
public static class GitCredentials
{
    public const string GitHubHost = "github.com";

    /// <summary>Extra environment for the git process (tests point git at a scripted helper with GIT_CONFIG_*).</summary>
    public static Dictionary<string, string> Environment { get; } = new();

    /// <summary>
    /// The github.com credential from git's helper. Interactive: a helper without one asks (GCM: the browser sign-in),
    /// for up to <paramref name="timeout"/>. Not interactive: only what is stored, no window (GCM_INTERACTIVE=never).
    /// Null when there is none; throws <see cref="GitException"/> when git or the helper failed.
    /// </summary>
    public static GitCredential? Fill(bool interactive, TimeSpan? timeout = null)
    {
        var (ok, output, error) = Run("fill", $"protocol=https\nhost={GitHubHost}\n\n", interactive, timeout ?? TimeSpan.FromSeconds(interactive ? 300 : 20));
        if (!ok)
        {
            if (interactive && error.Contains("terminal prompts disabled", StringComparison.Ordinal))
            {
                throw new GitException("no sign-in window: Git Credential Manager is not your git credential helper. Install Git for Windows " +
                                       "(it includes it) or run: git config --global credential.helper manager");
            }
            return interactive ? throw new GitException("GitHub sign-in did not finish: " + FirstLine(error)) : null;
        }
        var values = Parse(output);
        return values.TryGetValue("password", out var secret) && secret.Length > 0
            ? new GitCredential(GitHubHost, values.GetValueOrDefault("username", ""), secret)
            : null;
    }

    /// <summary>Tells the helper the credential works (GCM keeps it).</summary>
    public static void Approve(GitCredential credential) => Run("approve", Describe(credential), false, TimeSpan.FromSeconds(20));

    /// <summary>Tells the helper to forget the credential (sign out, or a token GitHub refused).</summary>
    public static void Reject(GitCredential credential) => Run("reject", Describe(credential), false, TimeSpan.FromSeconds(20));

    /// <summary>The GitHub login a token belongs to (GET /user); throws when GitHub refuses the token.</summary>
    public static async Task<string> GitHubLogin(HttpClient http, string token, CancellationToken cancel = default)
    {
        using var request = new HttpRequestMessage(HttpMethod.Get, "https://api.github.com/user");
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
        request.Headers.UserAgent.ParseAdd("BiobuzzSim");
        request.Headers.Accept.ParseAdd("application/vnd.github+json");
        using var response = await http.SendAsync(request, cancel);
        if (!response.IsSuccessStatusCode)
        {
            throw new GitException($"GitHub refused the sign-in ({(int)response.StatusCode})");
        }
        var json = JsonNode.Parse(await response.Content.ReadAsStringAsync(cancel));
        return json?["login"]?.GetValue<string>() ?? throw new GitException("GitHub did not name the account");
    }

    /// <summary>key=value lines of the git credential protocol.</summary>
    public static Dictionary<string, string> Parse(string output) =>
        output.Split('\n', StringSplitOptions.RemoveEmptyEntries)
            .Select(line => line.TrimEnd('\r').Split('=', 2))
            .Where(kv => kv.Length == 2)
            .GroupBy(kv => kv[0])
            .ToDictionary(g => g.Key, g => g.Last()[1]);

    private static string Describe(GitCredential c) => $"protocol=https\nhost={c.Host}\nusername={c.User}\npassword={c.Secret}\n\n";

    private static string FirstLine(string text) => text.Split('\n').Select(l => l.Trim()).FirstOrDefault(l => l.Length > 0) ?? "no answer";

    private static (bool Ok, string Output, string Error) Run(string action, string input, bool interactive, TimeSpan timeout)
    {
        var info = new System.Diagnostics.ProcessStartInfo("git")
        {
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
        };
        info.ArgumentList.Add("credential");
        info.ArgumentList.Add(action);
        foreach (var (key, value) in Environment)
        {
            info.Environment[key] = value;
        }
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";             // never a hidden console prompt
        info.Environment["GCM_INTERACTIVE"] = interactive ? "auto" : "never";
        if (interactive)
        {
            info.Environment["GCM_GITHUB_AUTHMODES"] = "browser";  // straight to the browser, no mode chooser
        }
        using var process = System.Diagnostics.Process.Start(info) ?? throw new GitException("git is not installed");
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        process.StandardInput.Write(input);
        process.StandardInput.Close();
        if (!process.WaitForExit((int)timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new GitException(interactive ? "GitHub sign-in timed out; start again" : $"git credential {action} did not answer");
        }
        return (process.ExitCode == 0, output.Result, error.Result);
    }
}

/// <summary>
/// Windows Credential Manager (spec 07: "Token stored only in Windows Credential Manager"). Generic credentials of the
/// current user; never logged, never written to files. On other systems (CI, development containers) there is no store:
/// nothing is saved and git uses the system's own credentials.
/// </summary>
public static class CredentialStore
{
    public const string GitHubTarget = "FtcSimulator:github";

    public static bool Available => OperatingSystem.IsWindows();

    public static void Save(string target, string user, string secret)
    {
        if (!Available)
        {
            throw new GitException("signing in to GitHub needs the Windows Credential Manager");
        }
        var blob = Encoding.Unicode.GetBytes(secret);
        var handle = GCHandle.Alloc(blob, GCHandleType.Pinned);
        try
        {
            var credential = new Credential
            {
                Type = 1,             // CRED_TYPE_GENERIC
                TargetName = target,
                UserName = user,
                CredentialBlob = handle.AddrOfPinnedObject(),
                CredentialBlobSize = blob.Length,
                Persist = 2,          // CRED_PERSIST_LOCAL_MACHINE
            };
            if (!CredWrite(ref credential, 0))
            {
                throw new GitException($"could not store the credential (Windows error {Marshal.GetLastWin32Error()})");
            }
        }
        finally
        {
            handle.Free();
            Array.Clear(blob);
        }
    }

    public static (string User, string Secret)? Load(string target)
    {
        if (!Available || !CredRead(target, 1, 0, out var pointer))
        {
            return null;
        }
        try
        {
            var c = Marshal.PtrToStructure<Credential>(pointer);
            var secret = c.CredentialBlobSize > 0 ? Marshal.PtrToStringUni(c.CredentialBlob, c.CredentialBlobSize / 2) : "";
            return (c.UserName ?? "", secret);
        }
        finally
        {
            CredFree(pointer);
        }
    }

    public static void Delete(string target)
    {
        if (Available)
        {
            CredDelete(target, 1, 0);
        }
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public int Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public long LastWritten;
        public int CredentialBlobSize;
        public IntPtr CredentialBlob;
        public int Persist;
        public int AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string? UserName;
    }

    [DllImport("advapi32.dll", EntryPoint = "CredWriteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite(ref Credential credential, int flags);

    [DllImport("advapi32.dll", EntryPoint = "CredReadW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int flags, out IntPtr credential);

    [DllImport("advapi32.dll", EntryPoint = "CredDeleteW", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree(IntPtr buffer);
}
