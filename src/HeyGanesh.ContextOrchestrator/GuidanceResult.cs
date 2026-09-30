using HeyGanesh.ScreenCapture;

namespace HeyGanesh.ContextOrchestrator;

/// <summary>Which coordinate-detection leg produced a verdict.</summary>
public enum GuidanceSource
{
    /// <summary>Element Selection — the PRIMARY leg: UIA enumeration plus a Jev choice over the ranked manifest (DEC-008).</summary>
    ElementSelection = 0,

    /// <summary>Vision — the FALLBACK leg, reached only when Element Selection returned a Failed result.</summary>
    Vision = 1,
}

/// <summary>
/// The orchestrator's own confidence tier. Member-for-member identical to the two legs'
/// <c>ConfidenceLevel</c> enums (also at 0.8/0.5 boundaries), because this type exists to hold
/// whichever leg's verdict arrived without making either leg's type the shared one (DEC-009).
/// </summary>
public enum GuidanceConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>
/// A global physical-screen pixel point in the orchestrator's normalized shape — the counterpart of
/// each leg's own point type, which stay where they are.
/// </summary>
public readonly record struct GuidancePoint(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// One resolved guidance verdict: the single shape the next module (and, later, the Jev
/// confidence-gate) consumes, whichever leg answered.
/// </summary>
/// <remarks>
/// <para>
/// This type is the deferred unification of DEC-009, and it lives here — in the Context Orchestrator —
/// and nowhere else. <c>SelectionResult</c> and <c>VisionResult</c> are untouched, and neither leg
/// references the other or this type; the orchestrator is the only consumer that sees both, and it
/// normalizes them here. There is deliberately no <c>HeyGanesh.Abstractions</c>.
/// </para>
/// <para>
/// Shape parity is what makes this cheap: <c>Found</c>, <c>Description</c>, <c>BoxOnScreen</c> /
/// <c>CenterOnScreen</c> in global physical pixels, a three-tier confidence, and the raw signals the
/// legs already surface. Nothing is gated and nothing is re-scored — a tier is carried, not acted on.
/// </para>
/// <para>
/// A "no match" verdict (a leg answering honestly that the element is not there) is a SUCCESS with
/// <see cref="Found"/> = false and no <see cref="FailureReason"/>. A failure means no verdict was
/// produced at all.
/// </para>
/// </remarks>
public sealed record GuidanceResult
{
    /// <summary>Whether an element was resolved. False is a real answer for an honest no-match.</summary>
    public bool Found { get; init; }

    /// <summary>What the answering leg said it found (UIA Name for Element Selection, model text for Vision).</summary>
    public string? Description { get; init; }

    /// <summary>The resolved box in global physical-screen pixels — the same space as <c>MonitorInfo.Bounds</c>.</summary>
    public PixelRect? BoxOnScreen { get; init; }

    /// <summary>Center of <see cref="BoxOnScreen"/> in global physical-screen pixels.</summary>
    public GuidancePoint? CenterOnScreen { get; init; }

    /// <summary>Three-tier verdict carried through from the answering leg, unchanged.</summary>
    public GuidanceConfidence Confidence { get; init; } = GuidanceConfidence.Low;

    /// <summary>Which leg produced this verdict.</summary>
    public GuidanceSource Source { get; init; }

    /// <summary>
    /// Non-null when the answering leg reported a failure. Carried verbatim from that leg — this type
    /// never re-interprets a verdict.
    /// </summary>
    /// <remarks>
    /// Asymmetric on purpose, because the legs are: an honest no-match from the PRIMARY leg
    /// (<c>Choice == "none"</c>) has no failure reason at all, whereas the FALLBACK leg currently
    /// reports a model-side not-found as a failure (<c>VisionService.Reconcile</c> sets "the model
    /// inspected the image but could not locate the requested element"). So <see cref="Found"/> = false
    /// arrives with <see cref="Failed"/> = true when Vision answered that way — see the CO-6 note in
    /// docs/ai/TEST-CHECKLIST.md. Re-classifying it here would be inventing policy the legs own.
    /// </remarks>
    public string? FailureReason { get; init; }

    /// <summary>Convenience: true when a failure prevented a verdict.</summary>
    public bool Failed => FailureReason is not null;

    /// <summary>
    /// Why the PRIMARY leg was skipped and the fallback ran. Null when Element Selection answered.
    /// Kept because the documented fallback triggers (no interactive candidates, unmapped choice id,
    /// no choice at all) are indistinguishable in the verdict itself, and a bug report needs to tell
    /// them apart.
    /// </summary>
    public string? PrimaryFailureReason { get; init; }

    /// <summary>Element Selection's raw choice id ("c007", or "none" for the honest no-match). Null when that leg never answered.</summary>
    public string? Choice { get; init; }

    /// <summary>Element Selection: Jev's self-rated confidence for the choice answer, 0..1.</summary>
    public double? JevConfidence { get; init; }

    /// <summary>Element Selection: probability of the winning option, 0..1.</summary>
    public double? WinnerProbability { get; init; }

    /// <summary>Element Selection: probability Jev gave the "none" escape option, 0..1 — the signal that separates a confident no-match from a lazy one.</summary>
    public double? NoneProbability { get; init; }

    /// <summary>Element Selection: probability of the "is there a plausible match at all" (noul) answer, 0..1.</summary>
    public double? PlausibleProbability { get; init; }

    /// <summary>Vision: the model's self-reported confidence, 0..1. Null when the fallback leg did not run.</summary>
    public float? RawConfidence { get; init; }

    /// <summary>Vision: cross-sample agreement, 0..1 (null for a single sample — no signal).</summary>
    public float? Consistency { get; init; }

    /// <summary>The provider model id of the leg that answered this verdict.</summary>
    public string ModelId { get; init; } = string.Empty;

    /// <summary>
    /// The frames captured for this session, cursor's display first — empty when
    /// <see cref="ContextOrchestratorOptions.RetainCaptures"/> is false. Vision consumed
    /// <c>[0]</c>; the Screen Overlay will annotate the same frame.
    /// </summary>
    public IReadOnlyList<MonitorCapture> Captures { get; init; } = Array.Empty<MonitorCapture>();

    /// <summary>Total wall-clock time for the whole session (capture + whichever legs ran).</summary>
    public TimeSpan Latency { get; init; }

    /// <summary>Input tokens summed across the legs that ran (Element Selection input + Vision prompt).</summary>
    public int InputTokens { get; init; }

    /// <summary>Output tokens summed across the legs that ran (Element Selection output + Vision completion).</summary>
    public int OutputTokens { get; init; }

    public override string ToString()
    {
        if (Failed) return $"GuidanceResult: failed via {Source} ({FailureReason})";
        if (!Found) return $"GuidanceResult: no match via {Source} (choice={Choice ?? "-"}, conf={Confidence})";
        return $"GuidanceResult: found '{Description}' @ {BoxOnScreen} center {CenterOnScreen} " +
               $"via {Source} (conf={Confidence})";
    }
}


