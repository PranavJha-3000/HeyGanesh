using System.Net;
using System.Net.Sockets;
using System.Text;

namespace HeyGanesh.Tools.TutorSmoke;

/// <summary>
/// A strictly loopback-only HTTP stub the payload row constructs its client against, by pointing
/// <c>DeepSeekTutorOptions.BaseUrl</c> at it. It exists because there is no InternalsVisibleTo and
/// no handler seam on the public factory — the SAME public construction path the App Shell will use
/// must be the one under test, so the substitution happens at the HTTP layer, below the client.
/// </summary>
/// <remarks>
/// <para>
/// <b>Port selection.</b> <see cref="HttpListener"/> cannot bind an ephemeral <c>:0</c> port, so the
/// stub probes for a free loopback TCP port (a throwaway <see cref="TcpListener"/> on port 0 tells
/// us one is available), starts the listener on that concrete prefix, and on any bind failure (the
/// port was taken in the probe-to-bind race, or the prefix was refused) disposes and retries with a
/// fresh candidate, bounded. Loopback <c>http://127.0.0.1:&lt;port&gt;/</c> prefixes need no admin
/// rights or URLACL entries — and no wildcard prefix is ever attempted, so the stub can never be
/// reachable from outside the machine.
/// </para>
/// <para>
/// Canned responses are dequeued in request order; the last one repeats, so a row that triggers more
/// calls than it planned still gets a deterministic answer instead of a hang.
/// </para>
/// </remarks>
internal sealed class StubServer : IDisposable
{
    private const int MaxBindAttempts = 20;

    private readonly HttpListener _listener = new();
    private readonly List<(int Status, string Body, TimeSpan Delay)> _responses = [];
    private readonly object _gate = new();
    private CancellationTokenSource? _cts;
    private Task? _loop;

    /// <summary>Requests served so far — doubles as the cursor into the canned-response list, so
    /// the response for request N is the Nth enqueued (the last entry repeats once exhausted).</summary>
    private int _served;

    /// <summary>The concrete loopback prefix that successfully bound, e.g. http://127.0.0.1:54321/.</summary>
    internal string Prefix { get; private set; } = string.Empty;

    /// <summary>Every request the stub served, in order, body captured verbatim.</summary>
    internal List<CapturedRequest> Requests { get; } = [];

    internal sealed record CapturedRequest(string Method, string Path, string? Authorization, string Body);

    /// <summary>Binds with bounded retry; throws (row failure, never a skip) if no port can be won.</summary>
    internal static StubServer Start()
    {
        for (var attempt = 1; attempt <= MaxBindAttempts; attempt++)
        {
            var port = PickFreePort();
            var stub = new StubServer();
            try
            {
                stub._listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                stub._listener.Start();
                stub.Prefix = $"http://127.0.0.1:{port}/";
                stub._cts = new CancellationTokenSource();
                stub._loop = Task.Run(stub.AcceptLoopAsync);
                return stub;
            }
            catch (Exception)
            {
                // Port lost the race or the prefix was refused: dispose and try another one.
                stub.Dispose();
            }
        }

        throw new InvalidOperationException($"could not bind a loopback stub port in {MaxBindAttempts} attempts");
    }

    /// <summary>Asks the OS for a free port by binding one on port 0 and immediately releasing it.</summary>
    private static int PickFreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    internal void Enqueue(int status, string body, TimeSpan? delay = null)
    {
        lock (_gate)
        {
            _responses.Add((status, body, delay ?? TimeSpan.Zero));
        }
    }

    /// <summary>Canned DeepSeek-shaped 200 completion with the given content (usage + model echo included).</summary>
    internal void EnqueueCompletion(string content, string model = "deepseek-flash", TimeSpan? delay = null)
    {
        var payload = new System.Text.Json.Nodes.JsonObject
        {
            ["choices"] = new System.Text.Json.Nodes.JsonArray
            {
                new System.Text.Json.Nodes.JsonObject
                {
                    ["message"] = new System.Text.Json.Nodes.JsonObject { ["content"] = content },
                    ["finish_reason"] = "stop",
                },
            },
            ["model"] = model,
            ["usage"] = new System.Text.Json.Nodes.JsonObject { ["prompt_tokens"] = 123, ["completion_tokens"] = 45 },
        };

        Enqueue(200, payload.ToJsonString(), delay);
    }

    /// <summary>
    /// Serves responses in enqueue order: request N gets the Nth enqueued response (the LAST entry
    /// repeats once the list is exhausted, so a row that triggers more calls than it planned still
    /// gets a deterministic answer instead of a hang). Rows keep alignment by enqueueing exactly
    /// one response per request they drive. A request arriving while an earlier delayed response is
    /// still pending (the cancellation sub-row) already sees the NEXT scenario, because the take
    /// happens before the delay, not after it.
    /// </summary>
    private (int Status, string Body, TimeSpan Delay) TakeResponse()
    {
        lock (_gate)
        {
            if (_responses.Count == 0)
            {
                return (500, "{\"error\":\"stub ran out of canned responses\"}", TimeSpan.Zero);
            }

            var index = Math.Min(_served, _responses.Count - 1);
            _served++;
            return _responses[index];
        }
    }

    private async Task AcceptLoopAsync()
    {
        var ct = _cts!.Token;
        try
        {
            while (!ct.IsCancellationRequested && _listener.IsListening)
            {
                var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(() => ServeAsync(ctx)); // keeps the accept pump free
            }
        }
        catch (Exception)
        {
            // Stop()/Dispose() during shutdown surfaces here as a closed listener. Expected.
        }
    }

    private async Task ServeAsync(HttpListenerContext ctx)
    {
        try
        {
            string body;
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
                body = await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            lock (_gate)
            {
                Requests.Add(new CapturedRequest(
                    ctx.Request.HttpMethod,
                    ctx.Request.Url?.AbsolutePath ?? string.Empty,
                    ctx.Request.Headers["Authorization"],
                    body));
            }

            var (status, responseBody, delay) = TakeResponse();

            if (delay > TimeSpan.Zero)
            {
                await Task.Delay(delay).ConfigureAwait(false);
            }

            var bytes = Encoding.UTF8.GetBytes(responseBody);
            ctx.Response.StatusCode = status;
            ctx.Response.ContentType = "application/json";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A cancelled or aborted request mid-write is the row ending; nothing to report.
        }
        finally
        {
            try
            {
                ctx.Response.Close();
            }
            catch
            {
                // Client gave up first (cancellation sub-row). Also expected.
            }
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        try
        {
            _listener.Stop();
        }
        catch
        {
            // Already stopped.
        }

        _listener.Close();
        try
        {
            _loop?.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Cancellation during the wait is fine; the row is done either way.
        }

        _cts?.Dispose();
    }
}