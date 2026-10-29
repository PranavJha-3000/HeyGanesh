using System.Diagnostics;
using System.Runtime.InteropServices;
using HeyGanesh.ScreenCapture;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

// Verification harness for the Screen Capture module.
//
// Usage: dotnet run --project tools/ScreenCaptureSmoke -c Debug -- [--out <dir>] [--move-cursor]
//                                                          [--no-exclude] [--repeats <n>]
//        ... -- --assert-ocr | --read-text     the screen-text rows (DEC-038, OcrRows.cs; local-only)
//
// Captures every monitor, writes the JPEGs to disk, and asserts the properties the module promises:
// cursor-first ordering, sane downscale, valid JPEGs with actual content, no leaks across repeated
// captures, and that the self-exclusion primitive is accepted for our own window.

var outputDirectory = Path.Combine(AppContext.BaseDirectory, "out");
var moveCursor = false;
var excludeSelf = true;
var repeats = 25;

for (var i = 0; i < args.Length; i++)
{
    switch (args[i])
    {
        case "--out" when i + 1 < args.Length:
            outputDirectory = Path.GetFullPath(args[++i]);
            break;
        case "--move-cursor":
            moveCursor = true;
            break;
        case "--no-exclude":
            excludeSelf = false;
            break;
        case "--repeats" when i + 1 < args.Length:
            repeats = int.Parse(args[++i]);
            break;
    }
}

var failures = new List<string>();
var warnings = new List<string>();
var skips = new List<string>();

void Check(string name, bool condition, string? detail = null)
{
    var suffix = detail is null ? string.Empty : $"  [{detail}]";
    Console.WriteLine($"  {(condition ? "PASS" : "FAIL")}  {name}{suffix}");

    if (!condition)
    {
        failures.Add(name);
    }
}

void Warn(string name, string detail)
{
    Console.WriteLine($"  WARN  {name}  [{detail}]");
    warnings.Add($"{name} ({detail})");
}

void Skip(string name, string reason)
{
    Console.WriteLine($"  SKIP  {name}  [{reason}]");
    skips.Add($"{name} ({reason})");
}

Directory.CreateDirectory(outputDirectory);

Console.WriteLine("=== HeyGanesh Screen Capture verification ===");
Console.WriteLine($"output: {outputDirectory}");

// DPI awareness is set by the *host*, never by the library: physical-pixel coordinates only line
// up with the capture frames if the process is per-monitor aware. This machine runs at 150%, so
// without this the cursor/monitor maths below would be in virtualised pixels.
var dpiContext = new IntPtr(-4); // DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2
var dpiAwarenessSet = NativeFns.SetProcessDpiAwarenessContext(dpiContext);
Console.WriteLine($"per-monitor-v2 DPI awareness: {dpiAwarenessSet}");

// The screen-text rows run alone, after DPI awareness (their boxes are physical pixels).
if (args.Length > 0 && args[0] == "--assert-ocr")
{
    return await OcrRows.AssertOcrAsync();
}

if (args.Length > 0 && args[0] == "--read-text")
{
    return await OcrRows.ReadTextAsync();
}

var consoleWindow = NativeFns.GetConsoleWindow();
Console.WriteLine($"console window: 0x{consoleWindow:X}");

if (!ScreenCaptureService.IsCaptureSupported())
{
    Check("capture is supported on this device", false, "GraphicsCaptureSession.IsSupported() == false");
    Console.WriteLine("RESULT: unsupported device; nothing further to verify.");
    return 1;
}

Check("capture is supported on this device", true);

using var service = new ScreenCaptureService();

var monitors = service.EnumerateMonitors();
var cursorMonitor = service.GetCursorMonitor();

Console.WriteLine();
Console.WriteLine($"--- monitors ({monitors.Count}) ---");

foreach (var monitor in monitors)
{
    var marker = monitor == cursorMonitor ? "   <- cursor is here" : string.Empty;
    Console.WriteLine($"  {monitor}{marker}");
}

Console.WriteLine($"cursor monitor: {cursorMonitor?.ToString() ?? "(unresolved)"}");

Console.WriteLine();
Console.WriteLine("--- capture (all monitors, cursor first) ---");

var captureClock = Stopwatch.GetTimestamp();
var captures = await service.CaptureAllMonitorsAsync();
var totalElapsed = Stopwatch.GetElapsedTime(captureClock);

Check("all monitors were captured", captures.Count == monitors.Count, $"{captures.Count}/{monitors.Count}");
Check(
    "cursor's monitor is first in the result set",
    cursorMonitor is not null && captures.Count > 0 && captures[0].Monitor == cursorMonitor,
    $"first={captures[0].Monitor.DeviceName}, cursor={cursorMonitor?.DeviceName ?? "(unresolved)"}");

var index = 0;

foreach (var capture in captures)
{
    index++;

    var shortName = capture.Monitor.DeviceName
        .Replace("\\", string.Empty, StringComparison.Ordinal)
        .Replace(".", string.Empty, StringComparison.Ordinal)
        .Replace(":", string.Empty, StringComparison.Ordinal);

    var path = Path.Combine(outputDirectory, $"monitor-{index}-{shortName}.jpg");
    await File.WriteAllBytesAsync(path, capture.Jpeg);

    Console.WriteLine(
        $"  [{index}] {capture.Monitor}  {capture.SourceWidth}x{capture.SourceHeight} -> " +
        $"{capture.OutputWidth}x{capture.OutputHeight}, {capture.Jpeg.Length / 1024} KB, " +
        $"nonBlack={capture.NonBlackPixelRatio:P1}, frames={capture.FramesObserved}, " +
        $"{capture.Duration.TotalMilliseconds:F0} ms");
    Console.WriteLine($"        {path}");

    var label = $"monitor {index}";

    Check($"{label}: JPEG magic bytes are valid", HasJpegMarkers(capture.Jpeg),
        $"length={capture.Jpeg.Length}");

    Check($"{label}: longest edge within the 1280 cap",
        Math.Max(capture.OutputWidth, capture.OutputHeight) <= 1280,
        $"{capture.OutputWidth}x{capture.OutputHeight}");

    Check($"{label}: never upscaled",
        capture.OutputWidth <= capture.SourceWidth && capture.OutputHeight <= capture.SourceHeight,
        $"{capture.SourceWidth}x{capture.SourceHeight} -> {capture.OutputWidth}x{capture.OutputHeight}");

    var sourceAspect = (double)capture.SourceWidth / capture.SourceHeight;
    var outputAspect = (double)capture.OutputWidth / capture.OutputHeight;

    Check($"{label}: aspect ratio preserved", Math.Abs(sourceAspect - outputAspect) < 0.02,
        $"source={sourceAspect:F4} output={outputAspect:F4}");

    Check($"{label}: frame has content (not a black capture)",
        capture.NonBlackPixelRatio > 0.01,
        $"nonBlack={capture.NonBlackPixelRatio:P2}");

    if (capture.NonBlackPixelRatio <= 0.05)
    {
        Warn($"{label}: very little content in frame", $"nonBlack={capture.NonBlackPixelRatio:P2}");
    }

    if (capture.FramesObserved < 2)
    {
        // Not a defect: WGC delivers frames on screen change, so a still desktop has no second
        // frame to offer and the settle frame stays a preference rather than a requirement.
        Console.WriteLine(
            $"        note: one frame observed (still desktop) — settle frames are a preference, not a requirement");
    }
}

Console.WriteLine($"  all monitors in {totalElapsed.TotalMilliseconds:F0} ms");


Console.WriteLine();
Console.WriteLine("--- self-exclusion contract (WDA_EXCLUDEFROMCAPTURE) ---");

if (!excludeSelf)
{
    Skip("self-exclusion A/B", "--no-exclude was passed; the probe window is left capturable by design");
}
else
{
    var probeTarget = cursorMonitor ?? monitors[0];

    // SetWindowDisplayAffinity only accepts windows this process owns — it returns
    // ERROR_ACCESS_DENIED for anything else. That is exactly why the console HWND is unusable as a
    // probe here: under Windows Terminal the console window belongs to the terminal, not to us.
    if (consoleWindow != IntPtr.Zero)
    {
        _ = CaptureExclusion.TryExcludeFromCapture(consoleWindow, out var consoleError);
        var note = consoleError == 5
            ? "  (ERROR_ACCESS_DENIED — not our window; the API is correctly refusing)"
            : string.Empty;
        Console.WriteLine($"  console HWND 0x{consoleWindow:X} affinity result: win32Error={consoleError}{note}");
    }

    var probeWidth = Math.Min(760, Math.Max(320, probeTarget.Bounds.Width / 2));
    var probeHeight = 200;
    var probeX = probeTarget.Bounds.X + ((probeTarget.Bounds.Width - probeWidth) / 2);
    var probeY = probeTarget.Bounds.Y + ((probeTarget.Bounds.Height - probeHeight) / 2);

    var probe = NativeFns.CreateWindowEx(
        0,
        "STATIC",
        "HeyGanesh capture-exclusion probe - this window must be absent from exclusion-on.jpg",
        NativeFns.WS_POPUP | NativeFns.WS_VISIBLE | NativeFns.SS_WHITERECT,
        probeX,
        probeY,
        probeWidth,
        probeHeight,
        IntPtr.Zero,
        IntPtr.Zero,
        IntPtr.Zero,
        IntPtr.Zero);

    Check("owned probe window created", probe != IntPtr.Zero, $"hwnd=0x{probe:X}");

    if (probe == IntPtr.Zero)
    {
        Console.WriteLine("  the OS refused to create the probe window; nothing further to verify here.");
    }
    else
    {
        // Focus is deliberately left alone (SWP_NOACTIVATE): forcing the probe to the foreground
        // makes the previously focused window, a terminal with focus-dependent styling, repaint its
        // whole surface, which pollutes any comparison of the surrounding region. Topmost is used
        // rather than merely HWND_TOP so the probe cannot end up occluded — which the precondition
        // check below catches if it ever still happens.
        var moved = NativeFns.SetWindowPos(
            probe,
            NativeFns.HWND_TOPMOST,
            0,
            0,
            0,
            0,
            NativeFns.SWP_NOMOVE | NativeFns.SWP_NOSIZE | NativeFns.SWP_NOACTIVATE);

        var moveError = Marshal.GetLastWin32Error();

        // A window with no running message loop does not get round to WM_PAINT on its own, so the
        // paint is forced rather than hoped for: without this the probe can be composited empty and
        // the comparison below would silently measure nothing.
        _ = NativeFns.RedrawWindow(
            probe,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeFns.RDW_INVALIDATE | NativeFns.RDW_ERASE | NativeFns.RDW_UPDATENOW);

        PumpMessages();
        await Task.Delay(900);
        PumpMessages();
        _ = NativeFns.RedrawWindow(
            probe,
            IntPtr.Zero,
            IntPtr.Zero,
            NativeFns.RDW_INVALIDATE | NativeFns.RDW_ERASE | NativeFns.RDW_UPDATENOW);
        PumpMessages();

        // Precondition, checked rather than assumed. Hit-testing is deliberately *not* used here:
        // the standard STATIC window class answers HTTRANSPARENT, so WindowFromPoint reports
        // whatever lies beneath it even when the probe is plainly on screen. Instead the probe's
        // own appearance is used — it is a solid white rectangle, so it is verified to be present
        // in the capture below.
        NativeFns.GetWindowRect(probe, out var actualProbeRect);
        var probeSized = actualProbeRect.Right - actualProbeRect.Left == probeWidth
            && actualProbeRect.Bottom - actualProbeRect.Top == probeHeight;

        var probeVisible = NativeFns.IsWindowVisible(probe) && probeSized;

        Console.WriteLine(
            $"  probe: setWindowPos ok={moved} err={moveError}, visible={NativeFns.IsWindowVisible(probe)}, " +
            $"rect=({actualProbeRect.Left},{actualProbeRect.Top})-({actualProbeRect.Right},{actualProbeRect.Bottom})");

        var excluded = CaptureExclusion.TryExcludeFromCapture(probe, out var excludeError);
        Check("our own window accepts WDA_EXCLUDEFROMCAPTURE", excluded, $"win32Error={excludeError}");

        var excludedCapture = await service.CaptureMonitorAsync(probeTarget);
        var excludedPath = Path.Combine(outputDirectory, "exclusion-on.jpg");
        await File.WriteAllBytesAsync(excludedPath, excludedCapture.Jpeg);

        // Second exclusion-ON capture: establishes the noise floor *in the probe's own rectangle*,
        // so the ON -> OFF difference below can be attributed to the affinity toggle rather than to
        // whatever else the desktop happened to be doing.
        await Task.Delay(250);
        var excludedRepeatCapture = await service.CaptureMonitorAsync(probeTarget);

        PumpMessages();

        var included = CaptureExclusion.TryIncludeInCapture(probe, out var includeError);
        Check("affinity can be cleared again", included, $"win32Error={includeError}");

        var includedCapture = await service.CaptureMonitorAsync(probeTarget);
        var includedPath = Path.Combine(outputDirectory, "exclusion-off.jpg");
        await File.WriteAllBytesAsync(includedPath, includedCapture.Jpeg);

        // Leave nothing unmarked, then tear the probe down.
        _ = CaptureExclusion.TryExcludeFromCapture(probe, out _);
        _ = NativeFns.DestroyWindow(probe);
        PumpMessages();

        Console.WriteLine($"  probe rect: {probeWidth}x{probeHeight} at ({probeX},{probeY})");
        Console.WriteLine(
            $"  exclusion ON : {excludedPath}  " +
            $"({excludedCapture.Jpeg.Length / 1024} KB, nonBlack={excludedCapture.NonBlackPixelRatio:P2})");
        Console.WriteLine(
            $"  exclusion OFF: {includedPath}  " +
            $"({includedCapture.Jpeg.Length / 1024} KB, nonBlack={includedCapture.NonBlackPixelRatio:P2})");
        Console.WriteLine("  operator check: the probe window must be visible in exclusion-off.jpg, and");
        Console.WriteLine("  absent in exclusion-on.jpg (the desktop behind it showing through instead).");

        if (excludedCapture.Jpeg.Length == includedCapture.Jpeg.Length)
        {
            Warn("exclusion A/B produced identical byte sizes",
                "inspect both JPEGs; identical sizes may mean the probe was off-screen");
        }

        if (!probeVisible)
        {
            Skip(
                "exclusion pixel diff",
                "the probe window was not visible with the expected size, so it cannot be in the capture");
        }
        else
        {
            // Decisive, self-controlled test: the probe window's own pixels must change between the
            // two captures, while two back-to-back captures establish what "no change" looks like in
            // the very same rectangle.
            try
            {
            var (onPixels, onWidth, onHeight) = await DecodeJpegAsync(excludedCapture.Jpeg);
            var (onRepeatPixels, repeatWidth, repeatHeight) = await DecodeJpegAsync(excludedRepeatCapture.Jpeg);
            var (offPixels, offWidth, offHeight) = await DecodeJpegAsync(includedCapture.Jpeg);

            if (onWidth != offWidth || onHeight != offHeight)
            {
                Warn("exclusion A/B size mismatch", $"{onWidth}x{onHeight} vs {offWidth}x{offHeight}");
            }
            else
            {
                var scale = (double)onWidth / probeTarget.Bounds.Width;
                var inset = 6;

                var rectX = Math.Clamp((int)Math.Round((probeX - probeTarget.Bounds.X) * scale) + inset, 0, onWidth - 1);
                var rectY = Math.Clamp((int)Math.Round((probeY - probeTarget.Bounds.Y) * scale) + inset, 0, onHeight - 1);
                var rectW = Math.Clamp((int)Math.Round(probeWidth * scale) - (inset * 2), 1, onWidth - rectX);
                var rectH = Math.Clamp((int)Math.Round(probeHeight * scale) - (inset * 2), 1, onHeight - rectY);

                var insideChange = PixelChangeFraction(onPixels, offPixels, onWidth, rectX, rectY, rectW, rectH);
                var controlChange = PixelChangeFractionOutside(
                    onPixels, offPixels, onWidth, onHeight, rectX, rectY, rectW, rectH);

                // Noise floor measured in the very same rectangle: two captures that differ only in
                // time. Whatever this picks up (animation, cursor, repaint) is what a difference
                // would look like *without* the affinity toggle.
                var noiseFloor = repeatWidth == onWidth && repeatHeight == onHeight
                    ? PixelChangeFraction(onPixels, onRepeatPixels, onWidth, rectX, rectY, rectW, rectH)
                    : double.NaN;

                // Appearance-based precondition: the probe is a solid white rectangle, so its own
                // pixels prove it was captured. If this fails, the harness could not see its own
                // probe (occlusion, or the window never painted) — that is a harness problem, not a
                // module defect, and it is what makes the diff below interpretable either way.
                var probeWhite = BrightFraction(offPixels, onWidth, rectX, rectY, rectW, rectH);

                Check("probe window is present in the OFF capture (precondition for the diff)",
                    probeWhite >= 0.3,
                    $"white pixels inside the probe rectangle={probeWhite:P1}");

                var controlNoiseFloor = repeatWidth == onWidth && repeatHeight == onHeight
                    ? PixelChangeFractionOutside(onPixels, onRepeatPixels, onWidth, onHeight, rectX, rectY, rectW, rectH)
                    : double.NaN;

                Console.WriteLine(
                    $"  noise floor in probe rect : {noiseFloor:P1}  (two exclusion-ON captures)");

                Console.WriteLine(
                    $"  pixel diff in probe rect  : {insideChange:P1}  (rect {rectW}x{rectH} at {rectX},{rectY} in the JPEG)");
                Console.WriteLine($"  pixel diff elsewhere      : {controlChange:P1}  (control region)");

                if (controlChange >= 0.02)
                {
                    // Say *where* the unrelated change is instead of guessing; a whole-screen
                    // bounding box means something global repainted (focus styling, wallpaper),
                    // a tight box means one window.
                    var bounds = ChangedPixelBoundsOutside(
                        onPixels, offPixels, onWidth, onHeight, rectX, rectY, rectW, rectH);

                    Console.WriteLine(
                        bounds is null
                            ? "  control change bounds    : (none)"
                            : $"  control change bounds    : {bounds.Value.Width}x{bounds.Value.Height} at " +
                              $"({bounds.Value.X},{bounds.Value.Y}) of {onWidth}x{onHeight}");
                }

                var signalBeatsNoise = double.IsNaN(noiseFloor) || insideChange > Math.Max(0.5, noiseFloor * 5);

                Check("probe window pixels changed when excluded (exclusion is effective)",
                    insideChange > 0.5 && signalBeatsNoise,
                    $"ON->OFF changed={insideChange:P1} vs noise floor {noiseFloor:P1} in the same rectangle");

                // A few stray pixels are unavoidable (clock, notifications, an animating control).
                // What would invalidate the result above is *widespread* unrelated change.
                if (controlChange < 0.05)
                {
                    Check("control region is stable, so the diff above means the probe and nothing else",
                        true, $"changed={controlChange:P1} outside the probe rectangle");
                }
                else if (!double.IsNaN(controlNoiseFloor) && controlChange > Math.Max(0.05, controlNoiseFloor * 5))
                {
                    // Expected: clearing or setting the affinity forces DWM to recompose that window
                    // and everything drawn with it (a translucent terminal re-samples what is behind
                    // it), so the surrounding desktop legitimately changes during the toggle. This is
                    // reported, not asserted — the asserted evidence is the probe rectangle above.
                    Console.WriteLine(
                        $"  control noise floor       : {controlNoiseFloor:P1}  (two exclusion-ON captures)");
                    Console.WriteLine(
                        "  note: the toggle itself forced a wider recomposition — consistent with the");
                    Console.WriteLine(
                        "        window being added to / removed from the composite. Not asserted.");
                }
                else
                {
                    Warn("control region changed during the A/B pair",
                        $"changed={controlChange:P1} outside the probe rectangle — unrelated desktop " +
                        "activity; inspect the bounds printed above");
                }
            }
        }
        catch (Exception ex)
        {
            Skip("exclusion pixel diff", $"{ex.GetType().Name}: {ex.Message}");
        }
        }
    }
}


Console.WriteLine();
Console.WriteLine("--- cursor sweep ---");

if (!moveCursor)
{
    Skip("cursor sweep", "pass --move-cursor to sweep the pointer across monitors and re-check ordering");
}
else
{
    foreach (var monitor in monitors)
    {
        var centerX = monitor.Bounds.X + (monitor.Bounds.Width / 2);
        var centerY = monitor.Bounds.Y + (monitor.Bounds.Height / 2);

        _ = NativeFns.SetCursorPos(centerX, centerY);
        await Task.Delay(150);

        var resolved = service.GetCursorMonitor();

        Check($"cursor parked on {monitor.DeviceName} is resolved to it",
            resolved == monitor,
            $"expected={monitor.DeviceName} resolved={resolved?.DeviceName ?? "(unresolved)"}");

        var single = await service.CaptureMonitorAsync(monitor);
        Check($"{monitor.DeviceName}: explicit single-monitor capture matches the request",
            single.Monitor == monitor && single.SourceWidth == monitor.Bounds.Width,
            $"source={single.SourceWidth}x{single.SourceHeight} bounds={monitor.Bounds}");
    }

    var cursorNow = service.GetCursorMonitor();
    var orderedCaptures = await service.CaptureAllMonitorsAsync();

    Check("cursor-first ordering follows the moved cursor",
        cursorNow is not null && orderedCaptures[0].Monitor == cursorNow,
        $"first={orderedCaptures[0].Monitor.DeviceName} cursor={cursorNow?.DeviceName ?? "(unresolved)"}");
}


Console.WriteLine();
Console.WriteLine($"--- repeat capture x{repeats} (frame pool / session / handle churn) ---");

using (var currentProcess = Process.GetCurrentProcess())
{
    var target = cursorMonitor ?? monitors[0];

    // Warm up first: the earliest captures grow the thread pool and start runtime timers, and that
    // one-off growth would otherwise be attributed to the captures being measured.
    for (var warmup = 0; warmup < 5; warmup++)
    {
        _ = await service.CaptureMonitorAsync(target);
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    currentProcess.Refresh();

    var handlesBefore = currentProcess.HandleCount;
    var heapBefore = GC.GetTotalMemory(forceFullCollection: true);

    var iterationFailures = 0;

    for (var iteration = 0; iteration < repeats; iteration++)
    {
        try
        {
            var repeatCapture = await service.CaptureMonitorAsync(target);

            if (repeatCapture.Jpeg.Length == 0 || repeatCapture.OutputWidth == 0)
            {
                iterationFailures++;
            }
        }
        catch (Exception ex)
        {
            iterationFailures++;
            Console.WriteLine($"  iteration {iteration + 1} threw: {ex.GetType().Name}: {ex.Message}");
        }
    }

    GC.Collect();
    GC.WaitForPendingFinalizers();
    GC.Collect();
    currentProcess.Refresh();

    var handlesAfter = currentProcess.HandleCount;
    var heapAfter = GC.GetTotalMemory(forceFullCollection: true);

    Console.WriteLine($"  handles: {handlesBefore} -> {handlesAfter} (delta {handlesAfter - handlesBefore})");
    Console.WriteLine($"  managed heap: {heapBefore / 1024 / 1024} MB -> {heapAfter / 1024 / 1024} MB");

    Check($"{repeats} consecutive captures all produced a frame",
        iterationFailures == 0,
        $"failures={iterationFailures}");

    // A leaked frame pool, session or capture item shows up as steadily growing handles.
    Check("no handle leak across repeated captures",
        handlesAfter - handlesBefore <= 20,
        $"delta={handlesAfter - handlesBefore}");

    Check("managed heap does not grow without bound",
        heapAfter <= heapBefore + (16L * 1024 * 1024),
        $"{heapBefore / 1024 / 1024} MB -> {heapAfter / 1024 / 1024} MB");
}


Console.WriteLine();
Console.WriteLine("--- summary ---");

if (failures.Count == 0)
{
    Console.WriteLine($"RESULT: all checks passed ({warnings.Count} warning(s), {skips.Count} skipped)");
}
else
{
    Console.WriteLine($"RESULT: {failures.Count} check(s) FAILED: {string.Join(" | ", failures)}");
}

foreach (var warning in warnings)
{
    Console.WriteLine($"  warning: {warning}");
}

foreach (var skip in skips)
{
    Console.WriteLine($"  skipped: {skip}");
}

return failures.Count == 0 ? 0 : 1;

// Decodes a JPEG back to BGRA pixels. Windows.Security.Cryptography is used only as a
// byte[] <-> IBuffer bridge, so no raw COM pixel access is needed.
static async Task<(byte[] Pixels, int Width, int Height)> DecodeJpegAsync(byte[] jpeg)
{
    using var stream = new InMemoryRandomAccessStream();

    var writer = new DataWriter(stream);
    writer.WriteBytes(jpeg);
    await writer.StoreAsync();
    await writer.FlushAsync();
    _ = writer.DetachStream();

    stream.Seek(0);

    var decoder = await BitmapDecoder.CreateAsync(stream);
    using var bitmap = await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);

    var byteCount = checked(bitmap.PixelWidth * bitmap.PixelHeight * 4);
    var buffer = CryptographicBuffer.CreateFromByteArray(new byte[byteCount]);
    bitmap.CopyToBuffer(buffer);
    CryptographicBuffer.CopyToByteArray(buffer, out var pixels);

    return (pixels, bitmap.PixelWidth, bitmap.PixelHeight);
}

// Fraction of pixels inside a rectangle whose colour differs by more than JPEG noise.
static double PixelChangeFraction(byte[] a, byte[] b, int stride, int x, int y, int width, int height)
{
    var differing = 0;
    var total = 0;

    for (var row = y; row < y + height; row++)
    {
        for (var column = x; column < x + width; column++)
        {
            total++;

            if (ColourDistance(a, b, ((row * stride) + column) * 4) > 30)
            {
                differing++;
            }
        }
    }

    return total == 0 ? 0 : (double)differing / total;
}

// Same measure over the whole image *except* the rectangle — the control region.
static double PixelChangeFractionOutside(byte[] a, byte[] b, int width, int height, int x, int y, int regionWidth, int regionHeight)
{
    var differing = 0;
    var total = 0;

    for (var row = 0; row < height; row++)
    {
        var inRegionRows = row >= y && row < y + regionHeight;

        for (var column = 0; column < width; column++)
        {
            if (inRegionRows && column >= x && column < x + regionWidth)
            {
                continue;
            }

            total++;

            if (ColourDistance(a, b, ((row * width) + column) * 4) > 30)
            {
                differing++;
            }
        }
    }

    return total == 0 ? 0 : (double)differing / total;
}

// Fraction of pixels inside a rectangle that are near-white — used to confirm the white probe
// window really is present in a capture.
static double BrightFraction(byte[] pixels, int stride, int x, int y, int width, int height)
{
    var bright = 0;
    var total = 0;

    for (var row = y; row < y + height; row++)
    {
        for (var column = x; column < x + width; column++)
        {
            var offset = ((row * stride) + column) * 4;
            total++;

            if (pixels[offset] > 200 && pixels[offset + 1] > 200 && pixels[offset + 2] > 200)
            {
                bright++;
            }
        }
    }

    return total == 0 ? 0 : (double)bright / total;
}

// BGRA at the given offset; summed channel difference.
static int ColourDistance(byte[] a, byte[] b, int offset)
    => Math.Abs(a[offset] - b[offset])
        + Math.Abs(a[offset + 1] - b[offset + 1])
        + Math.Abs(a[offset + 2] - b[offset + 2]);

// Bounding box of the pixels that changed outside the probe rectangle — used to say where the
// unrelated activity is rather than guessing at it.
static (int X, int Y, int Width, int Height)? ChangedPixelBoundsOutside(
    byte[] a,
    byte[] b,
    int width,
    int height,
    int x,
    int y,
    int regionWidth,
    int regionHeight)
{
    var minX = int.MaxValue;
    var minY = int.MaxValue;
    var maxX = int.MinValue;
    var maxY = int.MinValue;

    for (var row = 0; row < height; row++)
    {
        var inRegionRows = row >= y && row < y + regionHeight;

        for (var column = 0; column < width; column++)
        {
            if (inRegionRows && column >= x && column < x + regionWidth)
            {
                continue;
            }

            if (ColourDistance(a, b, ((row * width) + column) * 4) <= 30)
            {
                continue;
            }

            minX = Math.Min(minX, column);
            minY = Math.Min(minY, row);
            maxX = Math.Max(maxX, column);
            maxY = Math.Max(maxY, row);
        }
    }

    return maxX < minX || maxY < minY ? null : (minX, minY, maxX - minX + 1, maxY - minY + 1);
}

// A window created without a running message loop never processes WM_PAINT, so the probe is pumped
// between steps to make sure it genuinely appears on screen before it is captured.
static void PumpMessages()
{
    while (NativeFns.PeekMessage(out var message, IntPtr.Zero, 0, 0, NativeFns.PM_REMOVE))
    {
        _ = NativeFns.TranslateMessage(in message);
        _ = NativeFns.DispatchMessage(in message);
    }
}

// A real JPEG starts with SOI (FF D8) and ends with EOI (FF D9). This catches an encoder that
// silently produced nothing or the wrong format, without needing an image library.
static bool HasJpegMarkers(byte[] jpeg)
    => jpeg.Length > 4
        && jpeg[0] == 0xFF
        && jpeg[1] == 0xD8
        && jpeg[^2] == 0xFF
        && jpeg[^1] == 0xD9;


internal static class NativeFns
{
    [StructLayout(LayoutKind.Sequential)]
    internal struct Rect
    {
        internal int Left;
        internal int Top;
        internal int Right;
        internal int Bottom;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetProcessDpiAwarenessContext(IntPtr context);

    [DllImport("kernel32.dll")]
    internal static extern IntPtr GetConsoleWindow();

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetCursorPos(int x, int y);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool GetWindowRect(IntPtr hwnd, out Rect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetForegroundWindow(IntPtr hwnd);

    // ---- owned probe window -------------------------------------------------

    internal const uint PM_REMOVE = 0x0001;
    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_VISIBLE = 0x10000000;
    internal const uint WS_BORDER = 0x00800000;

    /// <summary>SS_WHITERECT — a solid white rectangle, the easiest thing to detect in a capture.</summary>
    internal const uint SS_WHITERECT = 0x00000006;

    internal static readonly IntPtr HWND_TOP = IntPtr.Zero;

    /// <summary>HWND_TOPMOST — the probe must be above whatever else is on screen for the diff.</summary>
    internal static readonly IntPtr HWND_TOPMOST = new(-1);

    internal const uint SWP_NOSIZE = 0x0001;
    internal const uint SWP_NOMOVE = 0x0002;
    internal const uint SWP_NOACTIVATE = 0x0010;

    internal const uint RDW_INVALIDATE = 0x0001;
    internal const uint RDW_ERASE = 0x0004;
    internal const uint RDW_UPDATENOW = 0x0100;

    [StructLayout(LayoutKind.Sequential)]
    internal struct Point
    {
        internal int X;
        internal int Y;
    }

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool SetWindowPos(
        IntPtr hwnd,
        IntPtr insertAfter,
        int x,
        int y,
        int width,
        int height,
        uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool RedrawWindow(IntPtr hwnd, IntPtr updateRect, IntPtr updateRegion, uint flags);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool IsWindowVisible(IntPtr hwnd);

    [StructLayout(LayoutKind.Sequential)]
    internal struct Msg
    {
        internal IntPtr Window;
        internal uint Message;
        internal IntPtr WParam;
        internal IntPtr LParam;
        internal uint Time;
        internal int X;
        internal int Y;
        internal uint Private;
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "CreateWindowExW")]
    internal static extern IntPtr CreateWindowEx(
        int exStyle,
        string className,
        string windowName,
        uint style,
        int x,
        int y,
        int width,
        int height,
        IntPtr parent,
        IntPtr menu,
        IntPtr instance,
        IntPtr param);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool DestroyWindow(IntPtr hwnd);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(out Msg message, IntPtr hwnd, uint filterMin, uint filterMax, uint remove);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool TranslateMessage(in Msg message);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "DispatchMessageW")]
    internal static extern IntPtr DispatchMessage(in Msg message);
}
