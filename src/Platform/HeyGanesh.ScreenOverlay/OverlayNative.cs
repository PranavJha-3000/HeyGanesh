using System.Runtime.InteropServices;

namespace HeyGanesh.ScreenOverlay;

/// <summary>
/// The Win32 surface the overlay needs: one top-level layered window per monitor, a message pump
/// thread, and plain GDI drawing.
/// </summary>
/// <remarks>
/// <para>
/// Read-only with respect to the user's session. There is deliberately <b>no</b> declaration here for
/// any input path — no <c>SendInput</c>, no <c>mouse_event</c>/<c>keybd_event</c>, no
/// <c>SetCursorPos</c>, no <c>SetFocus</c>, no <c>SetForegroundWindow</c>, and no UI Automation write
/// pattern. HeyGanesh points; the user acts (CONSTRAINTS.md, DEC-011).
/// </para>
/// <para>
/// The only window-state calls present are the ones that keep the overlay out of the user's way:
/// click-through extended styles at creation, <c>SetLayeredWindowAttributes</c> for the colour key,
/// <c>SetWindowPos</c> with <c>SWP_NOACTIVATE</c> to show without activating, and
/// <c>DestroyWindow</c> to remove it.
/// </para>
/// </remarks>
internal static class OverlayNative
{
    // ---- window styles ------------------------------------------------------

    /// <summary>WS_POPUP — no caption, no border, no system menu.</summary>
    internal const uint WS_POPUP = 0x80000000;

    internal const uint WS_EX_TOPMOST = 0x00000008;
    internal const uint WS_EX_TRANSPARENT = 0x00000020;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;
    internal const uint WS_EX_LAYERED = 0x00080000;

    /// <summary>
    /// WS_EX_NOACTIVATE — the window never becomes active and never appears in the taskbar. This is the
    /// style that makes it impossible for the overlay to steal focus from whatever the user is doing;
    /// <c>WM_MOUSEACTIVATE</c>/<c>MA_NOACTIVATE</c> is handled as well, so a click that somehow reached
    /// the window still could not activate it.
    /// </summary>
    internal const uint WS_EX_NOACTIVATE = 0x08000000;

    /// <summary>All extended styles every overlay window must carry, combined once so the assertion guard and the creation cannot drift apart.</summary>
    internal const uint OverlayExtendedStyle =
        WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOPMOST | WS_EX_TOOLWINDOW;

    // ---- messages, hit testing, activation ---------------------------------

    internal const uint WM_DESTROY = 0x0002;
    internal const uint WM_PAINT = 0x000F;
    internal const uint WM_ERASEBKGND = 0x0014;
    internal const uint WM_MOUSEACTIVATE = 0x0021;
    internal const uint WM_NCHITTEST = 0x0084;

    /// <summary>
    /// HTTRANSPARENT — "this point is not mine, keep looking at the windows below". Returned for every
    /// point of every overlay window.
    /// </summary>
    /// <remarks>
    /// <c>WS_EX_TRANSPARENT</c> alone is not sufficient for a top-level window: it is documented as a
    /// painting/hit-test hint and its behaviour for top-level windows has varied. Returning
    /// <c>HTTRANSPARENT</c> from <c>WM_NCHITTEST</c> is the explicit, authoritative statement that
    /// makes the OS route mouse input to the window underneath, so the overlay can never swallow a
    /// click, a drag or a hover meant for the user's own application.
    /// </remarks>
    internal static readonly IntPtr HTTRANSPARENT = new(-1);

    /// <summary>MA_NOACTIVATE — do not activate the window as a side effect of the click.</summary>
    internal static readonly IntPtr MA_NOACTIVATE = new(3);

    // ---- window placement --------------------------------------------------

    /// <summary>
    /// A message-only window: owns no visible surface at all, so a service instance that never shows a
    /// highlight still gets its own thread-safe pump without occupying a pixel of the desktop.
    /// </summary>
    internal static readonly IntPtr HWND_MESSAGE = new(-3);
    internal static readonly IntPtr HWND_TOPMOST = new(-1);

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;
    internal const uint SWP_SHOWWINDOW = 0x0040;
    internal const int SW_SHOWNOACTIVATE = 4;
    // ---- layered windows ---------------------------------------------------

    internal const uint LWA_COLORKEY = 0x0001;

    /// <summary>
    /// The colour key: every pixel the renderer paints in this colour becomes fully transparent.
    /// </summary>
    /// <remarks>
    /// Magenta because it is the colour a UI is least likely to use — critical, because any host
    /// content that happened to be painted in the key colour would show as a hole. The renderer paints
    /// the window background in exactly this colour and nothing else with it.
    /// </remarks>
    internal const uint ColorKey = 0x00FF00FF; // COLORREF 0x00BBGGRR -> R=255, G=0, B=255

    // ---- GDI ---------------------------------------------------------------

    /// <summary>NULL_PEN — selected before filling so shapes are filled without an outline of their own.</summary>
    internal const int NULL_PEN = 8;

    // ---- DPI awareness -----------------------------------------------------

    /// <summary>PROCESS_PER_MONITOR_DPI_AWARE — what a PerMonitorV2 host reports.</summary>
    internal const int PROCESS_PER_MONITOR_DPI_AWARE = 2;

    // ---- pump --------------------------------------------------------------

    /// <summary>WM_APP + 1 — "drain the UI-thread action queue".</summary>
    internal const uint WM_APP_DRAIN = 0x8001;

    /// <summary>Win32 error for "class already registered", which a second service instance in one process hits.</summary>
    internal const int ERROR_CLASS_ALREADY_EXISTS = 1410;
}
