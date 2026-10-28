namespace HeyGanesh.Voice;

/// <summary>
/// How HeyGanesh talks to the qwen-audio-agent Gateway: where it is, what this client declares, and what
/// the Gateway must prove before a listen is allowed to start.
/// </summary>
/// <remarks>
/// <para>
/// The Gateway is an external runtime, not a component of this repository — it is a separate Node service
/// the operator runs (or the Desktop app bundles), with its own provider credentials. Nothing in this file
/// holds a key, and no key crosses the Gateway Client Protocol: this client authenticates as a Gateway
/// client, and the speech provider's key stays inside the Gateway's own configuration.
/// </para>
/// <para>
/// <b>No default URL on purpose.</b> The Gateway's listen port is deployment-specific (CLI, background
/// service, Desktop bundle, remote WSS), and a guessed default would be a silent misconfiguration: a
/// listen that reaches the wrong service, or nothing at all, looks the same to the user. The endpoint is
/// configured explicitly, or through <see cref="GatewayUrlEnvVar"/>.
/// </para>
/// </remarks>
public sealed class QwenGatewayOptions
{
    /// <summary>Defaults: endpoint from the environment, no Backend Agent, 16 kHz mono input.</summary>
    public static QwenGatewayOptions Default { get; } = new();

    /// <summary>The Gateway's client WebSocket endpoint (for example <c>ws://127.0.0.1:PORT/api/realtime</c>).</summary>
    public string? GatewayUrl { get; init; }

    /// <summary>Environment variable consulted when <see cref="GatewayUrl"/> is null.</summary>
    public string GatewayUrlEnvVar { get; init; } = "HEYGANESH_QWEN_GATEWAY_URL";

    /// <summary>Health path used for the pre-flight check, joined onto the gateway origin.</summary>
    public string HealthPath { get; init; } = "/api/health";

    /// <summary>What this client declares in the handshake.</summary>
    public SessionHelloRequest Hello { get; init; } = new();

    /// <summary>
    /// Require the Gateway to advertise its conversation-client capability before listening. On by default:
    /// without it, this client is guessing that the event contract exists rather than knowing it.
    /// </summary>
    public bool RequireConversationClientCapability { get; init; } = true;

    /// <summary>The capability that proves the Gateway serves the published client event contract.</summary>
    public string RequiredCapability { get; init; } = "realtime.conversation-client-v1";

    /// <summary>
    /// Require the Gateway to be running with its Backend Agent disabled (<c>AGENT_PROTOCOL=none</c>).
    /// On by default. Note what this can and cannot do: it checks the health payload for a backend
    /// protocol field and refuses when one is configured, but only when the Gateway reports it. The
    /// guarantee that survives a Gateway that reports nothing is the runtime <c>task.*</c> guard, which
    /// aborts the listen outright (see <see cref="QwenGatewayClient"/>).
    /// </summary>
    public bool RequireBackendDisabled { get; init; } = true;

    /// <summary>Values of a reported backend protocol that count as "disabled".</summary>
    public IReadOnlyList<string> BackendDisabledValues { get; init; } = ["", "none", "off", "disabled"];

    /// <summary>Sample rate this client sends. 16 kHz mono PCM is what the realtime frontends consume.</summary>
    public int InputSampleRate { get; init; } = 16000;

    /// <summary>
    /// The label this frontend reports in <see cref="UtteranceResult.FrontendId"/>. Stable, and never the
    /// endpoint — a result object must not leak where the service runs.
    /// </summary>
    public string FrontendId { get; init; } = VoiceService.QwenFrontendId;

    /// <summary>The resolved endpoint, or the reason there is none. Throws when unset.</summary>
    internal Uri ResolveGatewayUri()
    {
        var configured = !string.IsNullOrWhiteSpace(GatewayUrl)
            ? GatewayUrl
            : Environment.GetEnvironmentVariable(GatewayUrlEnvVar);

        if (string.IsNullOrWhiteSpace(configured))
        {
            throw new VoiceClientException(
                $"No qwen-audio-agent Gateway endpoint is configured. Set {GatewayUrlEnvVar} " +
                "(for example ws://127.0.0.1:<port>/api/realtime) or pass QwenGatewayOptions.GatewayUrl. " +
                "There is deliberately no default: a wrong default reaches the wrong service, or nothing, " +
                "and both look like a silent user.");
        }

        if (!Uri.TryCreate(configured.Trim(), UriKind.Absolute, out var uri) ||
            (uri.Scheme != Uri.UriSchemeWs && uri.Scheme != Uri.UriSchemeWss))
        {
            throw new VoiceClientException(
                $"The configured Gateway endpoint is not a ws:// or wss:// URL ({GatewayUrlEnvVar}).");
        }

        return uri;
    }

    /// <summary>Builds the health URL from the gateway origin. Used for the pre-flight check only.</summary>
    internal Uri ResolveHealthUri(Uri gateway)
    {
        var builder = new UriBuilder(gateway)
        {
            Scheme = gateway.Scheme == Uri.UriSchemeWss ? Uri.UriSchemeHttps : Uri.UriSchemeHttp,
            Path = HealthPath,
            Query = string.Empty,
        };

        return builder.Uri;
    }

    /// <summary>
    /// The endpoint in its loggable form: scheme, host and port only. Path, query and user-info are
    /// dropped, so a URL that carries a token in a query string can never reach a log or a result object
    /// (the same rule that keeps provider keys out of both).
    /// </summary>
    internal static string SafeEndpoint(Uri uri) => $"{uri.Scheme}://{uri.Host}:{uri.Port}";
}
