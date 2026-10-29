using System.Text.Json;

namespace HeyGanesh.Voice;

/// <summary>
/// The Gateway Client Protocol as far as HeyGanesh needs it: classify an inbound frame, and build the
/// handful of outbound messages this module sends.
/// </summary>
/// <remarks>
/// Pure and dependency-free on purpose. It is the one part of the speech-in leg that can be proven
/// offline: verification feeds it recorded Gateway frames and asserts that exactly one kind of frame can
/// become the user's question (VO-5/VO-6), which is the assertion this module lives or dies by.
/// </remarks>
public static partial class QwenGatewayProtocol
{
    /// <summary>The GCP wire version this module speaks. Negotiated through <c>session.hello</c>.</summary>
    public const string WireVersion = "7.0.0";

    /// <summary>
    /// Classifies one inbound frame. Never throws for bad input: malformed JSON and unknown event names
    /// are <see cref="GatewayEventKind.Unknown"/>, because a frame we cannot read must never be mistaken
    /// for speech — and must not kill a live conversation either.
    /// </summary>
    public static GatewayEventDecision Classify(string? frameJson)
    {
        if (string.IsNullOrWhiteSpace(frameJson))
        {
            return new GatewayEventDecision { Kind = GatewayEventKind.Unknown, EventType = string.Empty };
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(frameJson);
        }
        catch (JsonException)
        {
            return new GatewayEventDecision
            {
                Kind = GatewayEventKind.Unknown,
                EventType = "<malformed>",
                ErrorMessage = "frame was not valid JSON",
            };
        }

        using (document)
        {
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object)
            {
                return new GatewayEventDecision { Kind = GatewayEventKind.Unknown, EventType = "<non-object>" };
            }

            var type = Text(root, "type") ?? string.Empty;
            return type switch
            {
                GatewayServerEvent.TranscriptDelta or GatewayServerEvent.TranscriptFinal or GatewayServerEvent.TranscriptDiscard
                    => Transcript(type, Text(root, "role"), Text(root, "content")),

                // response.started is the cancel trigger. turn.started deliberately is NOT: the protocol
                // describes a turn as covering the user's own speech too, so cancelling on it would abort
                // the very transcription we are waiting for. That difference is why these are two kinds.
                GatewayServerEvent.ResponseStarted => new() { Kind = GatewayEventKind.AssistantResponseStarted, EventType = type },
                GatewayServerEvent.ResponseInterrupted => new() { Kind = GatewayEventKind.AssistantResponseInterrupted, EventType = type },
                GatewayServerEvent.AudioDelta => new()
                {
                    Kind = GatewayEventKind.AssistantAudio,
                    EventType = type,
                    CarriesAudio = Text(root, "audio") is { Length: > 0 },
                    AudioSampleRate = Int(root, "sampleRate"),
                },
                GatewayServerEvent.AudioDone => new() { Kind = GatewayEventKind.AssistantAudioDone, EventType = type },
                GatewayServerEvent.PlaybackClear => new() { Kind = GatewayEventKind.PlaybackClear, EventType = type },
                GatewayServerEvent.VoiceState => new() { Kind = GatewayEventKind.StateChanged, EventType = type, State = Text(root, "state") },
                GatewayServerEvent.ClientState => new() { Kind = GatewayEventKind.StateChanged, EventType = type, State = Text(root, "state") },
                GatewayServerEvent.TurnStarted => new() { Kind = GatewayEventKind.StateChanged, EventType = type, State = "turn.started" },
                GatewayServerEvent.SessionPing => new() { Kind = GatewayEventKind.SessionPing, EventType = type },
                GatewayServerEvent.InputSuspend => new() { Kind = GatewayEventKind.InputSuspend, EventType = type, State = Text(root, "owner") },
                GatewayServerEvent.InputResume => new() { Kind = GatewayEventKind.InputResume, EventType = type },
                GatewayServerEvent.ToolCall => new()
                {
                    Kind = GatewayEventKind.ToolCall,
                    EventType = type,
                    ToolName = Text(root, "name"),
                    ToolSurface = Text(root, "surface"),
                },
                GatewayServerEvent.Error => new()
                {
                    Kind = GatewayEventKind.Error,
                    EventType = type,
                    ErrorMessage = Text(root, "message") ?? "the Gateway reported an error",
                },
                GatewayServerEvent.MemoryChanged => new() { Kind = GatewayEventKind.MemoryChanged, EventType = type },
                GatewayServerEvent.AgentActivity => new() { Kind = GatewayEventKind.AgentActivity, EventType = type },
                _ => DefaultFor(type, root),
            };
        }
    }

    /// <summary>
    /// Transcript events carry a role, and the role decides everything: a <c>final</c> with
    /// <c>role: user</c> is the question, while the same event with <c>role: assistant</c> is an answer
    /// HeyGanesh must never speak. A transcript with no recognisable role becomes
    /// <see cref="GatewayEventKind.Unknown"/>, which is the fail-safe direction: Unknown can never become
    /// text, so a role the Gateway spells differently goes quiet instead of becoming a fake question.
    /// </summary>
    private static GatewayEventDecision Transcript(string type, string? role, string? text) => role switch
    {
        GatewayServerEvent.RoleUser => type switch
        {
            GatewayServerEvent.TranscriptFinal => new() { Kind = GatewayEventKind.UserFinal, EventType = type, Role = role, Text = text ?? string.Empty },
            GatewayServerEvent.TranscriptDelta => new() { Kind = GatewayEventKind.UserPartial, EventType = type, Role = role, Text = text ?? string.Empty },
            _ => new() { Kind = GatewayEventKind.TranscriptDiscard, EventType = type, Role = role },
        },
        GatewayServerEvent.RoleAssistant => type switch
        {
            GatewayServerEvent.TranscriptFinal => new() { Kind = GatewayEventKind.AssistantFinal, EventType = type, Role = role, Text = text ?? string.Empty },
            GatewayServerEvent.TranscriptDelta => new() { Kind = GatewayEventKind.AssistantPartial, EventType = type, Role = role, Text = text ?? string.Empty },
            _ => new() { Kind = GatewayEventKind.TranscriptDiscard, EventType = type, Role = role },
        },
        _ => new() { Kind = GatewayEventKind.Unknown, EventType = type, Role = role },
    };

    private static GatewayEventDecision DefaultFor(string type, JsonElement root)
    {
        // task.* is the Backend Agent surface. With AGENT_PROTOCOL=none it should never appear at all;
        // giving it its own kind is what lets the client abort loudly instead of quietly ignoring it.
        if (type.StartsWith(GatewayServerEvent.TaskEventPrefix, StringComparison.Ordinal))
        {
            return new() { Kind = GatewayEventKind.TaskEvent, EventType = type };
        }

        return type switch
        {
            GatewayServerEvent.SessionReady => new()
            {
                Kind = GatewayEventKind.SessionLifecycle,
                EventType = type,
                // The wire version is negotiated, not assumed: if the Gateway states one, the handshake
                // checks it. Upstream's schema names the field protocol_version (GatewaySessionReadySchema
                // in shared/protocol/gateway-client-protocol.mjs, checked 2026-10-30, BUG-001); the two
                // older spellings stay as fallbacks. Absent means "not stated", not "wrong".
                Version = Text(root, "protocol_version") ?? Text(root, "protocolVersion") ?? Text(root, "protocol"),
            },
            GatewayServerEvent.GatewayConnected or GatewayServerEvent.GatewayDisconnected
                or GatewayServerEvent.VoiceConnection
                or GatewayServerEvent.VoiceReady or GatewayServerEvent.VoiceOwnership
                or GatewayServerEvent.VoiceDeactivated or GatewayServerEvent.VoiceSleep
                => new() { Kind = GatewayEventKind.SessionLifecycle, EventType = type },
            _ => new() { Kind = GatewayEventKind.Unknown, EventType = type },
        };
    }

    private static string? Text(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static int? Int(JsonElement obj, string name) =>
        obj.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;
}
