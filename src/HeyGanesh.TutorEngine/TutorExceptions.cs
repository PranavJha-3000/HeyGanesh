namespace HeyGanesh.TutorEngine;

/// <summary>
/// Transport/provider-level failure from the teaching seam that is neither an auth problem (see
/// <see cref="TutorAuthenticationException"/>) nor rate limiting (see
/// <see cref="TutorRateLimitException"/>). Carries the HTTP status when the failure came from HTTP.
/// </summary>
/// <remarks>
/// This family deliberately mirrors Voice's shape rather than referencing it: a shared-abstraction
/// assembly is what DEC-009 exists to forbid, and a Tutor that referenced Voice would weld the
/// teacher to the speaker. The parallelism is for the host's benefit — the same 12/3/13 exit
/// mapping reads identically across legs.
/// </remarks>
public class TutorClientException : Exception
{
    public int? StatusCode { get; }

    public TutorClientException(string message, int? statusCode = null) : base(message) => StatusCode = statusCode;
}

/// <summary>A bearer-key problem (missing key / HTTP 401 / 403) — surfaced separately from provider behaviour (exit 12 upstream).</summary>
public sealed class TutorAuthenticationException : TutorClientException
{
    public TutorAuthenticationException(string message) : base(message, null) { }
}

/// <summary>Provider rate limit (HTTP 429) — its own type so a future caller can back off rather than retry flat out.</summary>
public sealed class TutorRateLimitException : TutorClientException
{
    public TutorRateLimitException(string message) : base(message, 429) { }
}