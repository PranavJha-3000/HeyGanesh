namespace HeyGanesh.TutorEngine;

/// <summary>
/// The single boundary between the Tutor and whatever generates teaching text — the same
/// injectable-seam role <c>IVisionClient</c> plays for Vision and <c>ITtsClient</c> for Voice.
/// </summary>
/// <remarks>
/// <para>
/// An implementation turns a rendered <see cref="TutorPrompt"/> (plus the opaque visual when one is
/// attached) into a <see cref="TutorCompletion"/>. It owns transport, provider payload shape, and its
/// own credentials (from the environment at call time, DEC-007); it never sees questions-before-render,
/// guidance verdicts, captures, UIA, or any other leg's keys, and it never re-shapes teaching policy.
/// </para>
/// <para>
/// <b>This change ships no live implementation.</b> That is the point (DEC-015): the seam is verified
/// against fakes and recorded completions so no provider, model, endpoint or key name is committed
/// before the owner decides them. The first live client is a separate, documented change.
/// </para>
/// <para>
/// <b>Failure contract:</b> throw <see cref="TutorAuthenticationException"/> (exit 12 upstream),
/// <see cref="TutorRateLimitException"/>, or <see cref="TutorClientException"/> (exit 3). Cancellation
/// is <see cref="OperationCanceledException"/> and passes through the service untouched (exit 13).
/// Anything else propagates as the unexpected fault it is.
/// </para>
/// <para>
/// <b>Lifetime:</b> implementations are disposable but caller-owned — <see cref="TutorService"/> never
/// creates one and never disposes one. The implementer that holds sockets or an HttpClient disposes
/// what it owns, when its owner says so.
/// </para>
/// </remarks>
public interface ITutorClient : IDisposable
{
    /// <summary>
    /// Generates one completion for one rendered prompt. Must observe
    /// <paramref name="cancellationToken"/>; must return promptly-null-free text (the service treats
    /// empty/whitespace completion as a provider failure, never as a blank lesson).
    /// </summary>
    Task<TutorCompletion> CompleteAsync(TutorPrompt prompt, CancellationToken cancellationToken);
}