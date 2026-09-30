namespace HeyGanesh.Voice;

/// <summary>
/// Transport/provider-level failure from a voice seam that is neither an auth problem
/// (see <see cref="VoiceAuthenticationException"/>) nor rate limiting (see
/// <see cref="VoiceRateLimitException"/>). Carries the HTTP status when the failure came from HTTP.
/// </summary>
/// <remarks>
/// Local device failures (no capture device, no output device) are also reported through this type:
/// the caller's decision — "the voice leg could not run, do not fabricate a verdict" — is identical,
/// and the message names the device so the difference is still legible in the log.
/// </remarks>
public class VoiceClientException : Exception
{
    public int? StatusCode { get; }

    public VoiceClientException(string message, int? statusCode = null) : base(message) => StatusCode = statusCode;
}

/// <summary>A bearer-key problem (missing key / HTTP 401 / 403) — surfaced separately from provider behaviour.</summary>
public sealed class VoiceAuthenticationException : VoiceClientException
{
    public VoiceAuthenticationException(string message) : base(message, null) { }
}

/// <summary>Provider rate limit (HTTP 429).</summary>
public sealed class VoiceRateLimitException : VoiceClientException
{
    public VoiceRateLimitException(string message) : base(message, 429) { }
}

/// <summary>
/// The peer spoke something this module cannot honour: an unparsable frame, a handshake that never
/// completed, a capability the Gateway does not advertise, or — the one that matters most — a Backend
/// Agent event arriving on a session configured with <c>AGENT_PROTOCOL=none</c>.
/// </summary>
/// <remarks>
/// Separate from <see cref="VoiceClientException"/> so the reason is legible, but derived from it so a
/// host that already maps <c>VoiceClientException</c> to "provider error propagated, do not fabricate a
/// verdict" keeps working unchanged.
/// </remarks>
public sealed class VoiceProtocolException : VoiceClientException
{
    public VoiceProtocolException(string message) : base(message, null) { }
}
