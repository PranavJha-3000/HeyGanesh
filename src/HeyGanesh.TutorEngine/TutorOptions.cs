namespace HeyGanesh.TutorEngine;

/// <summary>
/// Tuning for <see cref="TutorService"/>. Immutable, set once, mirroring the other modules' options
/// types.
/// </summary>
/// <remarks>
/// No credentials live here, and none can: the future live <see cref="ITutorClient"/> implementation
/// resolves its key from the process environment at call time (DEC-007's temporary stopgap, the same
/// arrangement Vision and Voice run under), so no field on this type could carry one even by accident
/// — the <see cref="HeyGanesh.Voice"/> <c>SpeakRequest</c> precedent, applied to options.
/// The visual caps exist because every attached image is screen-derived user content leaving the
/// machine: bounded by bytes and by edge length, explicitly, in one place.
/// </remarks>
public sealed class TutorOptions
{
    /// <summary>Defaults: 1600 output chars, 500 completion tokens, one visual ≤ 512 KiB / 1568 px.</summary>
    public static TutorOptions Default { get; } = new();

    /// <summary>
    /// Hard cap on <see cref="TutorResult.ExplanationText"/>. Generous enough for a real explanation,
    /// short enough that a provider rambling past it is treated as the failure it is rather than paid for
    /// and pasted onward. It is a contract with downstream consumers (a spoken answer is built from this
    /// text later), not a suggestion to the provider.
    /// </summary>
    public int MaxExplanationChars { get; init; } = 1_600;

    /// <summary>Per-call completion budget handed to the seam as <see cref="TutorPrompt.MaxOutputTokens"/>.</summary>
    public int MaxOutputTokens { get; init; } = 500;

    /// <summary>
    /// Ceiling for the untrusted <see cref="TutorContext.Description"/> before it is truncated (with a
    /// visible marker) into the prompt. A UIA name or model description that runs past this is either a
    /// paste-dump or an attack surface; neither earns the whole budget.
    /// </summary>
    public int MaxContextDescriptionChars { get; init; } = 400;

    /// <summary>Ceiling for the optional visual, in compressed bytes.</summary>
    public int MaxVisualBytes { get; init; } = 512 * 1_024;

    /// <summary>
    /// Ceiling for either edge of the attached image, in pixels. Matches the Screen Capture downscale cap
    /// (1280) with headroom for a caller that re-encodes a crop at native region size — the crop of a
    /// 4K region can legitimately exceed the frame cap, so the ceiling sits one step above it.
    /// </summary>
    public int MaxVisualDimensionPx { get; init; } = 1_568;

    internal void Validate()
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxExplanationChars, 64);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxOutputTokens, 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxContextDescriptionChars, 16);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxVisualBytes, 1_024);
        ArgumentOutOfRangeException.ThrowIfLessThan(MaxVisualDimensionPx, 32);
    }
}