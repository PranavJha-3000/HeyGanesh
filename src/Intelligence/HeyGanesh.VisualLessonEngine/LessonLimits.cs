namespace HeyGanesh.VisualLessonEngine;

/// <summary>
/// The v1 hard limits. They bound what one spoken lesson can ask a renderer to do and keep beats
/// short enough to be one idea each (segmenting). Raised only by a schema minor, driven by evals.
/// </summary>
public sealed record LessonLimits
{
    public int MaxJsonBytes { get; init; } = 65536;
    public int MaxDepth { get; init; } = 16;
    public int MaxSurfaces { get; init; } = 4;
    public int MaxBeats { get; init; } = 40;
    public int MaxOpsPerBeat { get; init; } = 12;
    public int MaxTitleChars { get; init; } = 80;
    public int MaxSayChars { get; init; } = 400;
    public int MaxTextChars { get; init; } = 280;

    /// <summary>3D scenes per plan. One: spike 0b measured ~360–500 MB of WebView2 memory per scene page.</summary>
    public int MaxScenes { get; init; } = 1;

    /// <summary>Model instances live in a scene at once — a teaching scene, not a showroom.</summary>
    public int MaxModelsPerScene { get; init; } = 3;

    /// <summary>At or above this share of degraded beats the plan becomes plain spoken text.</summary>
    public double FallbackDegradedRatio { get; init; } = 0.5;

    public static LessonLimits Default { get; } = new();
}

/// <summary>Closed vocabularies the model must pick from; anything else is refused by the validator.</summary>
public static class LessonVocabulary
{
    public static IReadOnlySet<string> Glyphs { get; } = new HashSet<string>(StringComparer.Ordinal)
    {
        "atom", "bulb", "book", "cell", "flask", "gear", "leaf", "magnet", "cycle", "scale",
    };
}
