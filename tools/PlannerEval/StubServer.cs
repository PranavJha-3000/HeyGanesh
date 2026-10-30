using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json.Nodes;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// A loopback-only stand-in for an OpenAI-compatible chat endpoint that streams server-sent events, for PE-4
/// (DEC-044). The real <c>OpenAiCompatLlmClient</c> is pointed at it, so the recording path under test is the
/// one a live run takes, down to the HTTP layer. Modelled on TutorSmoke's StubServer: it binds
/// <c>http://127.0.0.1:&lt;port&gt;/</c> only, never a wildcard, so it is unreachable from outside the machine.
/// </summary>
/// <remarks>
/// Answers are served in request order; the last repeats, so an unexpected extra call gets a deterministic
/// answer instead of a hang. Each answer is sent in small <c>data:</c> chunks and ends with
/// <c>data: [DONE]</c>, with no usage block (DeepSeek sends none unless asked), so token counts read
/// "not reported", as they will live.
/// </remarks>
internal sealed class StubServer : IDisposable
{
    private const int MaxBindAttempts = 20;
    private const int ChunkChars = 40;

    private readonly HttpListener _listener = new();
    private readonly List<string> _answers = new();
    private readonly object _gate = new();
    private int _served;
    private Task? _loop;

    public string Prefix { get; private set; } = string.Empty;

    /// <summary>The Authorization header and path of every request served, in order.</summary>
    public List<(string Path, string? Authorization)> Requests { get; } = new();

    public static StubServer Start()
    {
        for (var attempt = 1; attempt <= MaxBindAttempts; attempt++)
        {
            var probe = new TcpListener(IPAddress.Loopback, 0);
            probe.Start();
            var port = ((IPEndPoint)probe.LocalEndpoint).Port;
            probe.Stop();

            var stub = new StubServer();
            try
            {
                stub._listener.Prefixes.Add($"http://127.0.0.1:{port}/");
                stub._listener.Start();
                stub.Prefix = $"http://127.0.0.1:{port}";
                stub._loop = Task.Run(stub.AcceptLoopAsync);
                return stub;
            }
            catch (Exception)
            {
                // The port lost the probe-to-bind race, or the prefix was refused: try another one.
                stub.Dispose();
            }
        }

        throw new InvalidOperationException($"could not bind a loopback stub port in {MaxBindAttempts} attempts");
    }

    public void Enqueue(string answer)
    {
        lock (_gate)
        {
            _answers.Add(answer);
        }
    }

    private async Task AcceptLoopAsync()
    {
        try
        {
            while (_listener.IsListening)
            {
                var ctx = await _listener.GetContextAsync().ConfigureAwait(false);
                await ServeAsync(ctx).ConfigureAwait(false);
            }
        }
        catch (Exception)
        {
            // Dispose closes the listener; that surfaces here and is the normal end.
        }
    }

    private async Task ServeAsync(HttpListenerContext ctx)
    {
        try
        {
            using (var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8))
            {
                await reader.ReadToEndAsync().ConfigureAwait(false);
            }

            string answer;
            lock (_gate)
            {
                Requests.Add((ctx.Request.Url?.AbsolutePath ?? string.Empty, ctx.Request.Headers["Authorization"]));
                answer = _answers.Count == 0 ? string.Empty : _answers[Math.Min(_served, _answers.Count - 1)];
                _served++;
            }

            var sse = new StringBuilder();
            for (var at = 0; at < answer.Length; at += ChunkChars)
            {
                var piece = answer.Substring(at, Math.Min(ChunkChars, answer.Length - at));
                var chunk = new JsonObject { ["choices"] = new JsonArray { new JsonObject { ["delta"] = new JsonObject { ["content"] = piece } } } };
                sse.Append("data: ").Append(chunk.ToJsonString()).Append("\n\n");
            }

            sse.Append("data: [DONE]\n\n");
            var bytes = Encoding.UTF8.GetBytes(sse.ToString());
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "text/event-stream";
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
        }
        catch (Exception)
        {
            // A client that gave up mid-write ends the request; nothing to report.
        }
        finally
        {
            try
            {
                ctx.Response.Close();
            }
            catch
            {
                // Already closed by the client.
            }
        }
    }

    public void Dispose()
    {
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch
        {
            // Never started (a lost bind race).
        }
    }
}
