using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HeyGanesh.ScreenCapture;

/// <summary>A rectangle in physical screen pixels.</summary>
/// <remarks>
/// Physical pixels, not DPI-virtualised ones: WGC delivers frames at the monitor's true
/// resolution, so every coordinate this module reports is in that same space. A host that is not
/// per-monitor-DPI-aware will see <c>GetCursorPos</c>/<c>MonitorFromPoint</c> disagree with these
/// bounds — see the DPI note on <see cref="ScreenCaptureService"/>.
/// </remarks>
public readonly record struct PixelRect(int X, int Y, int Width, int Height)
{
    /// <summary>Right edge, exclusive.</summary>
    public int Right => X + Width;

    /// <summary>Bottom edge, exclusive.</summary>
    public int Bottom => Y + Height;

    public override string ToString() => $"{Width}x{Height}+{X}+{Y}";
}

/// <summary>One connected display.</summary>
/// <param name="Handle">The <c>HMONITOR</c>. Stable while the display topology is unchanged.</param>
/// <param name="DeviceName">The GDI device name, e.g. <c>\\.\DISPLAY1</c>.</param>
/// <param name="Bounds">The full monitor rectangle, in physical pixels.</param>
/// <param name="IsPrimary">Whether Windows considers this the primary display.</param>
public sealed record MonitorInfo(IntPtr Handle, string DeviceName, PixelRect Bounds, bool IsPrimary)
{
    /// <summary>Human-readable form used by diagnostics and the verification host.</summary>
    public override string ToString()
        => $"{DeviceName} {Bounds}{(IsPrimary ? " [primary]" : string.Empty)}";
}

/// <summary>Monitor enumeration, cursor lookup and capture ordering.</summary>
internal static class Monitors
{
    /// <summary>Enumerates connected displays in the order Windows reports them.</summary>
    internal static IReadOnlyList<MonitorInfo> Enumerate()
    {
        var monitors = new List<MonitorInfo>(2);

        NativeMethods.MonitorEnumProc callback = (IntPtr hMonitor, IntPtr hdc, ref NativeMethods.Rect rect, IntPtr data) =>
        {
            monitors.Add(Describe(hMonitor));
            return true;
        };

        if (!NativeMethods.EnumDisplayMonitors(IntPtr.Zero, IntPtr.Zero, callback, IntPtr.Zero))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "EnumDisplayMonitors failed.");
        }

        return monitors;
    }

    /// <summary>
    /// Finds the monitor that currently contains the mouse cursor — the equivalent of Clicky's
    /// "primary focus screen". Returns <see langword="null"/> if the cursor cannot be resolved
    /// (for example if it moved off the enumerated set mid-call).
    /// </summary>
    internal static MonitorInfo? FindCursorMonitor(IReadOnlyList<MonitorInfo> monitors)
    {
        if (monitors.Count == 0 || !NativeMethods.GetCursorPos(out var point))
        {
            return null;
        }

        var handle = NativeMethods.MonitorFromPoint(point, NativeMethods.MONITOR_DEFAULTTONEAREST);

        foreach (var monitor in monitors)
        {
            if (monitor.Handle == handle)
            {
                return monitor;
            }
        }

        return null;
    }

    /// <summary>
    /// Orders monitors for capture: the cursor's monitor first (that is where the user is looking),
    /// then the primary display, then the rest in enumeration order.
    /// </summary>
    internal static IReadOnlyList<MonitorInfo> OrderForCapture(
        IReadOnlyList<MonitorInfo> monitors,
        MonitorInfo? cursorMonitor)
    {
        var ordered = new List<MonitorInfo>(monitors.Count);

        if (cursorMonitor is not null)
        {
            ordered.Add(cursorMonitor);
        }

        foreach (var monitor in monitors)
        {
            if (monitor.IsPrimary && !ordered.Contains(monitor))
            {
                ordered.Add(monitor);
            }
        }

        foreach (var monitor in monitors)
        {
            if (!ordered.Contains(monitor))
            {
                ordered.Add(monitor);
            }
        }

        return ordered;
    }

    private static MonitorInfo Describe(IntPtr hMonitor)
    {
        var info = new NativeMethods.MonitorInfoEx
        {
            Size = Marshal.SizeOf<NativeMethods.MonitorInfoEx>(),
            DeviceName = string.Empty,
        };

        if (!NativeMethods.GetMonitorInfo(hMonitor, ref info))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), "GetMonitorInfo failed.");
        }

        var rect = info.Monitor;

        return new MonitorInfo(
            hMonitor,
            info.DeviceName,
            new PixelRect(rect.Left, rect.Top, rect.Width, rect.Height),
            (info.Flags & NativeMethods.MONITORINFOF_PRIMARY) != 0);
    }
}
