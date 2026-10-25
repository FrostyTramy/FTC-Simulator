using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Sim.Editor;

/// <summary>The language server failed or answered with an error.</summary>
public sealed class LspException(string message) : Exception(message);

/// <summary>
/// Minimal Language Server Protocol client (JSON-RPC 2.0 over stdio with Content-Length framing). Requests return
/// their result; notifications from the server go to <see cref="Notification"/>; requests from the server are answered
/// with an empty result (configuration, capability registration, progress tokens) so the server never waits on us.
/// </summary>
public sealed class LspClient : IDisposable
{
    private readonly Stream _input;    // server stdin
    private readonly Stream _output;   // server stdout
    private readonly ConcurrentDictionary<int, TaskCompletionSource<JsonNode?>> _pending = new();
    private readonly object _writeLock = new();
    private readonly Thread _reader;
    private int _nextId;
    private volatile bool _closed;

    public LspClient(Stream serverInput, Stream serverOutput)
    {
        _input = serverInput;
        _output = serverOutput;
        _reader = new Thread(ReadLoop) { IsBackground = true, Name = "LSP reader" };
        _reader.Start();
    }

    /// <summary>Server notifications (method, params), raised on the reader thread.</summary>
    public event Action<string, JsonNode?>? Notification;

    /// <summary>Raised once when the server's output ends.</summary>
    public event Action? Closed;

    public async Task<JsonNode?> Request(string method, object? parameters, TimeSpan? timeout = null, CancellationToken cancel = default)
    {
        var id = Interlocked.Increment(ref _nextId);
        var tcs = new TaskCompletionSource<JsonNode?>(TaskCreationOptions.RunContinuationsAsynchronously);
        _pending[id] = tcs;
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = id, ["method"] = method, ["params"] = ToNode(parameters) });
        using var timer = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(60));
        using var linked = CancellationTokenSource.CreateLinkedTokenSource(timer.Token, cancel);
        await using var registration = linked.Token.Register(() =>
        {
            if (_pending.TryRemove(id, out var t))
            {
                t.TrySetException(new LspException($"{method}: no answer in time"));
            }
        });
        return await tcs.Task.ConfigureAwait(false);
    }

    public void Notify(string method, object? parameters) =>
        Send(new JsonObject { ["jsonrpc"] = "2.0", ["method"] = method, ["params"] = ToNode(parameters) });

    public void Dispose()
    {
        _closed = true;
        foreach (var (_, tcs) in _pending)
        {
            tcs.TrySetException(new LspException("language server closed"));
        }
    }

    /// <summary>One frame: "Content-Length: N\r\n\r\n" + N bytes of UTF-8 JSON.</summary>
    public static byte[] Frame(JsonNode message)
    {
        var body = Encoding.UTF8.GetBytes(message.ToJsonString());
        var header = Encoding.ASCII.GetBytes($"Content-Length: {body.Length}\r\n\r\n");
        return [.. header, .. body];
    }

    /// <summary>Reads one frame; null at end of stream.</summary>
    public static JsonNode? ReadFrame(Stream stream)
    {
        var length = -1;
        while (true)
        {
            var line = ReadHeaderLine(stream);
            if (line == null)
            {
                return null;
            }
            if (line.Length == 0)
            {
                break;
            }
            if (line.StartsWith("Content-Length:", StringComparison.OrdinalIgnoreCase))
            {
                length = int.Parse(line["Content-Length:".Length..].Trim(), System.Globalization.CultureInfo.InvariantCulture);
            }
        }
        if (length < 0)
        {
            throw new LspException("frame without Content-Length");
        }
        var body = new byte[length];
        stream.ReadExactly(body);
        return JsonNode.Parse(body);
    }

    private static string? ReadHeaderLine(Stream stream)
    {
        var bytes = new List<byte>();
        while (true)
        {
            var b = stream.ReadByte();
            if (b < 0)
            {
                return bytes.Count == 0 ? null : throw new EndOfStreamException("language server output ended inside a header");
            }
            if (b == '\n')
            {
                return Encoding.ASCII.GetString(bytes.ToArray()).TrimEnd('\r');
            }
            bytes.Add((byte)b);
        }
    }

    private void Send(JsonNode message)
    {
        if (_closed)
        {
            throw new LspException("language server closed");
        }
        var frame = Frame(message);
        lock (_writeLock)
        {
            _input.Write(frame);
            _input.Flush();
        }
    }

    private void ReadLoop()
    {
        try
        {
            while (!_closed && ReadFrame(_output) is { } message)
            {
                Dispatch(message);
            }
        }
        catch (Exception e) when (e is IOException or ObjectDisposedException or EndOfStreamException or JsonException or LspException)
        {
            Debug.WriteLine("LSP reader stopped: " + e.Message);
        }
        _closed = true;
        foreach (var (_, tcs) in _pending)
        {
            tcs.TrySetException(new LspException("language server closed"));
        }
        Closed?.Invoke();
    }

    private void Dispatch(JsonNode message)
    {
        var method = message["method"]?.GetValue<string>();
        var idNode = message["id"];
        if (method == null && idNode != null)
        {
            // A response to one of our requests.
            if (_pending.TryRemove(idNode.GetValue<int>(), out var tcs))
            {
                if (message["error"] is { } error)
                {
                    tcs.TrySetException(new LspException(error["message"]?.GetValue<string>() ?? error.ToJsonString()));
                }
                else
                {
                    tcs.TrySetResult(message["result"]?.DeepClone());
                }
            }
            return;
        }
        if (method != null && idNode != null)
        {
            // A request from the server: answer so it never blocks (configuration → one null per item).
            JsonNode? result = null;
            if (method == "workspace/configuration" && message["params"]?["items"] is JsonArray items)
            {
                result = new JsonArray(items.Select(_ => (JsonNode?)null).ToArray());
            }
            Send(new JsonObject { ["jsonrpc"] = "2.0", ["id"] = idNode.DeepClone(), ["result"] = result });
            return;
        }
        if (method != null)
        {
            Notification?.Invoke(method, message["params"]);
        }
    }

    private static JsonNode? ToNode(object? value) => value switch
    {
        null => null,
        JsonNode node => node,
        _ => JsonSerializer.SerializeToNode(value, value.GetType(), new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase }),
    };
}
