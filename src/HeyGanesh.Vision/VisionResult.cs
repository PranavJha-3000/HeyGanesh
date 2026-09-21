using HeyGanesh.ScreenCapture;

namespace HeyGanesh.Vision;

/// <summary>
/// The confidence tier a future Jev confidence-gate can branch on, derived from the model's
/// self-reported confidence and the cross-sample consistency signal.
/// </summary>
/// <remarks>
/// Intentionally coarse so that Jev owns the numeric thresholds; this module only ever exposes a
/// three-tier verdict plus the two raw inputs (see VisionResult.RawConfidence / Consistency).
/// </remarks>
public enum ConfidenceLevel
{
    Low,
    Medium,
    High,
}

/// <summary>
/// A bounding box in the model's 0..1 normalised coordinate space, where (0,0) is the
/// top-left of the JPEG and (1,1) is the bottom-right.
/// </summary>
/// <remarks>
/// BCL-free by design: System.Drawing.RectangleF would require System.Drawing.Common, which is not
/// in the .NET shared framework and would break the zero-PackageReference stance (DEC-004). This
/// struct is the Vision-module analogue of HeyGanesh.ScreenCapture.PixelRect but in normalised units.
/// </remarks>
public readonly record struct NormalizedBox(float X, float Y, float Width, float Height)
{
    public float Right => X + Width;
    public float Bottom => Y + Height;

    public override string ToString() => $"{{ {X:F3},{Y:F3} {Width:F3}x{Height:F3} }}";
}

/// <summary>
/// A bounding box in the source capture's physical-pixel space -- i.e. the same units as
/// ScreenCaptureService's SourceWidth/SourceHeight: the monitor's true resolution before JPEG downscale.
/// </summary>
public readonly record struct CaptureBox(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;

    public override string ToString() => $"{Width}x{Height}+{X}+{Y}";
}

/// <summary>A pixel-space point shared by the various coordinate planes Vision exposes.</summary>
public readonly record struct PointI(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// One per-call answer from the model -- the raw payload a Jev gate may want to inspect before
/// trusting the reconciled VisionResult.
/// </summary>
/// <remarks>
/// Coordinates are expressed on the model's 0..1000 grid (see DEC-006). This is a mutable accumulator
/// -- VisionResponseParser populates it field-by-field -- whereas VisionResult is an immutable verdict.
/// Parsed is false when this sample's JSON could not be extracted; its RawText is still preserved.
/// </remarks>
public sealed class VisionSample
{
    /// <summary>Whether the JSON payload was successfully parsed into the fields below.</summary>
    public bool Parsed { get; set; } = false;

    /// <summary>Value of the model's "found" flag (only meaningful when Parsed is true).</summary>
    public bool Found { get; set; } = false;

    /// <summary>The model's description of the element, if any.</summary>
    public string? Element { get; set; }

    /// <summary>Box on the 0..1000 grid (only meaningful when Parsed and Found are true).</summary>
    public int? X1 { get; set; }
    public int? Y1 { get; set; }
    public int? X2 { get; set; }
    public int? Y2 { get; set; }

    /// <summary>Model self-reported confidence, 0..1 (only meaningful when Parsed and Found are true).</summary>
    public float? Confidence { get; set; }

    /// <summary>Free-text note from the model (e.g. why not found).</summary>
    public string? Note { get; set; }

    /// <summary>The model's raw, unparsed completion text.</summary>
    public string RawText { get; set; } = string.Empty;

        /// <summary>Why parsing failed, when it did. Absent on success.</summary>
    public string? ParseError { get; set; }

    /// <summary>Wall-clock time for the underlying API call that produced this sample.</summary>
    public TimeSpan Latency { get; set; }

    /// <summary>Token usage returned by the provider for this sample (best-effort).</summary>
    public int PromptTokens { get; set; }
    public int CompletionTokens { get; set; }
}

/// <summary>
/// The structured result of asking the vision model to locate an element in a captured monitor frame.
/// </summary>
/// <remarks>
/// <para>
/// This is the surface a future Jev confidence-gate consumes. Three coordinate planes are emitted so
/// every downstream consumer can pick the one it owns without re-deriving anything:
/// </para>
/// <list type="bullet">
///   <item><description><see cref="Normalized"/> -- 0..1 box on the JPEG the model saw.</description></item>
///   <item><description><see cref="Rectangle"/> -- that box back-computed into the source capture's
///   physical pixels (the monitor's true resolution).</description></item>
///   <item><description><see cref="BoxOnScreen"/> / <see cref="CenterOnScreen"/> -- the box/center
///   translated into global physical-screen pixel space, the space <see cref="PixelRect"/> and
///   <see cref="MonitorInfo.Bounds"/> already describe, ready for a cursor/overlay module.</description></item>
/// </list>
/// Confidence is a signal, not a guarantee: <see cref="Confidence"/> fuses <see cref="RawConfidence"/>
/// (the model's self-report) with <see cref="Consistency"/> (cross-sample agreement). A single
/// low-confidence answer is <see cref="ConfidenceLevel.Low"/> with the raw sample preserved; a parse
/// failure or blank frame is a distinct <see cref="FailureReason"/>, never disguised as low confidence.
/// </remarks>
public sealed record VisionResult
{
    /// <summary>Which display this result came from (the capture's monitor, for downstream routing).</summary>
    public MonitorInfo Monitor { get; init; } = null!;

    /// <summary>Whether the model located the element. False for a clean "not present" answer.</summary>
    public bool Found { get; init; }

    /// <summary>Human description of the located element, verbatim from the model.</summary>
    public string? Description { get; init; }

    /// <summary>Box in 0..1 normalised coordinates on the JPEG.</summary>
    public NormalizedBox? Normalized { get; init; }

    /// <summary>Box back-computed into the source capture's physical pixels (monitor-local).</summary>
    public CaptureBox? Rectangle { get; init; }

    /// <summary>Box translated into global physical-screen pixel space.</summary>
    public PixelRect? BoxOnScreen { get; init; }

    /// <summary>Center of <see cref="BoxOnScreen"/> in global physical-screen pixel space.</summary>
    public PointI? CenterOnScreen { get; init; }

    /// <summary>Three-tier verdict a future Jev gate branches on.</summary>
    public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.Low;

    /// <summary>Model self-reported confidence, 0..1. Null if the model did not emit one.</summary>
    public float? RawConfidence { get; init; }

    /// <summary>
    /// Derived agreement across samples, 0..1 (1.0 = identical; 0.0 = total disagreement).
    /// Null when fewer than 2 samples yielded a box (no signal).
    /// </summary>
    public float? Consistency { get; init; }

    /// <summary>How many API samples were attempted toward <see cref="Consistency"/>.</summary>
    public int SampleCount { get; init; }

    /// <summary>The per-call raw answers -- the "raw model payload" Jev may inspect before trusting.</summary>
    public IReadOnlyList<VisionSample> Samples { get; init; } = Array.Empty<VisionSample>();

    /// <summary>Provider model identifier returned for this call.</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>Image fidelity flag forwarded to the provider (high / low / auto).</summary>
    public string ImageDetail { get; init; } = string.Empty;

    /// <summary>Total wall-clock time for the whole LocateAsync call (not just per-call).</summary>
    public TimeSpan Latency { get; init; }

    /// <summary>Aggregate prompt tokens across samples (best-effort; may be 0).</summary>
    public int PromptTokens { get; init; }

    /// <summary>Aggregate completion tokens across samples.</summary>
    public int CompletionTokens { get; init; }

    /// <summary>
    /// True when the capture frame was blank (Secure Desktop / elevated / RDP empty). Surfaced up front
    /// rather than as a low-confidence miss -- see DEC-006.
    /// </summary>
    public bool WasBlankFrame { get; init; }

    /// <summary>
    /// Non-null when the call could not produce a result at all (blank frame, parse failures, network
    /// error, auth/quota error). Absent for a valid "element not found" answer, which is a successful
    /// result with <see cref="Found"/> = false.
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>Convenience: true when a failure prevented a result.</summary>
    public bool Failed => FailureReason is not null;

    public override string ToString()
    {
        if (!Found) return $"VisionResult: not found (failure={FailureReason ?? "model returned no match"}, N={SampleCount})";
        var loc = BoxOnScreen?.ToString() ?? CenterOnScreen?.ToString() ?? Normalized?.ToString() ?? "(n/a)";
        return $"VisionResult: found '{Description}' @ {loc} (conf={Confidence}, model={RawConfidence:F2}, consistency={Consistency}, N={SampleCount})";
    }
}

