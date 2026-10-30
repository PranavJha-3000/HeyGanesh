using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;
using System.Runtime.InteropServices;

namespace HeyGanesh.ElementSelection;

/// <summary>
/// Win32 surface for window targeting. Kept internal: these calls only ever locate and describe the
/// target window — they never move it, never read its pixels (that is ScreenCapture's job) and never
/// inject input.
/// </summary>
/// <remarks>
/// GetCursorPos / GetWindowRect return physical pixels when the host process is per-monitor-v2
/// DPI-aware; DPI awareness is deliberately the host's decision — the same contract (and the same
/// standing tripwire) as ScreenCapture's MonitorInfo.Bounds. The verification host cross-checks UIA's
/// BoundingRectangle against GetWindowRect so a DPI mismatch cannot pass silently.
/// </remarks>
internal static class NativeMethods
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct RECT
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    internal const uint GA_ROOT = 2;

    [DllImport("user32.dll")]
    internal static extern bool GetCursorPos(out POINT point);

    // POINT is passed by value: WindowFromPoint takes the struct itself, not a pointer.
    [DllImport("user32.dll")]
    internal static extern IntPtr WindowFromPoint(POINT point);

    // WindowFromPoint can return a child HWND; the UIA walk wants the top-level window so the
    // window title and window rect describe something the user would recognise as "the window".
    [DllImport("user32.dll")]
    internal static extern IntPtr GetAncestor(IntPtr hwnd, uint flags);

    [DllImport("user32.dll")]
    internal static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowTextLength(IntPtr hwnd);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    internal static extern int GetWindowText(IntPtr hwnd, System.Text.StringBuilder text, int maxCount);

    [DllImport("user32.dll")]
    internal static extern bool GetWindowRect(IntPtr hwnd, out RECT rect);
}
