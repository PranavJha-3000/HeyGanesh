namespace HeyGanesh.TutorEngine;

/// <summary>Where an attached visual came from, as declared by the caller that produced it.</summary>
public enum TutorVisualKind
{
    /// <summary>A crop of one region of an already-retained frame — normally around the resolved box.</summary>
    CroppedRegion = 0,

    /// <summary>A downscaled whole frame — the "explain what is shown here" shape, no specific target.</summary>
    DownscaledFrame = 1,
}

/// <summary>
/// One bounded, opaque teaching image: the JPEG bytes, their declared dimensions, and provenance
/// metadata. Deliberately NOT a screen type, a capture type, or a provider type — see
/// <see cref="TutorContext"/> for why the visual crosses the seam as plain data.
/// </summary>
/// <remarks>
/// <para>
/// The Tutor never decodes these bytes and holds no decoder; whether a future model receives them as a
/// data URL, a multipart part, or a proxy upload is that provider implementation's mapping, not a
/// concept in this contract (the least-common-denominator reasoning behind DEC-015). They cross the
/// seam as separate binary — never base64-spliced into the prompt text — so byte caps stay auditable
/// and the text channels stay scannable.
/// </para>
/// <para>
/// Bytes may contain anything a screen showed, including text that reads like instructions. The prompt
/// labels the attachment as untrusted data (see TutorService's rendering), and the service itself
/// never parses or follows anything inside it.
/// </para>
/// </remarks>
public sealed record TutorVisual
{
    /// <summary>The compressed image. Never decoded here; never null/empty; capped by options.</summary>
    public required byte[] JpegBytes { get; init; }

    /// <summary>Width in pixels as the caller encoded it — the provider needs it, and so does the dimension cap.</summary>
    public required int WidthPx { get; init; }

    /// <summary>Height in pixels as the caller encoded it.</summary>
    public required int HeightPx { get; init; }

    /// <summary>Fixed contract: "image/jpeg". Anything else is refused rather than passed through.</summary>
    public string MimeType { get; init; } = "image/jpeg";

    /// <summary>Declared provenance. A visual claiming to be a crop must say which region.</summary>
    public TutorVisualKind Kind { get; init; } = TutorVisualKind.CroppedRegion;

    /// <summary>
    /// The global physical-pixel region the bytes were cropped from, when known (null for a whole frame).
    /// Provenance metadata for the record and the prompt — never teaching prose (coordinates do not speak).
    /// </summary>
    public int? SourceRegionX { get; init; }

    public int? SourceRegionY { get; init; }
    public int? SourceRegionWidth { get; init; }
    public int? SourceRegionHeight { get; init; }

    /// <summary>
    /// Structural validation only — the caps are enforced against <see cref="TutorOptions"/> by
    /// <see cref="TutorService"/>, because "too big" depends on the options the service was built with.
    /// Every failure is an <see cref="ArgumentException"/>: a malformed visual is a caller bug, and a
    /// caller bug must never be dressed up as a provider failure.
    /// </summary>
    internal void ValidateStructure()
    {
        if (JpegBytes is null || JpegBytes.Length == 0)
        {
            throw new ArgumentException("TutorVisual.JpegBytes must contain the encoded image.", nameof(JpegBytes));
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(WidthPx, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(HeightPx, 1);

        if (!string.Equals(MimeType, "image/jpeg", StringComparison.Ordinal))
        {
            throw new ArgumentException(
                $"The visual contract is opaque JPEG only, got '{MimeType}'. Re-encode before attaching.",
                nameof(MimeType));
        }

        var region = new[] { SourceRegionX, SourceRegionY, SourceRegionWidth, SourceRegionHeight };
        var set = region.Count(v => v is not null);
        if (set is > 0 and < 4)
        {
            throw new ArgumentException(
                "A source region is all four values or none — a half-known crop is provenance drift, not data.",
                nameof(SourceRegionX));
        }

        if (set == 4 && (SourceRegionWidth <= 0 || SourceRegionHeight <= 0))
        {
            throw new ArgumentException("Source region dimensions must be positive.", nameof(SourceRegionWidth));
        }
    }
}