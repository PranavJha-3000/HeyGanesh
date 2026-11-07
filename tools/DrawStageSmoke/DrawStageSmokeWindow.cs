using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using HeyGanesh.Abstractions;
using HeyGanesh.DrawStage;
using static HeyGanesh.Tools.DrawStageSmoke.DrawStageSmokeHost;

namespace HeyGanesh.Tools.DrawStageSmoke;

/// <summary>
/// DS-2 and DS-3 (FEATURE-011 step 2): the real stage window on the cursor's monitor. Local only: they need an
/// interactive desktop and the WebView2 runtime. Each uses a throwaway WebView2 profile, deleted afterwards. Neither
/// moves the cursor or presses a key.
/// </summary>
internal static class DrawStageSmokeWindow
{
    // DS-2: the window's shape, isolation and fences, and one program drawn end to end.
    public static int RunAssertWindow() => WithStage((stage, messages) =>
    {
        var failures = 0;
        var hwnd = stage.Handle;
        var frame = stage.Frame;

        failures = Check(IsWindowVisible(hwnd), $"the stage opened and its page said ready, over the cursor's monitor {frame}", failures);
        var style = (uint)GetWindowLongPtr(hwnd, GWL_STYLE);
        var ex = (uint)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        failures = Check((style & DrawStageWindow.WS_POPUP) != 0 && (ex & DrawStageWindow.ExStyle) == DrawStageWindow.ExStyle,
            $"a popup with no redirection bitmap, layered, transparent, tool window, topmost, no-activate (ex 0x{ex:X8})", failures);
        GetWindowRect(hwnd, out var rect);
        failures = Check(rect.Left == frame.X && rect.Top == frame.Y && rect.Right == frame.Right && rect.Bottom == frame.Bottom,
            $"it covers exactly the monitor, in physical pixels ({rect.Left},{rect.Top})-({rect.Right},{rect.Bottom})", failures);
        failures = Check(GetWindowDisplayAffinity(hwnd, out var affinity) && affinity == WDA_EXCLUDEFROMCAPTURE,
            $"excluded from screen capture (display affinity 0x{affinity:X})", failures);
        failures = Check(SendMessage(hwnd, DrawStageWindow.WM_NCHITTEST, 0, 0) == DrawStageWindow.HTTRANSPARENT && !stage.TakesMouse,
            "with no boxes reported, every point is click-through (HTTRANSPARENT)", failures);
        failures = Check(SendMessage(hwnd, DrawStageWindow.WM_MOUSEACTIVATE, 0, 0) == DrawStageWindow.MA_NOACTIVATE && GetForegroundWindow() != hwnd,
            "never activated: a click answers MA_NOACTIVATE, and it is not the foreground window", failures);

        // A box over the whole monitor (so it holds the cursor wherever the person moves it during the run), reported
        // the way the page reports its figures and cards: the window takes the mouse, and lets it through again when the
        // page reports none.
        GetCursorPos(out var cursor);
        var scale = ScaleOf(frame);
        Run(stage.RunHostScriptAsync($"chrome.webview.postMessage({{type: 'figures', rects: [{{x: 0, y: 0, w: {frame.Width / scale:F0}, h: {frame.Height / scale:F0}}}]}}); 1"));
        var took = WaitFor(() => stage.TakesMouse, TimeSpan.FromSeconds(2));
        var exTaking = (uint)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        var hitTaking = SendMessage(hwnd, DrawStageWindow.WM_NCHITTEST, 0, 0);
        Run(stage.RunHostScriptAsync("chrome.webview.postMessage({type: 'figures', rects: []}); 1"));
        var released = WaitFor(() => !stage.TakesMouse, TimeSpan.FromSeconds(2));
        var exReleased = (uint)GetWindowLongPtr(hwnd, GWL_EXSTYLE);
        failures = Check(took && (exTaking & DrawStageWindow.WS_EX_TRANSPARENT) == 0 && hitTaking == DrawStageWindow.HTCLIENT
                && released && (exReleased & DrawStageWindow.WS_EX_TRANSPARENT) != 0 && SendMessage(hwnd, DrawStageWindow.WM_NCHITTEST, 0, 0) == DrawStageWindow.HTTRANSPARENT,
            "over a reported box it takes the mouse (HTCLIENT), and with none reported it is click-through again", failures);
        Run(stage.RunHostScriptAsync($"chrome.webview.postMessage({{type: 'figures', rects: [{{x: -1e9, y: -1e9, w: 1e12, h: 1e12}}, {{x: 'a', y: null}}]}}); 1"));
        Thread.Sleep(200);
        failures = Check(stage.TakesMouse == IsOnFrame(cursor, frame) && stage.Handle == hwnd,
            "absurd or malformed boxes are clipped to the monitor and read as 0, without fault", failures);
        Run(stage.RunHostScriptAsync("chrome.webview.postMessage({type: 'figures', rects: []}); 1"));
        WaitFor(() => !stage.TakesMouse, TimeSpan.FromSeconds(2));

        // The page may never leave its origin.
        Run(stage.RunHostScriptAsync("location.href = 'https://example.com/'; 1"));
        Thread.Sleep(700);
        var origin = Run(stage.RunHostScriptAsync("location.origin"));
        failures = Check(stage.BlockedNavigations == 1 && origin == $"\"https://{DrawStageWindow.PageHost}\"",
            $"a navigation away is cancelled ({stage.BlockedNavigations} blocked) and the page stays on {origin}", failures);

        // What the page's host serves: the page's own files, the worker with its own policy, nothing else.
        failures = Check(ServesOnlyThePage(), "the host serves .html/.js/.css under the page folder only: other hosts, http, other types, missing files and ../ escapes get 404", failures);

        // One program end to end: the worker is created, served with its policy, runs, and the page finishes drawing.
        messages.Clear();
        Run(stage.PostAsync(JsonSerializer.Serialize(new { type = "beginStream", w = 1280, h = 720, pace = "estimate" })));
        Run(stage.PostAsync(JsonSerializer.Serialize(new { type = "feed", code = "step(1); say('One red dot.')\ndot(640, 360, 12, {color: 'red'})\n", done = true })));
        Run(stage.PostAsync("{\"type\":\"finishNow\"}"));
        var finished = WaitFor(() => messages.Any(m => m.Contains("\"finished\"", StringComparison.Ordinal)), TimeSpan.FromSeconds(10));
        var errors = messages.Where(m => m.Contains("\"error\"", StringComparison.Ordinal)).ToArray();
        failures = Check(finished && errors.Length == 0 && messages.Any(m => m.Contains("\"line\"", StringComparison.Ordinal)),
            $"a program runs in the worker and the page reports its line and 'finished' ({(errors.Length == 0 ? "no errors" : string.Join("; ", errors))})", failures);
        failures = Check(stage.WorkerPolicyServed, "the program worker was served with its own CSP (no network, no scripts, no workers)", failures);

        stage.Dispose();
        failures = Check(!IsWindow(hwnd) && !stage.ThreadAlive, "Dispose destroys the window and ends the stage thread", failures);
        return Summarize(failures, "WINDOW OK");
    });

    // DS-3: two samples drawn on the stage, saved as PNGs for a person to look at.
    public static int RunSnapshot(string folder) => WithStage((stage, messages) =>
    {
        Directory.CreateDirectory(folder);
        var failures = 0;
        foreach (var sample in new[] { "rocket.js", "jet-engine-cutaway-3d.js" })
        {
            messages.Clear();
            var code = File.ReadAllText(Path.Combine(DrawStagePage.Directory, "samples", sample));
            var watch = Stopwatch.StartNew();
            Run(stage.PostAsync(JsonSerializer.Serialize(new { type = "beginStream", w = stage.Frame.Width, h = stage.Frame.Height, pace = "estimate" })));
            Run(stage.PostAsync(JsonSerializer.Serialize(new { type = "feed", code, done = true })));
            Run(stage.PostAsync("{\"type\":\"finishNow\"}"));
            var finished = WaitFor(() => messages.Any(m => m.Contains("\"finished\"", StringComparison.Ordinal)), TimeSpan.FromSeconds(30));
            Thread.Sleep(2500); // the 3D figure settles and turns a little
            var png = Run(stage.CaptureAsync());
            var path = Path.Combine(folder, Path.ChangeExtension(sample, ".png"));
            File.WriteAllBytes(path, png);
            var errors = messages.Where(m => m.Contains("\"error\"", StringComparison.Ordinal)).ToArray();
            failures = Check(finished && errors.Length == 0 && png.Length > 1000,
                $"{sample}: drawn in {watch.ElapsedMilliseconds} ms, {png.Length / 1024} KB -> {path} ({(errors.Length == 0 ? "no errors" : string.Join("; ", errors))})", failures);
        }

        return Summarize(failures, "SNAPSHOT OK (review the PNGs by eye)");
    });

    private static int WithStage(Func<DrawStageWindow, ConcurrentQueue<string>, int> body)
    {
        var profile = Path.Combine(Path.GetTempPath(), "heyganesh-drawstagesmoke", Guid.NewGuid().ToString("N")[..8]);
        var frame = CursorMonitor();
        var messages = new ConcurrentQueue<string>();
        var stage = Run(DrawStageWindow.OpenAsync(frame, ScaleOf(frame), profile));
        stage.MessageReceived += messages.Enqueue;
        try
        {
            return body(stage, messages);
        }
        finally
        {
            stage.Dispose();
            try
            {
                Directory.Delete(profile, recursive: true);
            }
            catch (IOException)
            {
                // WebView2 can hold the profile for a moment after the browser exits; it is a temp folder.
            }
            catch (UnauthorizedAccessException)
            {
            }
        }
    }

    private static bool ServesOnlyThePage()
    {
        var temp = Path.Combine(Path.GetTempPath(), "heyganesh-drawstagesmoke", Guid.NewGuid().ToString("N")[..8]);
        var page = Path.Combine(temp, "page");
        Directory.CreateDirectory(page);
        File.WriteAllText(Path.Combine(page, "index.html"), "<p>");
        File.WriteAllText(Path.Combine(page, "stage-worker.js"), "1");
        File.WriteAllText(Path.Combine(page, "notes.md"), "x");
        File.WriteAllText(Path.Combine(temp, "secret.js"), "secret");
        try
        {
            var host = "https://" + DrawStageWindow.PageHost + "/";
            int Status(string uri) => DrawStageWindow.PageFile(uri, page).Status;
            var index = DrawStageWindow.PageFile(host + "index.html", page);
            var worker = DrawStageWindow.PageFile(host + "stage-worker.js", page);
            return index.Status == 200 && index.Headers.Contains("text/html", StringComparison.Ordinal) && !index.Headers.Contains("Content-Security-Policy", StringComparison.Ordinal)
                && worker.Status == 200 && worker.Headers.Contains("Content-Security-Policy: " + DrawStageWindow.WorkerPolicy, StringComparison.Ordinal)
                && Status(host + "notes.md") == 404
                && Status(host + "missing.js") == 404
                && Status(host + "%2e%2e%2fsecret.js") == 404
                && Status(host + "..%5csecret.js") == 404
                && Status("http://" + DrawStageWindow.PageHost + "/index.html") == 404
                && Status("https://example.com/index.html") == 404
                && Status("not a uri") == 404;
        }
        finally
        {
            Directory.Delete(temp, recursive: true);
        }
    }

    private static PixelRect CursorMonitor()
    {
        GetCursorPos(out var cursor);
        var screen = Screen.FromPoint(new System.Drawing.Point(cursor.X, cursor.Y));
        var b = screen.Bounds; // physical pixels: the process is PerMonitorV2 (Program.cs)
        return new PixelRect(b.X, b.Y, b.Width, b.Height);
    }

    private static double ScaleOf(PixelRect frame)
    {
        var monitor = MonitorFromPoint(new NativePoint { X = frame.X + frame.Width / 2, Y = frame.Y + frame.Height / 2 }, MONITOR_DEFAULTTONEAREST);
        return GetDpiForMonitor(monitor, 0, out var dpi, out _) == 0 && dpi > 0 ? dpi / 96.0 : 1.0;
    }

    private static bool IsOnFrame(NativePoint p, PixelRect f) => p.X >= f.X && p.X < f.Right && p.Y >= f.Y && p.Y < f.Bottom;

    private static T Run<T>(Task<T> task) => task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

    private static void Run(Task task) => task.WaitAsync(TimeSpan.FromSeconds(30)).GetAwaiter().GetResult();

    private static bool WaitFor(Func<bool> condition, TimeSpan timeout)
    {
        var watch = Stopwatch.StartNew();
        while (watch.Elapsed < timeout)
        {
            if (condition())
            {
                return true;
            }

            Thread.Sleep(25);
        }

        return condition();
    }

    private const int GWL_STYLE = -16;
    private const int GWL_EXSTYLE = -20;
    private const uint WDA_EXCLUDEFROMCAPTURE = 0x11;
    private const uint MONITOR_DEFAULTTONEAREST = 2;

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativeRect
    {
        public int Left;
        public int Top;
        public int Right;
        public int Bottom;
    }

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowRect(nint hWnd, out NativeRect rect);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetWindowDisplayAffinity(nint hWnd, out uint affinity);

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    private static extern nint SendMessage(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll")]
    private static extern nint GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindow(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(nint hWnd);

    [DllImport("user32.dll")]
    private static extern nint MonitorFromPoint(NativePoint pt, uint flags);

    [DllImport("shcore.dll")]
    private static extern int GetDpiForMonitor(nint monitor, int dpiType, out uint dpiX, out uint dpiY);
}
