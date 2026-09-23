using HeyGanesh.ScreenCapture;

namespace HeyGanesh.ElementSelection;

/// <summary>
/// The confidence tier a future Jev confidence-gate can branch on. Mirrors
/// <c>HeyGanesh.Vision.ConfidenceLevel</c> member-for-member (same names, same 0.8/0.5 boundaries)
/// so the primary and fallback paths stay uniform; deliberately a separate type — the two modules
/// must not reference each other (DEC-008).
/// </summary>
public enum ConfidenceLevel
{
    Low,
    Medium,
    High,
}

/// <summary>A pixel-space point — the Element Selection analogue of Vision's PointI (kept separate for the same reason).</summary>
public readonly record struct PointI(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// One call's verdict. Where it makes sense the shape matches <c>VisionResult</c> exactly (Found,
/// Description, BoxOnScreen/CenterOnScreen in global physical px, Confidence tier + raw probability
/// signals) so a future orchestrator can treat the primary (UIA+Jev) and fallback (Vision) paths
/// uniformly — see DEC-008 for the fallback composition contract.
/// </summary>
public sealed record SelectionResult
{
    public bool Found { get; init; }

    /// <summary>The chosen candidate's UIA Name (verbatim, like Vision's model description).</summary>
    public string? Description { get; init; }

    /// <summary>The chosen candidate's BoundingRectangle — global physical screen pixels, same space as MonitorInfo.Bounds.</summary>
    public PixelRect? BoxOnScreen { get; init; }

    /// <summary>Center of <see cref="BoxOnScreen"/> in global physical-screen pixel space.</summary>
    public PointI? CenterOnScreen { get; init; }

    /// <summary>Three-tier verdict a future Jev gate branches on (0.8/0.5 boundaries, same as Vision).</summary>
    public ConfidenceLevel Confidence { get; init; } = ConfidenceLevel.Low;

    /// <summary>Jev's self-rated confidence for the choice answer, 0..1 (null if the model did not emit one).</summary>
    public double? JevConfidence { get; init; }

    /// <summary>The winning option's probability from the choice answer, 0..1 (null if absent).</summary>
    public double? WinnerProbability { get; init; }

    /// <summary>Probability Jev assigned to the "none" escape option, 0..1 (null when the option was not offered).</summary>
    public double? NoneProbability { get; init; }

    /// <summary>The noul "is there a plausible match" probability, 0..1 (null when that answer was absent).</summary>
    public double? PlausibleProbability { get; init; }

    /// <summary>The full per-option distribution from the choice answer — the raw signal a gate may inspect before trusting the verdict.</summary>
    public IReadOnlyDictionary<string, double>? Probabilities { get; init; }

    /// <summary>Raw Jev choice id ("c007", or "none" for an honest no-match). Diagnostics.</summary>
    public string? Choice { get; init; }

    /// <summary>The winning candidate (null unless Found).</summary>
    public UiaCandidate? Selected { get; init; }

    /// <summary>The window that was walked (handle, title, GetWindowRect vs UIA root rect for the cross-check).</summary>
    public TargetWindowInfo? Target { get; init; }

    /// <summary>Control-view elements seen in the subtree walk (before filtering).</summary>
    public int TotalEnumerated { get; init; }

    /// <summary>Candidates that survived the interactive filter (before capping).</summary>
    public int TotalAfterFilter { get; init; }

    /// <summary>The capped, id-assigned manifest actually offered to Jev (c001..), in ranked order.</summary>
    public IReadOnlyList<UiaCandidate> Candidates { get; init; } = Array.Empty<UiaCandidate>();

    public string ModelId { get; init; } = string.Empty;

    /// <summary>Total wall-clock time for the whole SelectAsync call (enumeration + provider call).</summary>
    public TimeSpan Latency { get; init; }

    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    /// <summary>
    /// Non-null when the call could not produce a result at all (no resolvable window, empty/undecipherable
    /// UIA tree, Jev returned no choice, choice not in the offered manifest). Absent for a valid
    /// "no matching element" answer, which is a successful result with <see cref="Found"/> = false.
    /// The documented fallback triggers for the Vision module are the empty-tree and unmapped-choice
    /// cases (DEC-008).
    /// </summary>
    public string? FailureReason { get; init; }

    /// <summary>Convenience: true when a failure prevented a result.</summary>
    public bool Failed => FailureReason is not null;

    public override string ToString()
    {
        if (Failed) return $"SelectionResult: failed ({FailureReason})";
        if (!Found) return $"SelectionResult: no match (choice={Choice}, N={Candidates.Count})";
        return $"SelectionResult: found '{Description}' @ {BoxOnScreen} center {CenterOnScreen} " +
               $"(conf={Confidence}, winnerProb={WinnerProbability:F2}, N={Candidates.Count})";
    }
}
