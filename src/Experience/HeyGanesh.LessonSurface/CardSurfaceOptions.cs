using HeyGanesh.Abstractions;

namespace HeyGanesh.LessonSurface;

/// <summary>Layout and timing for the card surface. Sizes are in DIPs and scaled by each card's DPI.</summary>
public sealed class CardSurfaceOptions
{
    public int CardWidthDip { get; init; } = 560;
    public int CardHeightDip { get; init; } = 420;
    public int GapDip { get; init; } = 16;
    public int MarginDip { get; init; } = 24;

    /// <summary>How long a beat may take to be acknowledged by the page before it is a fault.</summary>
    public TimeSpan ApplyTimeout { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>
    /// WebView2's profile folder. Default: %LOCALAPPDATA%/HeyGanesh/webview2 (or
    /// $HEYGANESH_DATA_DIR/webview2), the project's runtime-data convention.
    /// </summary>
    public string? UserDataFolder { get; init; }

    public static CardSurfaceOptions Default { get; } = new();
}

/// <summary>
/// Read-only description of one card, for verification hosts and health checks. It reports what the
/// page shows and how the window is configured; it changes nothing.
/// </summary>
public sealed record CardSnapshot(
    string SurfaceId,
    string Title,
    bool Visible,
    int ExStyle,
    uint DisplayAffinity,
    PixelRect Bounds,
    IReadOnlyList<string> ElementIds,
    IReadOnlyDictionary<string, string> Texts,
    int Arrows,
    IReadOnlyList<string> HighlightedIds,
    int PlotPoints,
    int ImgElements,
    int ScriptElements,
    int BoldElements,
    string Notice);

/// <summary>Result of the page-isolation self-test.</summary>
public sealed record IsolationProbe(bool NavigationBlocked, bool FetchBlocked);
