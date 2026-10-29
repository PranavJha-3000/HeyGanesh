namespace HeyGanesh.Abstractions;

/// <summary>A rectangle in physical screen pixels.</summary>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    public int Right => X + Width;
    public int Bottom => Y + Height;
    public override string ToString() => $"{Width}x{Height}+{X}+{Y}";
}

/// <summary>Which coordinate-detection leg produced a verdict.</summary>
public enum GuidanceSource
{
    ElementSelection = 0,
    Vision = 1,
}
/// <summary>
/// The orchestrator's own confidence tier. Member-for-member identical to the two legs'
/// <c>ConfidenceLevel</c> enums (also at 0.8/0.5 boundaries).
/// </summary>
public enum GuidanceConfidence
{
    Low,
    Medium,
    High,
}

/// <summary>
/// A global physical-screen pixel point in the orchestrator's normalized shape.
/// </summary>
public readonly record struct GuidancePoint(int X, int Y)
{
    public override string ToString() => $"({X},{Y})";
}

/// <summary>
/// One resolved guidance verdict: the single shape the next module consumes, whichever leg answered.
/// </summary>
public sealed record GuidanceResult
{
    public bool Found { get; init; }
    public string? Description { get; init; }
    public PixelRect? BoxOnScreen { get; init; }
    public GuidancePoint? CenterOnScreen { get; init; }
    public GuidanceConfidence Confidence { get; init; } = GuidanceConfidence.Low;
    public GuidanceSource Source { get; init; }
    public string? FailureReason { get; init; }
    public bool Failed => FailureReason is not null;
    public string? PrimaryFailureReason { get; init; }

    /// <summary>What the Low-confidence check did (DEC-037); null when no check ran.</summary>
    public string? ConfidenceCheck { get; init; }
    public string? Choice { get; init; }
    public double? JevConfidence { get; init; }
    public double? WinnerProbability { get; init; }
    public double? NoneProbability { get; init; }
    public double? PlausibleProbability { get; init; }
    public float? RawConfidence { get; init; }
    public float? Consistency { get; init; }
    public string ModelId { get; init; } = string.Empty;
    public IReadOnlyList<MonitorCapture> Captures { get; init; } = Array.Empty<MonitorCapture>();
    public TimeSpan Latency { get; init; }
    public int InputTokens { get; init; }
    public int OutputTokens { get; init; }

    public override string ToString()
    {
        if (Failed) return $"GuidanceResult: failed via {Source} ({FailureReason})";
        if (!Found) return $"GuidanceResult: no match via {Source} (choice={Choice ?? "-"}, conf={Confidence})";
        return $"GuidanceResult: found '{Description}' @ {BoxOnScreen} center {CenterOnScreen} " +
               $"via {Source} (conf={Confidence})";
    }
}
/// <summary>One connected display.</summary>
public sealed record MonitorInfo(IntPtr Handle, string DeviceName, PixelRect Bounds, bool IsPrimary)
{
    public override string ToString()
        => $"{DeviceName} {Bounds}{(IsPrimary ? " [primary]" : string.Empty)}";
}

/// <summary>One monitor's still frame, ready to upload.</summary>
public sealed record MonitorCapture(
    MonitorInfo Monitor,
    int SourceWidth,
    int SourceHeight,
    int OutputWidth,
    int OutputHeight,
    byte[] Jpeg,
    double NonBlackPixelRatio,
    int FramesObserved,
    TimeSpan Duration);