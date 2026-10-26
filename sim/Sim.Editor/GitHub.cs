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
