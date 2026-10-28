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

    /// <summary>Budget for laying cards out and showing a notice (reset / notice messages).</summary>
    public TimeSpan ControlTimeout { get; init; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// WebView2's profile folder. Default: %LOCALAPPDATA%/HeyGanesh/webview2 (or
    /// $HEYGANESH_DATA_DIR/webview2), the project's runtime-data convention.
    /// </summary>
    public string? UserDataFolder { get; init; }

    /// <summary>The 3D scene window (DEC-030), laid out to the left of the card column.</summary>
    public int SceneWidthDip { get; init; } = 640;
    public int SceneHeightDip { get; init; } = 480;

    /// <summary>
    /// Extra Chromium switches for the WebView2 environment. For verification hosts only — LSU-12 uses
    /// <c>--disable-webgl</c> to prove a scene lesson still completes in speech without WebGL.
    /// </summary>
    public string? AdditionalBrowserArguments { get; init; }

    public static CardSurfaceOptions Default { get; } = new();
}

/// <summary>One model instance in the scene, as the page built it.</summary>
public sealed record SceneInstance(string Id, string Model, int Parts, int Meshes);

/// <summary>One sticky-note label in the scene.</summary>
public sealed record SceneLabel(string Id, string Target, string Text);

/// <summary>
/// Read-only description of the 3D scene window and what its page shows (verification hosts and
/// health checks). <see cref="WebGl"/> is 0 when the page could not start WebGL and is speaking only.
/// </summary>
public sealed record SceneSnapshot(
    string SurfaceId,
    bool Visible,
    int ExStyle,
    uint DisplayAffinity,
    PixelRect Bounds,
    int WebGl,
    string Title,
    IReadOnlyList<string> Tags,
    string Notice,
    IReadOnlyList<SceneInstance> Instances,
    IReadOnlyList<SceneLabel> Labels,
    IReadOnlyList<string> Highlights,
    IReadOnlyDictionary<string, double> Explode,
    IReadOnlyDictionary<string, double[]> PartOffsets,
    IReadOnlyList<string> Playing,
    string CameraView,
    string CameraFocus,
    int Frames,
    int CspViolations,
    double LastModelBuildMs,
    int ScriptElements);

/// <summary>
/// Result of the orientation self-test (DEC-029/DEC-030): three test markers posed from C#, located in
/// the page's own pixels. All three true means the renderer applies the library's frame and rotations.
/// </summary>
public sealed record OrientationProbe(bool XRight, bool YUp, bool ZTurnMapsYToMinusX, string Detail);

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
    string Notice,
    bool BrowserAcceleratorKeys);

/// <summary>Result of the page-isolation self-test.</summary>
public sealed record IsolationProbe(bool NavigationBlocked, bool FetchBlocked);
