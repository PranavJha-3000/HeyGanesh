using System.Runtime.InteropServices;

namespace HeyGanesh.ScreenCapture;

/// <summary>
/// Win32 and D3D interop used by the Screen Capture module.
/// </summary>
/// <remarks>
/// Read-only with respect to the desktop: nothing here injects input, moves windows or changes
/// process-wide state. The one process-wide API present (<see cref="SetProcessDpiAwarenessContext"/>)
/// is declared but never called by this library — DPI awareness is the host application's decision
/// (see docs/ai/DECISIONS.md DEC-004).
/// </remarks>
internal static class NativeMethods
{
    // ---- monitor enumeration -------------------------------------------------

    /// <summary>MONITOR_DEFAULTTONEAREST — used to resolve the cursor's monitor.</summary>
    internal const uint MONITOR_DEFAULTTONEAREST = 2;

    /// <summary>MONITORINFOF_PRIMARY — rcMonitor is the primary display.</summary>
    internal const uint MONITORINFOF_PRIMARY = 0x00000001;

    // ---- window display affinity --------------------------------------------

    /// <summary>WDA_NONE — no affinity; the window is captured normally.</summary>
    internal const uint WDA_NONE = 0x00000000;

    /// <summary>
    /// WDA_MONITOR — the window's content is replaced by black in captures. Not what we want:
    /// black rectangles are as visible to a vision model as the window itself.
    /// </summary>
    internal const uint WDA_MONITOR = 0x00000001;

    /// <summary>
    /// WDA_EXCLUDEFROMCAPTURE — the window is omitted entirely from capture output, as if it were
    /// not on screen. This is the primitive the Screen Overlay must use so HeyGanesh never
    /// photographs its own UI (and so captures can never feed back into themselves).
    /// </summary>
    internal const uint WDA_EXCLUDEFROMCAPTURE = 0x00000011;

    // ---- DPI awareness contexts (negative pseudo-handles) --------------------

    internal static readonly IntPtr DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 = new(-4);

    // ---- D3D11 ---------------------------------------------------------------

    internal const int D3D_DRIVER_TYPE_HARDWARE = 1;
    internal const uint D3D11_CREATE_DEVICE_BGRA_SUPPORT = 0x00000020;
    internal const uint D3D11_SDK_VERSION = 7;

    /// <summary>IID_IDXGIDevice — queried from the D3D11 device to hand to WinRT.</summary>
    internal static readonly Guid IID_IDXGIDevice = new("54ec77fa-1377-44e6-8c32-88fd5f44c84c");

    // ---- structs -------------------------------------------------------------

    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;

        internal readonly int Width => Right - Left;

        internal readonly int Height => Bottom - Top;
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    /// <summary>MONITORINFOEXW — <see cref="Size"/> must be set before the call.</summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    internal struct MonitorInfoEx
    {
        internal int Size;
        internal Rect Monitor;
        internal Rect Work;
        internal uint Flags;

        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 32)]
        internal string DeviceName;
    }

    internal delegate bool MonitorEnumProc(IntPtr hMonitor, IntPtr hdc, ref Rect monitorRect, IntPtr data);

    // ---- monitor / cursor / window APIs -------------------------------------

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool EnumDisplayMonitors(IntPtr hdc, IntPtr clipRect, MonitorEnumProc callback, IntPtr data);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetMonitorInfoW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetMonitorInfo(IntPtr hMonitor, ref MonitorInfoEx info);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr MonitorFromPoint(Point point, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetCursorPos(out Point point);

    /// <summary>Declared for verification hosts only; this library never moves the cursor.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowDisplayAffinity(IntPtr hwnd, uint affinity);

    /// <summary>Process-global. Declared so hosts can opt in deliberately, never called here.</summary>
    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    // ---- D3D11 device creation ----------------------------------------------

    [DllImport("d3d11.dll")]
    internal static extern int D3D11CreateDevice(
        IntPtr adapter,
        int driverType,
        IntPtr software,
        uint flags,
        IntPtr featureLevels,
        uint featureLevelCount,
        uint sdkVersion,
        out IntPtr device,
        out int featureLevel,
        out IntPtr immediateContext);

    /// <summary>Wraps an IDXGIDevice as the WinRT <c>IDirect3DDevice</c> projection.</summary>
    [DllImport("d3d11.dll")]
    internal static extern int CreateDirect3D11DeviceFromDXGIDevice(IntPtr dxgiDevice, out IntPtr inspectable);
}
