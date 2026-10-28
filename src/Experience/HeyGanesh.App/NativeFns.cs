using System.Runtime.InteropServices;

namespace HeyGanesh.App;

/// <summary>
/// The only Win32 the shell itself needs: set the process's DPI awareness, and read back what the
/// process actually got.
/// </summary>
/// <remarks>
/// <para>
/// Awareness is process-global and irreversible, so it belongs to the host and to no module —
/// DEC-004 says the capture module must not set it, and the Screen Overlay checks it and "never sets
/// it (DEC-004 keeps that the host's decision)". Both are right: this assembly is the one thing in the
/// tree allowed to decide it, and every coordinate the pipeline produces (UIA rectangles from Element
/// Selection, back-mapped boxes from Vision, <c>MonitorInfo.Bounds</c> from Screen Capture) is only
/// mutually comparable if the answer is PerMonitorV2.
/// </para>
/// <para>
/// The read-back matters because <see cref="SetProcessDpiAwarenessContext"/> returns false both when
/// the call is refused and when awareness was already set to the same value — the two are
/// indistinguishable from the return alone, and a shell that prints "aware" on the strength of a bool
/// would be guessing. Probing the live thread context is what the overlay's own gate compares
/// against, so the shell reports the same fact the overlay will act on rather than a proxy for it.
/// </para>
/// </remarks>
internal static class NativeFns
{
    /// <summary>DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2 — the only context this host requests.</summary>
    internal static readonly IntPtr PerMonitorV2 = new(-4);

    /// <summary>DPI_AWARENESS_PER_MONITOR_AWARE — the floor the Screen Overlay's gate accepts.</summary>
    private const int PerMonitorAware = 2;

    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern IntPtr GetThreadDpiAwarenessContext();

    [DllImport("user32.dll")]
    private static extern int GetAwarenessFromDpiAwarenessContext(IntPtr dpiContext);

    [DllImport("user32.dll")]
    private static extern int GetSystemMetrics(int index);

    private const int SmXVirtualScreen = 76, SmYVirtualScreen = 77, SmCxVirtualScreen = 78, SmCyVirtualScreen = 79;

    /// <summary>
    /// The virtual desktop in physical pixels: the outer bound the lesson validator holds every mark to.
    /// Physical only because this process is PerMonitorV2 (set in Program.cs); under --no-dpi the numbers
    /// are virtualised, and the lesson's marks then go to an overlay that refuses to draw anyway.
    /// </summary>
    internal static HeyGanesh.Abstractions.PixelRect VirtualDesktop() => new(
        GetSystemMetrics(SmXVirtualScreen),
        GetSystemMetrics(SmYVirtualScreen),
        GetSystemMetrics(SmCxVirtualScreen),
        GetSystemMetrics(SmCyVirtualScreen));

    /// <summary>
    /// True when the calling thread is at least per-monitor aware. Same probe, same floor, same
    /// "say aware when the OS cannot answer" bias as <c>ScreenOverlayService.IsPerMonitorDpiAware</c>,
    /// so the shell's startup line and the overlay's later refusal cannot disagree.
    /// </summary>
    internal static bool IsCurrentThreadPerMonitorAware()
    {
        try
        {
            return GetAwarenessFromDpiAwarenessContext(GetThreadDpiAwarenessContext()) >= PerMonitorAware;
        }
        catch (EntryPointNotFoundException)
        {
            return true;
        }
        catch (DllNotFoundException)
        {
            return true;
        }
    }
}
