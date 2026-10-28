using System.ComponentModel;
using System.Runtime.InteropServices;
using HeyGanesh.Abstractions;

namespace HeyGanesh.ScreenCapture;

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
