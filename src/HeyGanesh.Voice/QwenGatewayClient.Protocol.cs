using System.Text.Json;

namespace HeyGanesh.Voice;

/// <summary>
/// Client→Gateway message names this module emits, verbatim from the qwen-audio-agent Gateway Client
/// Protocol (wire 7.0 — <c>shared/protocol/gateway-client-protocol.mjs</c> and
/// <c>shared/protocol/realtime-events.mjs</c>). Nothing here is invented: these are the published names
/// a GCP client is allowed to send.
/// </summary>
/// <remarks>
/// <see cref="QwenGatewayClient"/> sends these and only these. Verification asserts the set never grows
/// by accident, because every extra message name is a protocol claim HeyGanesh would be making against a
/// runtime it does not own.
/// </remarks>
public static class GatewayClientMessage
{
    public const string SessionHello = "session.hello";
    public const string SessionPong = "session.pong";
    public const string InputAudioAppend = "input_audio_buffer.append";
    public const string ResponseCancel = "response.cancel";
    public const string Interrupt = "interrupt";
    public const string PlaybackCancelled = "playback.cancelled";
    public const string InputSuspendAck = "input.suspend.ack";

    /// <summary>The complete allowlist of message types this module may send.</summary>
    public static readonly IReadOnlySet<string> Allowed = new HashSet<string>(StringComparer.Ordinal)
    {
        SessionHello, SessionPong, InputAudioAppend, ResponseCancel, Interrupt,
        PlaybackCancelled, InputSuspendAck,
    };
}

/// <summary>
/// Gateway→client event names this module reacts to, verbatim from the same protocol
/// (<c>shared/protocol/realtime-events.mjs</c>: <c>GatewayServerEvent</c> and <c>GatewayTaskEvent</c>).
/// </summary>
public static class GatewayServerEvent
{
    public const string SessionReady = "session.ready";
    public const string SessionPing = "session.ping";
    public const string GatewayConnected = "gateway.connected";
    public const string GatewayDisconnected = "gateway.disconnected";
    public const string VoiceConnection = "voice.connection";
    public const string VoiceReady = "voice.ready";
    public const string VoiceState = "voice.state";
    public const string VoiceOwnership = "voice.ownership";
    public const string VoiceDeactivated = "voice.deactivated";
    public const string VoiceSleep = "voice.sleep";
    public const string TurnStarted = "turn.started";
    public const string ResponseStarted = "response.started";
    public const string ResponseInterrupted = "response.interrupted";
    public const string AudioDelta = "audio.delta";
    public const string AudioDone = "audio.done";
    public const string PlaybackClear = "playback.clear";
    public const string TranscriptDelta = "transcript.delta";
    public const string TranscriptFinal = "transcript.final";
    public const string TranscriptDiscard = "transcript.discard";
    public const string ToolCall = "tool.call";
    public const string InputSuspend = "input.suspend";
    public const string InputResume = "input.resume";
    public const string MemoryChanged = "memory.changed";
    public const string AgentActivity = "agent.activity";
    public const string ClientState = "client.state";
    public const string Error = "error";

    /// <summary>
    /// The events a correct listen depends on. A Gateway that does not emit these cannot serve this
    /// module, so verification pins the list rather than discovering the gap in the field.
    /// </summary>
    public static readonly IReadOnlyList<string> Required =
        [SessionReady, TranscriptDelta, TranscriptFinal, TranscriptDiscard, ResponseStarted, AudioDelta, Error];

    internal const string TaskEventPrefix = "task.";

    /// <summary>The two roles a transcript event may carry.</summary>
    internal const string RoleUser = "user";
    internal const string RoleAssistant = "assistant";
}
