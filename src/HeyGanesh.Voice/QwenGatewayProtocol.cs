using System.Text.Json;

namespace HeyGanesh.Voice;

/// <summary>What a single Gateway frame means to HeyGanesh.</summary>
/// <remarks>
/// This enum is the entire policy surface of the speech-in leg: exactly one kind can become the user's
/// text (<see cref="UserFinal"/>), and every assistant-side kind exists so it can be named and counted
/// while being discarded. Nothing else in the module is allowed to decide what counts as a question.
/// </remarks>
public enum GatewayEventKind
{
    /// <summary>A frame this module does not recognise. Ignored and counted, never guessed at.</summary>
    Unknown = 0,

    /// <summary>Connection/voice lifecycle bookkeeping (<c>gateway.*</c>, <c>voice.connection|ready|ownership|deactivated|sleep</c>).</summary>
    SessionLifecycle,

    /// <summary>An application heartbeat from the Gateway; answered with <c>session.pong</c>.</summary>
    SessionPing,

    /// <summary>Interim recognised USER speech. Diagnostics only.</summary>
    UserPartial,

    /// <summary>A committed USER transcript — the one and only source of the question.</summary>
    UserFinal,

    /// <summary>The Gateway withdrew a transcript (either role).</summary>
    TranscriptDiscard,

    /// <summary>Interim assistant text. Never spoken, never shown, never routed.</summary>
    AssistantPartial,

    /// <summary>A committed assistant answer. Never spoken, never shown, never routed (DEC-013).</summary>
    AssistantFinal,

    /// <summary>The frontend started generating a response — the signal to cancel it.</summary>
    AssistantResponseStarted,

    /// <summary>The frontend's response was interrupted (by us or by the service).</summary>
    AssistantResponseInterrupted,

    /// <summary>Assistant speech audio. Never played.</summary>
    AssistantAudio,

    /// <summary>End of an assistant audio stream.</summary>
    AssistantAudioDone,

    /// <summary>The Gateway wants local playback stopped; we acknowledge and play nothing.</summary>
    PlaybackClear,

    /// <summary>A model-facing tool call (frontend or backend surface). Ignored and counted.</summary>
    ToolCall,

    /// <summary>A Backend Agent task event. With <c>AGENT_PROTOCOL=none</c> this must never happen — it aborts the listen.</summary>
    TaskEvent,

    /// <summary>Capture suspension commanded by the Gateway or an external host.</summary>
    InputSuspend,

    /// <summary>Capture may resume.</summary>
    InputResume,

    /// <summary>Frontend-memory invalidation. Nothing for this module to do.</summary>
    MemoryChanged,

    /// <summary>Foreground activity hint from the Gateway.</summary>
    AgentActivity,

    /// <summary>Gateway-side client presentation state.</summary>
    StateChanged,

    /// <summary>A structured Gateway error.</summary>
    Error,
}

/// <summary>
/// One frame, classified. Every field is copied out of the JSON, so a decision never keeps a
/// <see cref="JsonDocument"/> alive and can be replayed in verification without a live Gateway.
/// </summary>
public sealed record GatewayEventDecision
{
    /// <summary>What the frame means.</summary>
    public required GatewayEventKind Kind { get; init; }

    /// <summary>The raw <c>type</c> string, kept verbatim for logs and assertions.</summary>
    public required string EventType { get; init; }

    /// <summary><c>user</c> or <c>assistant</c> for transcript events; null otherwise.</summary>
    public string? Role { get; init; }

    /// <summary>Transcript text (<c>content</c>), verbatim.</summary>
    public string? Text { get; init; }

    /// <summary>Voice presentation state, for <c>voice.state</c>.</summary>
    public string? State { get; init; }

    /// <summary>Error text, for <c>error</c>.</summary>
    public string? ErrorMessage { get; init; }

    /// <summary>Tool name, for <c>tool.call</c>.</summary>
    public string? ToolName { get; init; }

    /// <summary><c>frontend</c> or <c>backend</c>, for <c>tool.call</c>.</summary>
    public string? ToolSurface { get; init; }

    /// <summary>The task id, for a <c>task.*</c> event. Present only when a Backend Agent is running.</summary>
    public string? TaskId { get; init; }

    /// <summary>
    /// A version string the frame carried (used for <c>session.ready</c>), so the handshake can check that
    /// the Gateway and this client are speaking the same wire version instead of assuming it.
    /// </summary>
    public string? Version { get; init; }

    /// <summary>Sample rate advertised by <c>audio.delta</c>.</summary>
    public int? AudioSampleRate { get; init; }

    /// <summary>True when the frame carried assistant audio bytes we must never play.</summary>
    public bool CarriesAudio { get; init; }

    /// <summary>Convenience: this decision is the user's own speech, and the only thing that can become a question.</summary>
    public bool IsUserSpeech => Kind is GatewayEventKind.UserFinal or GatewayEventKind.UserPartial;

    public override string ToString() =>
        Kind == GatewayEventKind.Unknown
            ? $"GatewayEventDecision: unknown type '{EventType}'"
            : $"GatewayEventDecision: {EventType} -> {Kind}" +
              (Role is null ? string.Empty : $" role={Role}") +
              (CarriesAudio ? " (audio discarded)" : string.Empty);
}
