namespace HeyGanesh.ScreenOverlay;

/// <summary>
/// Tuning for <see cref="ScreenOverlayService"/>. Immutable, set once, mirroring the other modules'
/// options types.
/// </summary>
/// <remarks>
/// Contains no confidence threshold and no classification: the overlay draws whatever
/// <c>GuidanceResult</c> it is handed, for a bounded time, and gates nothing (DEC-009/DEC-010 keep
/// thresholds out of every module until the Jev confidence-gate exists).
/// </remarks>
public sealed class ScreenOverlayOptions
{
    /// <summary>Defaults: ~3 s visible, box and pointer both drawn, PerMonitorV2 required.</summary>
    public static ScreenOverlayOptions Default { get; } = new();

    /// <summary>
    /// How long the highlight stays on screen before removing itself. Bounded on purpose: an overlay
    /// the user cannot dismiss that outlives the question becomes clutter, and this program must never
    /// feel like it has taken over the desktop.
    /// </summary>
    public TimeSpan VisibleDuration { get; init; } = TimeSpan.FromSeconds(3);

    /// <summary>Whether the resolved element's rectangle is outlined.</summary>
    public bool ShowBox { get; init; } = true;

    /// <summary>Whether a target glyph is drawn at the resolved center.</summary>
    public bool ShowPointer { get; init; } = true;

    /// <summary>
    /// Whether to refuse to show when the process is not per-monitor DPI aware. On by default because
    /// the failure it prevents is the worst kind: without PerMonitorV2 awareness Windows virtualises
    /// the window coordinates, so the highlight would land in a confidently *wrong* place instead of
    /// failing. Note this is the overlay checking its own host, not setting awareness — awareness is
    /// process-global and stays the host's decision (DEC-004).
    /// </summary>
    public bool RequirePerMonitorDpi { get; init; } = true;

    /// <summary>
    /// Thickness of the outline drawn around the resolved rectangle, in physical pixels, before the
    /// inner accent stroke.
    /// </summary>
    public int BoxThickness { get; init; } = 4;

    /// <summary>Radius of the target glyph at the resolved center, in physical pixels.</summary>
    public int PointerRadius { get; init; } = 10;

    internal void Validate()
    {
        if (VisibleDuration <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(VisibleDuration), "VisibleDuration must be positive.");
        }

        // A cap, not a preference: this is the guard that keeps "temporarily" true.
        if (VisibleDuration > TimeSpan.FromMinutes(1))
        {
            throw new ArgumentOutOfRangeException(
                nameof(VisibleDuration),
                "VisibleDuration must be at most one minute; a highlight that outlives the moment stops being guidance.");
        }

        ArgumentOutOfRangeException.ThrowIfLessThan(BoxThickness, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(PointerRadius, 2);
    }
}
