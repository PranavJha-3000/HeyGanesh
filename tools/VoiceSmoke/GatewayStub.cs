using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using HeyGanesh.Voice;

namespace HeyGanesh.Tools.VoiceSmoke;

/// <summary>
/// A loopback stand-in for a qwen-audio-agent Gateway, just enough of wire 7.0 for VO-11 (DEC-040):
/// answers <c>session.hello</c> with <c>session.ready</c> after <see cref="ReadyDelay"/>, records every
/// <c>input_audio_buffer.append</c> frame's number, and sends one committed user transcript once
/// <see cref="FramesBeforeFinal"/> frames have arrived. Loopback only, through .NET's own HttpListener;
/// no NuGet, no network, no microphone.
/// </summary>
internal sealed class GatewayStub : IAsyncDisposable
{
    public const string FinalText = "where is the Save button";

    private readonly HttpListener _listener = new();
    private readonly CancellationTokenSource _stop = new();
    private readonly List<int> _frames = new();
    private Task? _loop;

    public GatewayStub(TimeSpan readyDelay, int framesBeforeFinal)
    {
        ReadyDelay = readyDelay;
        FramesBeforeFinal = framesBeforeFinal;
        Port = FreePort();
        _listener.Prefixes.Add($"http://localhost:{Port}/");
    }

    public int Port { get; }

    public TimeSpan ReadyDelay { get; }

    public int FramesBeforeFinal { get; }

    /// <summary>The URL a client should be given.</summary>
    public string Url => $"ws://localhost:{Port}/api/realtime";

    /// <summary>Frame numbers in the order they arrived.</summary>
    public IReadOnlyList<int> Frames
    {
        get { lock (_frames) return _frames.ToArray(); }
    }

    /// <summary>Starts listening. Throws HttpListenerException when the OS refuses the prefix.</summary>
    public void Start()
    {
        _listener.Start();
        _loop = AcceptLoopAsync();
    }

    /// <summary>A loopback port nothing is listening on (also used for the "connection refused" case).</summary>
    public static int FreePort()
    {
        var probe = new TcpListener(IPAddress.Loopback, 0);
        probe.Start();
        var port = ((IPEndPoint)probe.LocalEndpoint).Port;
        probe.Stop();
        return port;
    }

    private async Task AcceptLoopAsync()
    {
        while (!_stop.IsCancellationRequested)
        {
            HttpListenerContext context;
            try
            {
                context = await _listener.GetContextAsync().ConfigureAwait(false);
            }
            catch (Exception) when (_stop.IsCancellationRequested)
            {
                return;
            }
            catch (HttpListenerException)
            {
                return;
            }

            _ = context.Request.IsWebSocketRequest ? ServeSocketAsync(context) : ServeHealth(context);
        }
    }

    private static Task ServeHealth(HttpListenerContext context)
    {
        var body = Encoding.UTF8.GetBytes("{}");
        context.Response.ContentType = "application/json";
        context.Response.OutputStream.Write(body);
        context.Response.Close();
        return Task.CompletedTask;
    }

    private async Task ServeSocketAsync(HttpListenerContext context)
    {
        WebSocket socket;
        try
        {
            socket = (await context.AcceptWebSocketAsync(subProtocol: null).ConfigureAwait(false)).WebSocket;
        }
        catch (Exception)
        {
            return;
        }

        var buffer = new byte[64 * 1024];
        var finalSent = false;
        try
        {
            while (socket.State == WebSocketState.Open && !_stop.IsCancellationRequested)
            {
                using var message = new MemoryStream();
                WebSocketReceiveResult result;
                do
                {
                    result = await socket.ReceiveAsync(buffer, _stop.Token).ConfigureAwait(false);
                    if (result.MessageType == WebSocketMessageType.Close)
                    {
                        await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, "bye", CancellationToken.None).ConfigureAwait(false);
                        return;
                    }

                    message.Write(buffer, 0, result.Count);
                }
                while (!result.EndOfMessage);

                using var json = JsonDocument.Parse(message.ToArray());
                var type = json.RootElement.TryGetProperty("type", out var t) ? t.GetString() : null;
                if (type == GatewayClientMessage.SessionHello)
                {
                    // The delay stands in for the Gateway opening its upstream realtime session: the window
                    // in which, before DEC-040, the user's first words were never recorded.
                    await Task.Delay(ReadyDelay, _stop.Token).ConfigureAwait(false);
                    await SendAsync(socket, $"{{\"type\":\"{GatewayServerEvent.SessionReady}\",\"protocolVersion\":\"{QwenGatewayProtocol.WireVersion}\"}}").ConfigureAwait(false);
                }
                else if (type == GatewayClientMessage.InputAudioAppend)
                {
                    var pcm = Convert.FromBase64String(json.RootElement.GetProperty("audio").GetString()!);
                    int count;
                    lock (_frames)
                    {
                        _frames.Add(BitConverter.ToInt32(pcm, 0));
                        count = _frames.Count;
                    }

                    if (!finalSent && count >= FramesBeforeFinal)
                    {
                        finalSent = true;
                        await SendAsync(socket, $"{{\"type\":\"{GatewayServerEvent.TranscriptFinal}\",\"role\":\"user\",\"content\":\"{FinalText}\"}}").ConfigureAwait(false);
                    }
                }
            }
        }
        catch (Exception) when (_stop.IsCancellationRequested || socket.State != WebSocketState.Open)
        {
            // The client closed or the stub is stopping.
        }
        finally
        {
            socket.Dispose();
        }
    }

    private static Task SendAsync(WebSocket socket, string json) =>
        socket.SendAsync(Encoding.UTF8.GetBytes(json), WebSocketMessageType.Text, endOfMessage: true, CancellationToken.None);

    public async ValueTask DisposeAsync()
    {
        _stop.Cancel();
        try
        {
            _listener.Stop();
            _listener.Close();
        }
        catch (ObjectDisposedException)
        {
        }

        if (_loop is not null)
        {
            try
            {
                await _loop.WaitAsync(TimeSpan.FromSeconds(2)).ConfigureAwait(false);
            }
            catch (Exception)
            {
            }
        }

        _stop.Dispose();
    }
}

/// <summary>
/// A microphone whose frames are numbered by wall-clock time since the press: frame n covers
/// n×100 ms..(n+1)×100 ms after <c>pressedAt</c>. A client that starts the microphone late therefore
/// shows it — its first frame number is the time it lost — which is the bug VO-11 proves and DEC-040 fixes.
/// </summary>
internal sealed class NumberedMicrophone : IMicrophoneSource
{
    private readonly DateTimeOffset _pressedAt;
    private readonly object _gate = new();
    private Timer? _timer;
    private int _next;

    public NumberedMicrophone(DateTimeOffset pressedAt) => _pressedAt = pressedAt;

    public int SampleRate => 16000;

    public int Starts { get; private set; }

    public int Stops { get; private set; }

    public event EventHandler<MicrophoneFrameEventArgs>? Frame;

    public void Start()
    {
        lock (_gate)
        {
            Starts++;
            _next = (int)((DateTimeOffset.UtcNow - _pressedAt).TotalMilliseconds / 100);
            _timer = new Timer(_ => Emit(), null, TimeSpan.FromMilliseconds(100), TimeSpan.FromMilliseconds(100));
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            Stops++;
            _timer?.Dispose();
            _timer = null;
        }
    }

    private void Emit()
    {
        byte[] pcm;
        lock (_gate)
        {
            if (_timer is null)
            {
                return;
            }

            pcm = new byte[3200]; // 100 ms of 16 kHz mono 16-bit
            BitConverter.GetBytes(_next++).CopyTo(pcm, 0);
        }

        Frame?.Invoke(this, new MicrophoneFrameEventArgs(pcm));
    }

    public void Dispose() => Stop();
}
