using System.Diagnostics;

namespace HeyGanesh.TutorEngine;

/// <summary>
/// One explanation, produced from one request. Content and accounting only — it speaks nothing,
/// draws nothing, and carries no reference that could reach the screen (DEC-015).
/// </summary>
/// <remarks>
/// The teaching shape, not a verdict shape, is deliberate: there is no Found and no FailureReason
/// here, because for a tutor "I could not resolve that element" is not a teaching outcome — the
/// caller resolved (or did not resolve) <i>before</i> this call, and a genuine failure to teach
/// throws rather than returning a sad string. This is the one seam result in HeyGanesh that is not a
/// three-state verdict, and it should stay that way: a negative verdict belongs to the legs.
/// </remarks>
public sealed record TutorResult
{
    /// <summary>
    /// The explanation, plain text, validated against <see cref="TutorOptions.MaxExplanationChars"/>.
    /// Shaped to be usable verbatim as future <c>SpeakRequest.Text</c> and lesson-engine input.
    /// </summary>
    public required string ExplanationText { get; init; }

    /// <summary>Prompt tokens as reported by the seam (0 when unreported).</summary>
    public int InputTokens { get; init; }

    /// <summary>Completion tokens as reported by the seam (0 when unreported).</summary>
    public int OutputTokens { get; init; }

    /// <summary>The seam's model label, carried through for diagnostics. Never a key, never a URL.</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Wall-clock time of the provider call (rendering excluded — it is deterministic and instant).</summary>
    public TimeSpan Latency { get; init; }

    public override string ToString() =>
        $"TutorResult: {ExplanationText.Length} chars via {ModelId} ({Latency.TotalMilliseconds:F0} ms, " +
        $"in={InputTokens} out={OutputTokens})";
}