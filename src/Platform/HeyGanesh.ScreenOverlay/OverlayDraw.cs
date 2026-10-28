using HeyGanesh.Abstractions;

namespace HeyGanesh.ScreenOverlay;

/// <summary>
/// What an overlay window will be asked to draw, resolved once per show into the coordinate space of
/// one monitor's fullscreen window.
/// </summary>
/// <param name="MonitorBounds">
/// The monitor's bounds in <b>global physical pixels</b> — exactly what the window is positioned at,
/// so the only conversion performed anywhere in this module is this subtraction. Nothing is scaled;
/// the coordinates arrived in this space from the Context Orchestrator and are used as-is.
/// </param>
/// <param name="OutlineLocal">
/// The resolved element's rectangle translated into window-local physical pixels, or null when the
/// element lives on another monitor (the window for that monitor still appears; it just has no
/// rectangle to outline).
/// </param>
/// <param name="CenterLocal">
/// The resolved center in window-local physical pixels, or null when the center is on another
/// monitor. Only the window containing the center draws the target glyph.
/// </param>
/// <remarks>
/// Created by the service's geometry pass and consumed by the overlay window's paint handler; never
/// exposed outside the assembly.
/// </remarks>
internal sealed record OverlayDraw(
    PixelRect MonitorBounds,
    PixelRect? OutlineLocal,
    PixelRect? CenterLocal);
