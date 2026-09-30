using System.Runtime.InteropServices;
using HeyGanesh.ContextOrchestrator;
using HeyGanesh.ScreenCapture;
using HeyGanesh.ScreenOverlay;

namespace HeyGanesh.Tools.OverlaySmoke;

/// <summary>Per-monitor-v2 DPI awareness must be enabled by the host before any coordinate work (physical px).</summary>
internal static class NativeFns
{
    internal static readonly IntPtr PerMonitorV2 = new(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2

    [DllImport("user32.dll")]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr dpiContext);
}

/// <summary>
/// Verification host for the Screen Overlay. Its job is the window contract, not the verdicts: the
/// overlay must draw exactly what a canned <see cref="GuidanceResult"/> says for a bounded time,
/// pass every click through, never activate, exclude itself from capture, and remove itself on
/// timeout or cancellation. Every verdict is constructed offline — no capture, no keys, no network
/// (SO rows in docs/ai/TEST-CHECKLIST.md).
/// </summary>
internal static partial class OverlaySmokeHost
{
    /// <summary>
    /// Mirrors OverlayWindow.ClassName, which is internal to the module: the literal is repeated here
    /// on purpose, so if the class name ever drifts these checks fail loudly (zero windows found)
    /// instead of silently passing against nothing.
    /// </summary>
    private const string OverlayWindowClass = "HeyGanesh.ScreenOverlay.Window";

    private const int GwlExstyle = -20;
    private const uint WdaExcludeFromCapture = 0x00000011;
    private const uint ExpectedExtendedStyle =
        0x00080000 | // WS_EX_LAYERED
        0x00000020 | // WS_EX_TRANSPARENT
        0x08000000 | // WS_EX_NOACTIVATE
        0x00000008 | // WS_EX_TOPMOST
        0x00000080;  // WS_EX_TOOLWINDOW
    private const uint WmNcHitTest = 0x0084;
    private static readonly IntPtr HtTransparent = new(-1);

    // Window-contract Win32, declared here rather than imported from the module: the assertions must
    // observe what the OS actually sees, not what the module believes it set.
    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern IntPtr FindWindowEx(IntPtr parent, IntPtr after, string? className, string? title);

    [DllImport("user32.dll")]
    private static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    private static extern IntPtr SendMessage(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    [DllImport("user32.dll")]
    private static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint processId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetCurrentProcess();

    [DllImport("user32.dll")]
    private static extern uint GetGuiResources(IntPtr process, uint flags);

    /// <summary>All top-level overlay windows currently on screen (one per monitor the box touches).</summary>
    internal static List<IntPtr> FindOurWindows()
    {
        var found = new List<IntPtr>(1);
        var current = IntPtr.Zero;
        while ((current = FindWindowEx(IntPtr.Zero, current, OverlayWindowClass, null)) != IntPtr.Zero)
        {
            found.Add(current);
        }

        return found;
    }

    /// <summary>
    /// The top-level overlay windows owned by THIS process: what FindOurWindows filtered to our own
    /// PID. A lingering window from another process (an older crashed run, a concurrent smoke
    /// invocation) is environmental noise the runner must log, never an assertion input — every
    /// post-show assertion must use this, not the unfiltered list.
    /// </summary>
    internal static List<IntPtr> FindOwnWindows()
    {
        var own = new List<IntPtr>(1);
        var self = (uint)Environment.ProcessId;
        foreach (var hwnd in FindOurWindows())
        {
            _ = GetWindowThreadProcessId(hwnd, out var pid);
            if (pid == self)
            {
                own.Add(hwnd);
            }
        }

        return own;
    }

    internal static int GdiObjectCount() => (int)GetGuiResources(GetCurrentProcess(), 0);

    public static async Task<int> RunAsync(string[] args)
    {
        var options = ParseArgs(args);
        if (options.Help)
        {
            PrintUsage();
            return 0;
        }

        PrintHeader(options);

        using var captureService = new ScreenCaptureService();
        var monitors = captureService.EnumerateMonitors();
        if (monitors.Count == 0)
        {
            Console.WriteLine("RESULT: no connected displays; the overlay has nowhere to draw.");
            return 1;
        }

        foreach (var monitor in monitors)
        {
            Console.WriteLine($"  monitor   : {monitor}");
        }

        Console.WriteLine();

        var found = CannedFound(monitors);
        using var overlay = new ScreenOverlayService(
            new ScreenOverlayOptions { VisibleDuration = options.Duration },
            captureService);

        switch (options.Mode)
        {
            case RunMode.Found:
                return await RunFoundAsync(overlay, found, options);
            case RunMode.NoMatch:
                return await RunRefusalAsync(
                    overlay, CannedNoMatch(), OverlayShowResult.ReasonNoMatch,
                    "Found=false draws nothing and creates no window (SO-4)");
            case RunMode.Failed:
                return await RunRefusalAsync(
                    overlay, CannedFailed(), OverlayShowResult.ReasonFailure,
                    "a failed verdict draws nothing and creates no window (SO-5)");
            case RunMode.Cancel:
                return await RunCancelAsync(overlay, found, options);
            case RunMode.Burst:
                return await RunBurstAsync(overlay, found, options);
            case RunMode.ClickThrough:
                return await RunClickThroughAsync(overlay, found, options);
            case RunMode.Repeats:
                return await RunRepeatsAsync(overlay, found, options);
            default:
                PrintUsage();
                return 1;
        }
    }

    // ---- canned verdicts ----------------------------------------------------

    /// <summary>
    /// A found verdict with a box in the middle of the primary display: touches exactly one monitor,
    /// so WindowCount stays assertable on any topology, and the size is large enough to see.
    /// </summary>
    private static GuidanceResult CannedFound(IReadOnlyList<MonitorInfo> monitors)
    {
        var primary = monitors.FirstOrDefault(m => m.IsPrimary) ?? monitors[0];
        var box = new PixelRect(
            primary.Bounds.X + (primary.Bounds.Width / 4),
            primary.Bounds.Y + (primary.Bounds.Height / 4),
            Math.Max(120, primary.Bounds.Width / 6),
            Math.Max(80, primary.Bounds.Height / 6));

        return new GuidanceResult
        {
            Found = true,
            Description = "canned smoke target",
            BoxOnScreen = box,
            CenterOnScreen = new GuidancePoint(box.X + (box.Width / 2), box.Y + (box.Height / 2)),
            Source = GuidanceSource.ElementSelection,
            Confidence = GuidanceConfidence.High,
        };
    }

    /// <summary>The honest no-match shape: Found=false, choice "none", no failure reason (CO-4's verdict).</summary>
    private static GuidanceResult CannedNoMatch() => new()
    {
        Found = false,
        Choice = "none",
        Source = GuidanceSource.ElementSelection,
        Confidence = GuidanceConfidence.Low,
    };

    /// <summary>
    /// The CO-6 failure shape: Found=false WITH a FailureReason — the asymmetry the overlay must
    /// classify as "failure", not "no-match", which is why the service checks Failed first.
    /// </summary>
    private static GuidanceResult CannedFailed() => new()
    {
        Found = false,
        FailureReason = "synthetic failure for SO-5",
        Source = GuidanceSource.Vision,
        Confidence = GuidanceConfidence.Low,
    };
}