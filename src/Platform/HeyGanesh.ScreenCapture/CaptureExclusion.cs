using System.Runtime.InteropServices;

namespace HeyGanesh.ScreenCapture;

/// <summary>
/// The self-exclusion contract: HeyGanesh's own windows must be invisible to capture output.
/// </summary>
/// <remarks>
/// <para>Screen Capture deliberately does <b>not</b> try to filter HeyGanesh's windows out of the
/// frames it produces. WGC monitor capture photographs the composited desktop; there is no
/// per-capture exclusion list to pass in. The correct primitive is the other way round — each of
/// our own windows marks itself, and the OS then omits it from every capture on the system,
/// ours included.</para>
///
/// <para><b>Contract for Screen Overlay (and any future HeyGanesh window):</b> call
/// <see cref="TryExcludeFromCapture"/> once the window has an HWND — after the native window
/// exists, before or independently of any capture. Use the HWND of the real top-level window, not
/// a XAML <c>Window</c> object and not a child HWND. Affinity is stored per window, so it must be
/// re-applied whenever a window is recreated (per-monitor overlays, monitor hot-plug, a window
/// that is destroyed and rebuilt).</para>
///
/// <para><b>Why this matters beyond tidiness.</b> An overlay that draws the answer on screen while
/// a capture can still see it creates a feedback loop: the next capture would contain our own
/// output, and a vision model would be asked to interpret HeyGanesh's rendering as if it were the
/// user's screen. Exclusion is what breaks that loop.</para>
///
/// <para><b>Scope.</b> This only affects windows owned by the calling process, which is exactly the
/// requirement. It cannot hide other applications' windows.</para>
/// </remarks>
public static class CaptureExclusion
{
    /// <summary>
    /// Marks one of our own windows as excluded from capture output.
    /// </summary>
    /// <param name="windowHandle">HWND of a top-level window owned by this process.</param>
    /// <param name="win32Error">Win32 error code when the call fails; 0 on success.</param>
    /// <returns><see langword="true"/> if the affinity was applied.</returns>
    public static bool TryExcludeFromCapture(IntPtr windowHandle, out int win32Error)
        => ApplyAffinity(windowHandle, NativeMethods.WDA_EXCLUDEFROMCAPTURE, out win32Error);

    /// <summary>
    /// Clears exclusion so the window is captured normally again.
    /// </summary>
    /// <param name="windowHandle">HWND of a top-level window owned by this process.</param>
    /// <param name="win32Error">Win32 error code when the call fails; 0 on success.</param>
    /// <returns><see langword="true"/> if the affinity was cleared.</returns>
    public static bool TryIncludeInCapture(IntPtr windowHandle, out int win32Error)
        => ApplyAffinity(windowHandle, NativeMethods.WDA_NONE, out win32Error);

    private static bool ApplyAffinity(IntPtr windowHandle, uint affinity, out int win32Error)
    {
        if (windowHandle == IntPtr.Zero)
        {
            win32Error = 87; // ERROR_INVALID_PARAMETER — no window, nothing to mark.
            return false;
        }

        if (NativeMethods.SetWindowDisplayAffinity(windowHandle, affinity))
        {
            win32Error = 0;
            return true;
        }

        win32Error = Marshal.GetLastWin32Error();
        return false;
    }
}
