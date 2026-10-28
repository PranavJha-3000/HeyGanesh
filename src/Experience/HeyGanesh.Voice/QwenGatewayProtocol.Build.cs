using System.Text.Json.Nodes;

namespace HeyGanesh.Voice;

/// <summary>
/// What this client declares in <c>session.hello</c>. A plain record, so verification can build one and
/// assert the resulting JSON without a Gateway in the room.
/// </summary>
public sealed record SessionHelloRequest
{
    /// <summary>GCP wire version. We speak exactly one version and say so on both ends of the range.</summary>
    public string Protocol { get; init; } = QwenGatewayProtocol.WireVersion;

    /// <summary>Client type reported to the Gateway, for its logs and ownership bookkeeping.</summary>
    public string ClientType { get; init; } = "heyganesh";

    /// <summary>Client version reported to the Gateway.</summary>
    public string ClientVersion { get; init; } = "1.0.0";

    /// <summary>Human label for this client instance.</summary>
    public string ClientLabel { get; init; } = "HeyGanesh";

    /// <summary>
    /// Capabilities requested — only what this client actually does: audio in, text in, playback receipts.
    /// No images, no files, no Task commands, no Client Events, no Client Actions.
    /// </summary>
    public IReadOnlyList<string> Capabilities { get; init; } = ["input.audio", "input.text", "playback.receipts"];

    /// <summary>Whether this client captures audio (yes — it owns the microphone while listening).</summary>
    public bool InputEnabled { get; init; } = true;

    /// <summary>
    /// False: this client never plays the frontend's voice; HeyGanesh speaks with its own TTS. A
    /// presentation preference, NOT a generation switch — see <see cref="GatewaySuppressionLedger"/>.
    /// </summary>
    public bool OutputEnabled { get; init; }

    /// <summary>False: this client sends real audio, so it is not a text-only client.</summary>
    public bool TextOnly { get; init; }
}

/// <summary>
/// Outbound message construction for the Gateway Client Protocol — pure string building, so every message
/// this module can send is assertable offline.
/// </summary>
/// <remarks>
/// Message names are the protocol's own (<see cref="GatewayClientMessage"/>); payload field names follow
/// the published schemas. Where a payload could not be verified against a live Gateway it is said so in
/// place rather than guessed at silently — see the note on <see cref="BuildAudioAppend"/>.
/// </remarks>
public static partial class QwenGatewayProtocol
{
    /// <summary>Fresh event id, shaped like the Gateway's own generator: <c>evt_client_&lt;uuid&gt;</c>.</summary>
    public static string NewEventId(string origin = "client") => $"evt_{origin}_{Guid.NewGuid():N}";

    /// <summary>The 7.0 handshake: sent once per connection, immediately after the socket opens.</summary>
    public static string BuildSessionHello(SessionHelloRequest request, string? eventId = null)
    {
        ArgumentNullException.ThrowIfNull(request);

        return new JsonObject
        {
            ["type"] = GatewayClientMessage.SessionHello,
            ["event_id"] = eventId ?? NewEventId(),
            ["protocol"] = new JsonObject { ["min"] = request.Protocol, ["max"] = request.Protocol },
            ["client"] = new JsonObject
            {
                ["type"] = request.ClientType,
                ["version"] = request.ClientVersion,
                ["instance_id"] = NewEventId("instance"),
                ["label"] = request.ClientLabel,
            },
            ["capabilities"] = new JsonArray([.. request.Capabilities.Select(c => (JsonNode)c)]),
            ["connection"] = new JsonObject
            {
                ["input_enabled"] = request.InputEnabled,
                ["output_enabled"] = request.OutputEnabled,
                ["text_only"] = request.TextOnly,
            },
        }.ToJsonString();
    }

    /// <summary>
    /// One microphone frame: PCM16 little-endian mono at <paramref name="sampleRate"/>, the shape the
    /// Gateway's realtime frontends consume (DashScope realtime runs 16 kHz PCM input).
    /// </summary>
    /// <remarks>
    /// The exact payload field names for this message were NOT verifiable from the published schemas at
    /// write time. <c>audio</c> (base64) plus <c>sample_rate</c> is what is sent, and VO-7 asserts the
    /// message type only. If a live Gateway rejects the payload, this is the single place to correct —
    /// which is exactly why the frame is built here instead of inline in the send loop.
    /// </remarks>
    public static string BuildAudioAppend(ReadOnlyMemory<byte> pcm, int sampleRate, string? eventId = null)
    {
        if (pcm.Length == 0)
        {
            throw new ArgumentException("There is no audio to append.", nameof(pcm));
        }

        return new JsonObject
        {
            ["type"] = GatewayClientMessage.InputAudioAppend,
            ["event_id"] = eventId ?? NewEventId(),
            ["audio"] = Convert.ToBase64String(pcm.Span),
            ["sample_rate"] = sampleRate,
        }.ToJsonString();
    }

    /// <summary>
    /// Stops a frontend response that has already started — the countermeasure DEC-013 records. The
    /// frontend generates its own conversational reply to every utterance; this is how HeyGanesh keeps
    /// that reply from reaching the speaker. It prevents the reply's <i>effect</i>, after generation began.
    /// </summary>
    public static string BuildResponseCancel(string? responseId = null, string? eventId = null)
    {
        var message = new JsonObject
        {
            ["type"] = GatewayClientMessage.ResponseCancel,
            ["event_id"] = eventId ?? NewEventId(),
        };

        if (!string.IsNullOrWhiteSpace(responseId))
        {
            message["response_id"] = responseId;
        }

        return message.ToJsonString();
    }

    /// <summary>Asks the Gateway to drop in-flight assistant audio; sent with cancel once audio is already flowing.</summary>
    public static string BuildInterrupt(string? eventId = null) => new JsonObject
    {
        ["type"] = GatewayClientMessage.Interrupt,
        ["event_id"] = eventId ?? NewEventId(),
    }.ToJsonString();

    /// <summary>Heartbeat answer. Echoes the ping's id so the Gateway can pair request and answer.</summary>
    public static string BuildSessionPong(string? requestEventId = null, string? eventId = null)
    {
        var message = new JsonObject
        {
            ["type"] = GatewayClientMessage.SessionPong,
            ["event_id"] = eventId ?? NewEventId(),
        };

        if (!string.IsNullOrWhiteSpace(requestEventId))
        {
            message["request_event_id"] = requestEventId;
        }

        return message.ToJsonString();
    }

    /// <summary>
    /// Acknowledges <c>playback.clear</c>. Nothing is playing here — this client never plays assistant
    /// audio — so the receipt keeps the Gateway's playback bookkeeping honest rather than stopping audio.
    /// </summary>
    public static string BuildPlaybackCancelled(string? responseId = null, string? eventId = null)
    {
        var message = new JsonObject
        {
            ["type"] = GatewayClientMessage.PlaybackCancelled,
            ["event_id"] = eventId ?? NewEventId(),
        };

        if (!string.IsNullOrWhiteSpace(responseId))
        {
            message["response_id"] = responseId;
        }

        return message.ToJsonString();
    }

    /// <summary>
    /// Acknowledges <c>input.suspend</c>, so the Gateway can tell "this client stopped capturing" apart
    /// from "it never heard us".
    /// </summary>
    public static string BuildInputSuspendAck(string owner, string? reason = null, string? eventId = null)
    {
        if (string.IsNullOrWhiteSpace(owner))
        {
            throw new ArgumentException("input.suspend.ack requires the owner the Gateway named.", nameof(owner));
        }

        var message = new JsonObject
        {
            ["type"] = GatewayClientMessage.InputSuspendAck,
            ["event_id"] = eventId ?? NewEventId(),
            ["owner"] = owner,
        };

        if (!string.IsNullOrWhiteSpace(reason))
        {
            message["reason"] = reason;
        }

        return message.ToJsonString();
    }
}
