using System.Net.WebSockets;
using System.Text;
using System.Threading.Channels;

namespace HeyGanesh.Voice;

/// <summary>
/// Speech in, through a qwen-audio-agent Gateway's Gateway Client Protocol (wire 7.0).
/// </summary>
/// <remarks>
/// <para>
/// <b>What the Gateway is here.</b> An external realtime voice runtime, configured and run by the operator
/// with its Backend Agent disabled (<c>AGENT_PROTOCOL=none</c>). This client connects to it as a
/// Conversation Client, streams the microphone, and reads the user's transcript events.
/// </para>
/// <para>
/// <b>What it cannot do, and what HeyGanesh does about it.</b> The Gateway's frontend is a conversational
/// model: it transcribes the user AND answers in the same turn. No verified protocol switch stops it
/// generating — not <c>output_enabled</c>, not <c>text_only</c>, not sleep/mute/suspend, and the
/// context-only delivery modes apply to typed client events, not to live audio (DEC-013 records the whole
/// investigation). So this client neutralises rather than prevents: on <c>response.started</c> it sends
/// <c>response.cancel</c> and <c>interrupt</c>, it never plays <c>audio.delta</c>, it drops every
/// <c>transcript.*</c> carrying <c>role: assistant</c>, and it counts all of that in
/// <see cref="GatewaySuppressionLedger"/>. The user only ever hears HeyGanesh.
/// </para>
/// <para>
/// <b>Transport per utterance.</b> The socket is opened for one listen and closed when that listen ends.
/// GCP grants one active client per owner, so holding a socket open would keep the operator's own TUI or
/// WebUI client evicted between questions; a bounded press-to-talk window keeps that takeover short.
/// </para>
/// <para>
/// <b>Threading.</b> One listen at a time — enforced by <see cref="VoiceService"/> and re-checked here. The
/// receive pump runs on its own task and hands decisions over a channel; the microphone pump does the same
/// with frames, so neither the socket nor the audio thread ever blocks on the other.
/// </para>
/// </remarks>
public sealed partial class QwenGatewayClient : IVoiceListener
{
    private readonly QwenGatewayOptions _gateway;
    private readonly VoiceOptions _options;
    private readonly IMicrophoneSource _microphone;
    private readonly bool _ownsMicrophone;
    private readonly GatewaySuppressionLedger _ledger = new();
    private readonly HttpClient _http;
    private readonly SemaphoreSlim _sendLock = new(1, 1);
    private readonly object _gate = new();

    private ClientWebSocket? _socket;
    private Channel<GatewayEventDecision>? _inbox;
    private Task? _receivePump;
    private string? _transportFault;
    private int _consecutiveFailures;
    private int _captureSuspended;
    private bool _disposed;

    /// <summary>
    /// Creates the client. With no arguments this is the real thing: a winmm microphone at the configured
    /// rate, talking to the endpoint from <see cref="QwenGatewayOptions.GatewayUrlEnvVar"/>. Every seam is
    /// injectable so the listen loop can be exercised without a capture device or a Gateway.
    /// </summary>
    public QwenGatewayClient(
        QwenGatewayOptions? gatewayOptions = null,
        VoiceOptions? voiceOptions = null,
        IMicrophoneSource? microphone = null)
    {
        _gateway = gatewayOptions ?? QwenGatewayOptions.Default;
        _options = voiceOptions ?? VoiceOptions.Default;
        _options.Validate();

        if (microphone is null)
        {
            _microphone = new WinMmMicrophoneSource(_gateway.InputSampleRate);
            _ownsMicrophone = true;
        }
        else
        {
            _microphone = microphone;
        }

        _http = new HttpClient { Timeout = _options.ConnectTimeout };
    }

    /// <summary>Interim recognised user speech. Raised from the receive pump; never for assistant text.</summary>
    public event EventHandler<VoicePartialEventArgs>? Partial;

    /// <summary>The suppression counters for this client's lifetime — the evidence behind DEC-013's limitation.</summary>
    public GatewaySuppressionLedger Ledger => _ledger;

    /// <summary>Everything the frontend generated that HeyGanesh discarded, across all listens.</summary>
    public long SuppressedAssistantEvents => _ledger.SuppressedAssistantEvents;

    /// <summary>The Gateway endpoint in loggable form (scheme, host, port). Null until the first connect.</summary>
    public string? GatewayEndpoint { get; private set; }

    /// <summary>The health-contract version the Gateway reported, when it reported one.</summary>
    public string? GatewayProtocolVersion { get; private set; }

    /// <summary>
    /// What the Gateway said about its Backend Agent: <c>none</c>, the protocol it named, or an explicit
    /// "unknown" when the health payload said nothing. Unknown is not treated as safe by itself — the
    /// <c>task.*</c> guard is what makes the guarantee hold.
    /// </summary>
    public string BackendState { get; private set; } = "not checked yet";

    /// <summary>Consecutive failed listens. At or above <see cref="VoiceOptions.MaxConsecutiveFailures"/> further listens are refused.</summary>
    public int ConsecutiveFailures
    {
        get { lock (_gate) return _consecutiveFailures; }
    }

    /// <summary>Whether the transport is currently open.</summary>
    public bool IsConnected => _socket?.State == WebSocketState.Open;

    /// <summary>
    /// Sends one already-built protocol message. Serialised with a lock because the microphone pump and the
    /// listen loop both send, and a WebSocket does not allow two concurrent sends.
    /// </summary>
    private async Task SendAsync(string json, CancellationToken cancellationToken)
    {
        var socket = _socket;
        if (socket is null || socket.State != WebSocketState.Open)
        {
            throw new VoiceClientException("The Gateway connection is not open, so there is nothing to send to.");
        }

        var payload = Encoding.UTF8.GetBytes(json);
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await socket.SendAsync(payload, WebSocketMessageType.Text, endOfMessage: true, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (WebSocketException ex)
        {
            throw new VoiceClientException($"A message could not be sent to the Gateway: {ex.Message}");
        }
        finally
        {
            _sendLock.Release();
        }
    }

    /// <summary>
    /// Records why the transport died and closes the inbox, so whichever loop is waiting wakes up and
    /// reports it. A fault is never swallowed: a listen that ended because the socket broke must read as a
    /// failure, not as a quiet user.
    /// </summary>
    private void FailTransport(string message)
    {
        lock (_gate)
        {
            _transportFault ??= message;
        }

        _inbox?.Writer.TryComplete();
    }

    /// <summary>The receive pump: one frame in, one decision out. Never throws at its caller.</summary>
    private async Task ReceivePumpAsync(
        ClientWebSocket socket,
        ChannelWriter<GatewayEventDecision> writer,
        CancellationToken cancellationToken)
    {
        var buffer = new byte[16 * 1024];
        var accumulated = new MemoryStream();

        try
        {
            while (!cancellationToken.IsCancellationRequested && socket.State == WebSocketState.Open)
            {
                var received = await socket.ReceiveAsync(buffer, cancellationToken).ConfigureAwait(false);

                if (received.MessageType == WebSocketMessageType.Close)
                {
                    FailTransport("The Gateway closed the connection.");
                    return;
                }

                accumulated.Write(buffer, 0, received.Count);
                if (!received.EndOfMessage)
                {
                    continue;
                }

                var frame = Encoding.UTF8.GetString(accumulated.GetBuffer(), 0, (int)accumulated.Length);
                accumulated.SetLength(0);

                // Classify and count in one step, so what the loop reacts to and what the counters report
                // can never disagree.
                writer.TryWrite(_ledger.Observe(frame));
            }
        }
        catch (OperationCanceledException)
        {
            // Cancellation is the caller's own doing: a released chord, Ctrl+C, or Dispose.
        }
        catch (WebSocketException ex)
        {
            FailTransport($"The Gateway connection failed mid-stream: {ex.Message}");
        }
        catch (ObjectDisposedException)
        {
            FailTransport("The Gateway connection was disposed while a read was in flight.");
        }
        finally
        {
            writer.TryComplete();
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;
        }

        _inbox?.Writer.TryComplete();
        _socket?.Dispose();
        _socket = null;

        if (_ownsMicrophone)
        {
            _microphone.Dispose();
        }

        _http.Dispose();
        _sendLock.Dispose();
        _inbox = null;
    }
}
