namespace HeyGanesh.TutorEngine;

/// <summary>
/// The fully-rendered request a provider receives: assembled text, output budget, and — when the
/// caller attached one — the opaque visual. The seam's transport shape (Chat Completions, a proxy
/// envelope, a recording replay) is the implementation's business; nothing in this record is
/// provider-specific.
/// </summary>
/// <remarks>
/// <see cref="TutorService"/> is the only builder, which is what makes every implementation — fake,
/// recorded, future live — see byte-identical text for a given request (TU-10 pins it). The visual
/// crosses as its own field, never spliced into <see cref="UserText"/>, so the text channels stay
/// scannable and the byte cap stays the single enforcement point.
/// </remarks>
public sealed record TutorPrompt
{
    /// <summary>The teaching contract: role, grounding rules, output rules. Owned by TutorService.</summary>
    public required string SystemText { get; init; }

    /// <summary>The question, then the labelled-untrusted screen-data block, then the visual notice.</summary>
    public required string UserText { get; init; }

    /// <summary>Completion budget for this call, from <see cref="TutorOptions.MaxOutputTokens"/>.</summary>
    public int MaxOutputTokens { get; init; }

    /// <summary>The bounded visual, or null. At most one — there is no list here on purpose.</summary>
    public TutorVisual? Visual { get; init; }
}

/// <summary>
/// The raw completion the seam hands back: text plus usage accounting. The service validates it
/// (non-empty, within the explanation cap) and owns turning it into a <see cref="TutorResult"/>; the
/// implementation does not parse, repair, or re-shape teaching content.
/// </summary>
public sealed record TutorCompletion
{
    /// <summary>The provider's answer text, verbatim. Empty/whitespace is a provider failure, not a blank lesson.</summary>
    public required string Text { get; init; }

    /// <summary>Prompt tokens as reported by the provider; 0 when it reports none. Best-effort accounting.</summary>
    public int InputTokens { get; init; }

    /// <summary>Completion tokens as reported by the provider; 0 when it reports none.</summary>
    public int OutputTokens { get; init; }

    /// <summary>Provider model id. A stable label, never a key and never a URL.</summary>
    public string ModelId { get; init; } = string.Empty;
}