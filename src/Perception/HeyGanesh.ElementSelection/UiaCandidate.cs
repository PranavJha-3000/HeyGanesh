using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;

namespace HeyGanesh.ElementSelection;

/// <summary>
/// One interactive UI element harvested from the UIA tree of the target window. Geometry is the
/// element's BoundingRectangle verbatim — global physical screen pixels, the same coordinate space
/// as <see cref="ScreenCapture"/>'s MonitorInfo.Bounds (the host's per-monitor-v2 DPI awareness makes
/// GetCursorPos/GetWindowRect agree with it; the verification host cross-checks this every run).
/// </summary>
/// <param name="Id">
/// Option id offered to Jev ("c001", "c002", ...), assigned by <see cref="ElementSelectionService"/>
/// after ranking. Empty on the raw enumeration pass — ids only exist for the capped manifest.
/// </param>
/// <param name="Name">The element's UIA Name (never empty: unnamed elements are useless to a selector).</param>
/// <param name="ControlType">Friendly UIA control-type name, e.g. "Button", "MenuItem".</param>
/// <param name="ControlTypeId">Raw UIA_ControlTypeIds value, for diagnostics and future retuning.</param>
/// <param name="Rect">BoundingRectangle in global physical screen pixels.</param>
/// <param name="PositionHint">
/// Coarse 3x3 placement inside the window ("top-left", "center", ...). Shipped in the Jev option
/// descriptions so identically-named controls (two "OK" buttons in one dialog) stay distinguishable
/// without putting raw coordinates into the prompt — Jev must not be tempted to reason in pixels.
/// </param>
public sealed record UiaCandidate(
    string Id,
    string Name,
    string ControlType,
    int ControlTypeId,
    PixelRect Rect,
    string PositionHint)
{
    public override string ToString() => $"{ControlType} '{Name}' {Rect} ({PositionHint})";
}

/// <summary>
/// What the enumerator learned about the window it walked. <see cref="WindowRect"/> comes from
/// GetWindowRect and <see cref="UiaRootRect"/> from UIA — they should be the same rectangle; the
/// harness prints both as the standing coordinate-space cross-check.
/// </summary>
public sealed record TargetWindowInfo(IntPtr Handle, string Title, PixelRect WindowRect, PixelRect UiaRootRect);

/// <summary>
/// Result of one enumeration pass: the filtered interactive candidates plus the raw counts the
/// diagnostics and the fallback decision want. TotalEnumerated is the control-view subtree size;
/// InteractiveAfterFilter is what survived the visible+enabled+named+non-empty-rect+type filter.
/// </summary>
public sealed record CandidateSet(
    TargetWindowInfo Target,
    IReadOnlyList<UiaCandidate> Candidates,
    int TotalEnumerated,
    int InteractiveAfterFilter);
