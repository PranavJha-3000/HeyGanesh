using System.Diagnostics;
using HeyGanesh.ContextOrchestrator;
using HeyGanesh.ScreenOverlay;

namespace HeyGanesh.Tools.OverlaySmoke;

// Runners + result printing for the Screen Overlay smoke host. The exit code is part of the
// contract: 0 = the mode's expectation held, 2 = it failed, 13 = the show was cancelled exactly as
// requested (SO-6), 1 = unexpected error. Every runner also asserts no overlay window survives it.

internal static partial class OverlaySmokeHost
{
    private static async Task<int> RunFoundAsync(
        ScreenOverlayService overlay,
        GuidanceResult guidance,
        HostOptions options)
    {
        Console.WriteLine("showing the canned found box for the full duration...");
        var result = await overlay.ShowAsync(guidance);
        PrintResult(result, overlay);

        var pass = result.Displayed
            && result.Reason is null
            && result.WindowCount >= 1
            && result.ShownFor + TimeSpan.FromMilliseconds(200) >= options.Duration
            && !overlay.IsShowing
            && FindOwnWindows().Count == 0;

        return Finish(pass, "found verdict shows for the full duration, then removes itself (SO-3)");
    }

    private static async Task<int> RunRefusalAsync(
        ScreenOverlayService overlay,
        GuidanceResult guidance,
        string expectedReason,
        string label)
    {
        var result = await overlay.ShowAsync(guidance);
        PrintResult(result, overlay);

        // WindowCount == 0 and ShownFor == 0 are the load-bearing halves: a refusal must not have
        // created, shown, or kept up anything — FindOurWindows() proves it from the OS side too.
        var pass = !result.Displayed
            && result.Reason == expectedReason
            && result.WindowCount == 0
            && result.ShownFor == TimeSpan.Zero
            && !overlay.IsShowing
            && FindOwnWindows().Count == 0;

        return Finish(pass, label);
    }

    private static async Task<int> RunCancelAsync(
        ScreenOverlayService overlay,
        GuidanceResult guidance,
        HostOptions options)
    {
        using var cts = new CancellationTokenSource();
        cts.CancelAfter(options.CancelMs);
        Console.WriteLine($"cancelling the show token after {options.CancelMs} ms...");

        var result = await overlay.ShowAsync(guidance, cts.Token);
        PrintResult(result, overlay);

        var pass = !result.Displayed
            && result.Reason == OverlayShowResult.ReasonCancelled
            && !overlay.IsShowing
            && FindOwnWindows().Count == 0;

        Console.WriteLine(pass
            ? "expectation  : PASS cancellation removes the overlay cleanly (SO-6) -> exit 13"
            : "expectation  : FAIL cancellation did not cleanly remove the overlay (SO-6)");
        return pass ? 13 : 2;
    }

    private static async Task<int> RunBurstAsync(
        ScreenOverlayService overlay,
        GuidanceResult guidance,
        HostOptions options)
    {
        Console.WriteLine("burst mode: a second ShowAsync arrives while the first is still showing.");
        var first = overlay.ShowAsync(guidance);

        // Give the first show time to put its window up before the second call arrives.
        await Task.Delay(150);

        try
        {
            await overlay.ShowAsync(guidance);
            Console.WriteLine("FAIL: the second ShowAsync was accepted — one show at a time is not enforced.");
            return 2;
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"REFUSED -- {ex.Message}");
        }

        var result = await first;
        PrintResult(result, overlay);

        var pass = result.Displayed && !overlay.IsShowing && FindOwnWindows().Count == 0;
        return Finish(pass, "second show refused while the first runs; the first still completes (SO-8)");
    }

    private static async Task<int> RunClickThroughAsync(
        ScreenOverlayService overlay,
        GuidanceResult guidance,
        HostOptions options)
    {
        Console.WriteLine("showing, then asserting the window contract from the OS's point of view...");
        var showTask = overlay.ShowAsync(guidance);

        // Window creation happens on the pump thread just after ShowAsync returns; poll briefly.
        List<IntPtr> windows = [];
        for (var i = 0; i < 40 && windows.Count == 0; i++)
        {
            await Task.Delay(50);
            windows = FindOurWindows();
        }

        if (windows.Count == 0)
        {
            Console.WriteLine("FAIL: no overlay window found while a show is active.");
            _ = await showTask;
            return 2;
        }

        var failures = new List<string>();
        foreach (var hwnd in windows)
        {
            var style = unchecked((uint)GetWindowLong(hwnd, GwlExstyle));
            if ((style & ExpectedExtendedStyle) != ExpectedExtendedStyle)
            {
                failures.Add($"extended style 0x{style:X8} is missing required bits 0x{ExpectedExtendedStyle:X8}");
            }

            // The corrected click-through contract: WM_NCHITTEST must answer HTTRANSPARENT for every
            // point. WS_EX_TRANSPARENT alone is not sufficient for a top-level window (DEC-011).
            var hit = SendMessage(hwnd, WmNcHitTest, IntPtr.Zero, IntPtr.Zero);
            if (hit != HtTransparent)
            {
                failures.Add($"WM_NCHITTEST returned 0x{hit.ToInt64():X}, expected HTTRANSPARENT (-1)");
            }

            if (!GetWindowDisplayAffinity(hwnd, out var affinity) || affinity != WdaExcludeFromCapture)
            {
                failures.Add($"display affinity 0x{affinity:X} is not WDA_EXCLUDEFROMCAPTURE — capture exclusion missing (EX-5)");
            }

            if (GetForegroundWindow() == hwnd)
            {
                failures.Add("the overlay took the foreground — it must never activate");
            }
        }

        Console.WriteLine($"  inspected  : {windows.Count} overlay window(s), WM_NCHITTEST answered on each");

        await overlay.HideAsync();
        var result = await showTask;
        PrintResult(result, overlay);

        if (FindOwnWindows().Count != 0)
        {
            failures.Add("overlay window(s) still enumerable after HideAsync");
        }

        foreach (var failure in failures)
        {
            Console.WriteLine($"  FAIL       : {failure}");
        }

        return Finish(
            failures.Count == 0 && !result.Displayed,
            "click-through + no-activate + capture-excluded + removed on hide (SO-7, flips EX-5)");
    }

    private static async Task<int> RunRepeatsAsync(
        ScreenOverlayService overlay,
        GuidanceResult guidance,
        HostOptions options)
    {
        Console.WriteLine("warmup cycle (first pump/window setup is one-time cost)...");
        var warm = await overlay.ShowAsync(guidance);
        if (!warm.Displayed)
        {
            Console.WriteLine($"FAIL: warmup show did not display: {warm}");
            return 2;
        }

        var gdiBefore = GdiObjectCount();
        var handlesBefore = Process.GetCurrentProcess().HandleCount;

        for (var i = 1; i <= options.Repeats; i++)
        {
            var result = await overlay.ShowAsync(guidance);
            if (!result.Displayed)
            {
                Console.WriteLine($"FAIL: cycle {i}/{options.Repeats} did not display: {result}");
                return 2;
            }

            if (i % 5 == 0 || i == options.Repeats)
            {
                Console.WriteLine($"  cycle {i}/{options.Repeats} ok");
            }
        }

        var gdiAfter = GdiObjectCount();
        var handlesAfter = Process.GetCurrentProcess().HandleCount;
        Console.WriteLine($"  GDI objects : {gdiBefore} -> {gdiAfter} (delta {gdiAfter - gdiBefore})");
        Console.WriteLine($"  handles     : {handlesBefore} -> {handlesAfter} (delta {handlesAfter - handlesBefore})");
        Console.WriteLine($"  is showing  : {overlay.IsShowing}");

        // Small tolerances, not zero: GC finalization lag can release a brush late, and one or two
        // one-time allocations can land after the warmup. A leak shows up as growth proportional to
        // the cycle count, which these bounds catch.
        var pass = !overlay.IsShowing
            && (gdiAfter - gdiBefore) <= 4
            && (handlesAfter - handlesBefore) <= 8
            && FindOwnWindows().Count == 0;

        return Finish(pass, $"{options.Repeats} show/hide cycles leak no GDI objects or handles (SO-9)");
    }

    private static void PrintResult(OverlayShowResult result, ScreenOverlayService overlay)
    {
        Console.WriteLine($"  displayed  : {result.Displayed}");
        Console.WriteLine($"  reason     : {result.Reason ?? "-"}");
        if (result.Detail is not null)
        {
            Console.WriteLine($"  detail     : {result.Detail}");
        }

        Console.WriteLine($"  windows    : {result.WindowCount}");
        Console.WriteLine(
            $"  shown for  : {result.ShownFor.TotalMilliseconds:F0} ms " +
            $"(duration {overlay.VisibleDuration.TotalMilliseconds:F0} ms)");
        Console.WriteLine($"  is showing : {overlay.IsShowing}");

        var ownLingering = FindOwnWindows().Count;
        if (ownLingering > 0)
        {
            Console.WriteLine($"  WARN       : {ownLingering} overlay window(s) from this process still enumerable after the show ended");
        }

        var foreignLingering = FindOurWindows().Count - ownLingering;
        if (foreignLingering > 0)
        {
            Console.WriteLine($"  note       : {foreignLingering} overlay window(s) from other processes are also on screen (ignored — not ours)");
        }
    }

    private static int Finish(bool pass, string label)
    {
        Console.WriteLine($"expectation  : {(pass ? "PASS" : "FAIL")} {label}");
        return pass ? 0 : 2;
    }
}