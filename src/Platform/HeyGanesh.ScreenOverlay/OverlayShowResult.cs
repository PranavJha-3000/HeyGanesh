namespace HeyGanesh.ScreenOverlay;

/// <summary>
/// What a <see cref="ScreenOverlayService.ShowAsync"/> call actually did. Deliberately not a coordinate
/// type: this carries an outcome and a reason, never a position.
/// </summary>
/// <remarks>
/// A show that displayed nothing is a normal, expected result — an honest "no match" must not draw
/// anything — so it is reported here rather than thrown. The reason constants below are the contract
/// the verification host asserts against.
/// </remarks>
public sealed record OverlayShowResult
{
    /// <summary>The verdict was an honest no-match, so nothing was drawn (the CO-4 case).</summary>
    public const string ReasonNoMatch = "no-match";

    /// <summary>The leg failed, so there was no verdict to point at (includes the CO-6 Vision not-found case).</summary>
    public const string ReasonFailure = "failure";

    /// <summary><c>Found</c> was true but no usable rectangle came with it; geometry is never invented.</summary>
    public const string ReasonNoBox = "no-box";

    /// <summary>The rectangle intersects no connected display — the topology changed since it was resolved.</summary>
    public const string ReasonOffScreen = "off-screen";

    /// <summary>The caller's token (or service disposal) ended the show; any window that appeared was removed.</summary>
    public const string ReasonCancelled = "cancelled";

    /// <summary>Our own window could not be excluded from capture, so it was never left on screen.</summary>
    public const string ReasonExclusionFailed = "exclusion-failed";

    /// <summary>The process is not per-monitor DPI aware, so physical-pixel placement would be wrong.</summary>
    public const string ReasonNotPerMonitorDpiAware = "not-per-monitor-dpi-aware";

    /// <summary>Our own window could not be created (Win32 failure); <see cref="Detail"/> carries the error.</summary>
    public const string ReasonWindowCreationFailed = "window-creation-failed";

    /// <summary>
    /// True only when the highlight ran its full requested duration on screen. An early end
    /// (cancellation, <see cref="ScreenOverlayService.HideAsync"/>, disposal) reports false even if a
    /// window appeared briefly — windows are always removed before this returns.
    /// </summary>
    public bool Displayed { get; init; }

    /// <summary>Why nothing was displayed, or why a displayed highlight ended early. Null on a full-duration show.</summary>
    public string? Reason { get; init; }

    /// <summary>How long the highlight was actually on screen; <see cref="TimeSpan.Zero"/> when nothing was drawn.</summary>
    public TimeSpan ShownFor { get; init; }

    /// <summary>How many monitor windows were created for this show (0 when nothing was drawn).</summary>
    public int WindowCount { get; init; }

    /// <summary>Free-text diagnostics for a failure reason (Win32 error code, offending HWND, and so on).</summary>
    public string? Detail { get; init; }

    public override string ToString()
        => Displayed
            ? $"OverlayShowResult: displayed on {WindowCount} window(s) for {ShownFor.TotalMilliseconds:F0}ms" +
              (Reason is null ? string.Empty : $" (ended early: {Reason})")
            : $"OverlayShowResult: nothing displayed ({Reason ?? "unknown"})" +
              (Detail is null ? string.Empty : $" -- {Detail}");
}
