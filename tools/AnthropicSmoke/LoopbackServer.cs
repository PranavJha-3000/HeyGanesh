namespace HeyGanesh.Tools.AnthropicSmoke;

using System.Net;
using System.Text;

internal sealed class LoopbackServer : IDisposable
{
    private readonly HttpListener _listener;
    private readonly Task _loop;
    private readonly CancellationTokenSource _cts = new();
    private readonly List<QueuedResponse> _queue = new();
    private readonly object _lock = new();
    private RecordedRequest? _lastRequest;
    private int _delayMs;

    public string BaseUrl { get; }

    public RecordedRequest LastRequest
    {
        get { lock (_lock) { return _lastRequest!; } }
    }

    public LoopbackServer()
    {
        _listener = new HttpListener();
        var port = GetFreePort();
        _listener.Prefixes.Add($"http://127.0.0.1:{port}/");
        _listener.Start();
        BaseUrl = $"http://127.0.0.1:{port}";
        _loop = Task.Run(AcceptLoop);
    }

    private static int GetFreePort()
    {
        var l = new System.Net.Sockets.TcpListener(System.Net.IPAddress.Loopback, 0);
        l.Start();
        var port = ((System.Net.IPEndPoint)l.LocalEndpoint).Port;
        l.Stop();
        return port;
    }

    private async Task AcceptLoop()
    {
        while (!_cts.IsCancellationRequested)
        {
            HttpListenerContext? ctx = null;
            try { ctx = await _listener.GetContextAsync().ConfigureAwait(false); }
            catch (HttpListenerException) { break; }
            catch (ObjectDisposedException) { break; }
            if (ctx is null) continue;
            _ = Task.Run(() => HandleContext(ctx));
        }
    }

    private async Task HandleContext(HttpListenerContext ctx)
    {
        string body;
        using (var reader = new StreamReader(ctx.Request.InputStream, ctx.Request.ContentEncoding ?? Encoding.UTF8))
            body = await reader.ReadToEndAsync().ConfigureAwait(false);

        var headers = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string? key in ctx.Request.Headers.AllKeys)
        {
            if (key is not null)
                headers[key] = ctx.Request.Headers[key] ?? string.Empty;
        }

        var recorded = new RecordedRequest
        {
            Method = ctx.Request.HttpMethod,
            Path = ctx.Request.Url?.AbsolutePath ?? "/",
            Body = body,
            Headers = headers,
        };
        lock (_lock) { _lastRequest = recorded; }

        QueuedResponse? qr;
        lock (_lock)
        {
            if (_queue.Count > 0) { qr = _queue[0]; _queue.RemoveAt(0); }
            else { qr = new QueuedResponse { StatusCode = 404, ContentType = "text/plain", Body = "Not Found" }; }
            _delayMs = qr.DelayMs;
        }

        if (_delayMs > 0) await Task.Delay(_delayMs).ConfigureAwait(false);

        ctx.Response.StatusCode = qr.StatusCode;
        ctx.Response.ContentType = qr.ContentType;
        if (qr.IsSse)
        {
            ctx.Response.ContentType = "text/event-stream";
            using var writer = new StreamWriter(ctx.Response.OutputStream, Encoding.UTF8, leaveOpen: true);
            foreach (var line in qr.SseLines)
            {
                await writer.WriteLineAsync(line).ConfigureAwait(false);
                await writer.FlushAsync().ConfigureAwait(false);
            }
            ctx.Response.Close();
        }
        else
        {
            var bytes = Encoding.UTF8.GetBytes(qr.Body);
            ctx.Response.ContentLength64 = bytes.Length;
            await ctx.Response.OutputStream.WriteAsync(bytes).ConfigureAwait(false);
            ctx.Response.Close();
        }
    }

    public void EnqueueResponse(int statusCode, string contentType, string body)
    {
        lock (_lock) { _queue.Add(new QueuedResponse { StatusCode = statusCode, ContentType = contentType, Body = body }); }
    }

    public void EnqueueSseResponse(string[] lines, int delayMs = 0)
    {
        lock (_lock) { _queue.Add(new QueuedResponse { StatusCode = 200, ContentType = "text/event-stream", IsSse = true, SseLines = lines, DelayMs = delayMs }); }
    }

    public void Reset()
    {
        lock (_lock) { _queue.Clear(); _lastRequest = null; }
    }

    public void Dispose()
    {
        _cts.Cancel();
        _listener.Stop();
        _listener.Close();
    }
}

internal sealed class QueuedResponse
{
    public int StatusCode { get; set; }
    public string ContentType { get; set; } = "text/plain";
    public string Body { get; set; } = string.Empty;
    public bool IsSse { get; set; }
    public string[] SseLines { get; set; } = Array.Empty<string>();
    public int DelayMs { get; set; }
}

internal sealed class RecordedRequest
{
    public string Method { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Body { get; set; } = string.Empty;
    public Dictionary<string, string> Headers { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}