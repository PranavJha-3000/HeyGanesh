using System.Drawing;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;
using Microsoft.Web.WebView2.Core;
using Windows.UI.Composition;
using Windows.UI.Composition.Desktop;
using WinRT;

namespace HeyGanesh.DrawStage;

/// <summary>
/// The drawing stage on screen (FEATURE-011 step 2, DEC-069), ported from openclicky's DrawStageWindow (MIT). One
/// see-through, click-through, topmost, never-activated window over one monitor, showing the vendored page
/// (DrawStagePage/index.html) in WebView2.
/// <para>
/// A WinForms or WPF host can't do this: a WebView2 control in a layered window draws nothing, and in an opaque one it
/// covers the screen. So the window is a plain Win32 popup with no redirection surface, its only content is a
/// Windows.UI.Composition visual tree, and WebView2 runs in visual hosting mode (CoreWebView2CompositionController)
/// with a transparent background, so the page's alpha reaches the desktop.
/// </para>
/// <para>
/// Capture-excluded (a hard gate: the next question's screenshot must see the screen, not the drawing). Coordinates
/// are physical pixels; the host process must be PerMonitorV2. The mouse passes through except over the boxes the page
/// reports in its "figures" message (3D figures, cards), where the window takes it and forwards it to the page.
/// </para>
/// <para>
/// The page is served by <see cref="PageFile"/> from the output folder only (never the network); every other
/// navigation is cancelled, and the program worker gets its own Content-Security-Policy header, because a worker
/// follows its own response's policy, not the page's &lt;meta&gt;. Every member runs on the stage's own thread.
/// </para>
/// </summary>
public sealed class DrawStageWindow : IDisposable
{
    /// <summary>The page's origin, served by <see cref="PageFile"/>; .invalid can never resolve on a network.</summary>
    public const string PageHost = "drawstage.heyganesh.invalid";

    internal const string PageUrl = "https://" + PageHost + "/index.html";

    /// <summary>The program worker's policy: no network, no scripts or workers of its own (so no import() of an address),
    /// eval for the program's statements (openclicky's Windows host, unchanged).</summary>
    internal const string WorkerPolicy = "default-src 'none'; script-src 'unsafe-eval'; worker-src 'none'; connect-src 'none'";

    private static readonly TimeSpan ReadyTimeout = TimeSpan.FromSeconds(15);

    private readonly StageThread _thread = new();
    private readonly PixelRect _frame;
    private readonly double _scale;
    private readonly WndProc _procedure;
    private readonly string _pageFolder;
    private readonly TaskCompletionSource _ready = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private nint _hwnd;
    private string? _className;
    private object? _dispatcherQueueController;
    private Compositor? _compositor;
    private DesktopWindowTarget? _target;
    private ContainerVisual? _root;
    private CoreWebView2CompositionController? _web;
    private List<PixelRect> _figureBoxes = new();
    private System.Windows.Forms.Timer? _pointerWatch;
    private volatile bool _takesMouse;
    private bool _dragging;
    private int _blockedNavigations;
    private int _offPageRequests;
    private volatile bool _workerPolicyServed;

    private DrawStageWindow(PixelRect frame, double scale, string pageFolder)
    {
        _frame = frame;
        _scale = scale;
        _pageFolder = pageFolder;
        _procedure = WindowProcedure; // held in a field: the window class keeps only a raw pointer to it
    }

    /// <summary>Every message the page posts, as its raw JSON (stage thread). The page is untrusted: listeners parse
    /// and check what they read.</summary>
    public event Action<string>? MessageReceived;

    public nint Handle => _hwnd;

    /// <summary>The monitor area the stage covers, in physical pixels.</summary>
    public PixelRect Frame => _frame;

    /// <summary>True while the pointer is over a box the page reported (the window then takes the mouse).</summary>
    public bool TakesMouse => _takesMouse;

    /// <summary>Navigations away from the page that were cancelled.</summary>
    public int BlockedNavigations => Volatile.Read(ref _blockedNavigations);

    /// <summary>Requests for anything but the page's own host, all answered 403 locally; 0 while the CSPs hold.</summary>
    public int OffPageRequests => Volatile.Read(ref _offPageRequests);

    /// <summary>The program worker's script was served with <see cref="WorkerPolicy"/> at least once.</summary>
    public bool WorkerPolicyServed => _workerPolicyServed;

    internal bool ThreadAlive => _thread.IsAlive;

    /// <summary>
    /// Creates the window over <paramref name="frame"/> (physical pixels; <paramref name="scale"/> is the monitor's
    /// DPI / 96), loads the page, waits for its "ready" and shows the window. Throws if the page is not deployed, the
    /// window cannot be excluded from capture, WebView2 or composition is missing, or the page does not load in 15 s.
    /// </summary>
    public static async Task<DrawStageWindow> OpenAsync(PixelRect frame, double scale, string? userDataFolder = null, CancellationToken cancellationToken = default)
    {
        if (frame.Width <= 0 || frame.Height <= 0)
        {
            throw new ArgumentException($"the stage needs a non-empty monitor area, got {frame}", nameof(frame));
        }

        if (!(scale > 0) || double.IsInfinity(scale))
        {
            throw new ArgumentOutOfRangeException(nameof(scale), scale, "the monitor scale must be a positive number");
        }

        var pageFolder = DrawStagePage.Directory;
        if (!File.Exists(Path.Combine(pageFolder, "index.html")))
        {
            throw new FileNotFoundException("the drawing stage page was not deployed next to the host", Path.Combine(pageFolder, "index.html"));
        }

        var dataRoot = Environment.GetEnvironmentVariable("HEYGANESH_DATA_DIR");
        var userData = userDataFolder
            ?? Path.Combine(string.IsNullOrEmpty(dataRoot) ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeyGanesh") : dataRoot, "webview2-drawstage");

        var window = new DrawStageWindow(frame, scale, pageFolder);
        try
        {
            await window._thread.InvokeAsync(() => window.StartAsync(userData)).WaitAsync(cancellationToken);
            return window;
        }
        catch
        {
            window.Dispose();
            throw;
        }
    }

    /// <summary>Posts one message (JSON text) to the page.</summary>
    public Task PostAsync(string json) => _thread.RunAsync(() => _web!.CoreWebView2.PostWebMessageAsJson(json));

    public Task ShowAsync() => _thread.RunAsync(Place);

    /// <summary>Hides the window; a hidden page gets no frames (its loops and 3D figures stop) until shown again.</summary>
    public Task HideAsync() => _thread.RunAsync(() =>
    {
        _web!.IsVisible = false;
        ShowWindow(_hwnd, SW_HIDE);
    });

    /// <summary>The page as it looks now, as a PNG. WebView2 renders it itself, so this works although the window is
    /// left out of screen capture; for review only.</summary>
    public Task<byte[]> CaptureAsync() => _thread.InvokeAsync(async () =>
    {
        using var stream = new MemoryStream();
        await _web!.CoreWebView2.CapturePreviewAsync(CoreWebView2CapturePreviewImageFormat.Png, stream);
        return stream.ToArray();
    });

    /// <summary>Runs one fixed, host-owned script in the page (the smoke host only). Never called with model text.</summary>
    internal Task<string> RunHostScriptAsync(string script) => _thread.InvokeAsync(() => _web!.CoreWebView2.ExecuteScriptAsync(script));

    public void Dispose() => _thread.Dispose(() =>
    {
        _pointerWatch?.Dispose();
        _pointerWatch = null;
        _web?.Close();
        _web = null;
        _target?.Dispose();
        _target = null;
        _compositor?.Dispose();
        _compositor = null;
        if (_hwnd != 0)
        {
            DestroyWindow(_hwnd);
            _hwnd = 0;
        }

        if (_className is not null)
        {
            UnregisterClass(_className, GetModuleHandle(null));
        }
    });

    // ------------------------------------------------------------------ starting (stage thread)

    private async Task StartAsync(string userData)
    {
        CreateWindow();
        CreateVisualTree();
        await CreateWebViewAsync(userData);
        try
        {
            await _ready.Task.WaitAsync(ReadyTimeout);
        }
        catch (TimeoutException)
        {
            throw new TimeoutException($"the drawing stage page did not say ready within {ReadyTimeout.TotalSeconds:F0} s");
        }

        Place();
    }

    private void CreateWindow()
    {
        _className = "HeyGanesh.DrawStage." + Guid.NewGuid().ToString("N");
        var windowClass = new WNDCLASSEX
        {
            cbSize = (uint)Marshal.SizeOf<WNDCLASSEX>(),
            lpfnWndProc = Marshal.GetFunctionPointerForDelegate(_procedure),
            hInstance = GetModuleHandle(null),
            lpszClassName = _className,
        };
        if (RegisterClassEx(ref windowClass) == 0)
        {
            _className = null;
            throw new InvalidOperationException("RegisterClassEx failed: " + Marshal.GetLastWin32Error());
        }

        // No redirection surface: the composition tree is the window's only content, alpha and all. Layered (fully
        // opaque as a layer) + transparent: the mouse goes to whatever is under the drawing. Created hidden.
        _hwnd = CreateWindowEx(ExStyle, _className, "Hey Ganesh drawing", WS_POPUP, 0, 0, 1, 1, 0, 0, windowClass.hInstance, 0);
        if (_hwnd == 0)
        {
            throw new InvalidOperationException("CreateWindowEx failed: " + Marshal.GetLastWin32Error());
        }

        SetLayeredWindowAttributes(_hwnd, 0, 255, LWA_ALPHA);
        if (!CaptureExclusion.TryExcludeFromCapture(_hwnd, out var error))
        {
            throw new InvalidOperationException($"the drawing stage could not be excluded from capture (Win32 error {error})");
        }
    }

    private void CreateVisualTree()
    {
        // Windows.UI.Composition needs a DispatcherQueue on this thread; the WinForms loop pumps it.
        if (global::Windows.System.DispatcherQueue.GetForCurrentThread() is null)
        {
            var options = new DispatcherQueueOptions { dwSize = Marshal.SizeOf<DispatcherQueueOptions>(), threadType = DQTYPE_THREAD_CURRENT, apartmentType = DQTAT_COM_STA };
            Marshal.ThrowExceptionForHR(CreateDispatcherQueueController(options, out var controller));
            _dispatcherQueueController = Marshal.GetObjectForIUnknown(controller);
            Marshal.Release(controller);
        }

        _compositor = new Compositor();
        var interop = _compositor.As<ICompositorDesktopInterop>();
        interop.CreateDesktopWindowTarget(_hwnd, true, out var rawTarget);
        _target = DesktopWindowTarget.FromAbi(rawTarget);
        Marshal.Release(rawTarget);
        _root = _compositor.CreateContainerVisual();
        _root.RelativeSizeAdjustment = System.Numerics.Vector2.One;
        _target.Root = _root;
    }

    private async Task CreateWebViewAsync(string userData)
    {
        var environment = await CoreWebView2Environment.CreateAsync(null, userData);
        var controller = await environment.CreateCoreWebView2CompositionControllerAsync(_hwnd);

        // WebView2 marshals RootVisualTarget as a classic COM object: hand it the visual's own IUnknown.
        var visualPointer = MarshalInspectable<object>.FromManaged(_root!);
        try
        {
            controller.RootVisualTarget = Marshal.GetObjectForIUnknown(visualPointer);
        }
        finally
        {
            Marshal.Release(visualPointer);
        }

        // Transparent from the first frame (set before navigating, so there is no white flash).
        controller.DefaultBackgroundColor = Color.Transparent;
        controller.ShouldDetectMonitorScaleChanges = false;
        controller.RasterizationScale = _scale;
        controller.Bounds = new Rectangle(0, 0, _frame.Width, _frame.Height);
        // Visual hosting leaves the pointer's look to the window: the page's (grab over a figure, move over a card).
        controller.CursorChanged += (_, _) =>
        {
            if (_takesMouse)
            {
                SetCursor(controller.Cursor);
            }
        };

        var page = controller.CoreWebView2;
        var settings = page.Settings;
        settings.AreDevToolsEnabled = false;
        settings.AreDefaultContextMenusEnabled = false;
        settings.AreHostObjectsAllowed = false;
        settings.AreBrowserAcceleratorKeysEnabled = false;
        settings.IsWebMessageEnabled = true;
        settings.IsStatusBarEnabled = false;
        settings.IsZoomControlEnabled = false;
        settings.IsPinchZoomEnabled = false;
        settings.IsSwipeNavigationEnabled = false;
        settings.IsGeneralAutofillEnabled = false;
        settings.IsPasswordAutosaveEnabled = false;

        // Every request to the page's host is answered here, from the output folder. A virtual host folder mapping
        // would be simpler, but its responses can't carry headers, and the worker needs a CSP of its own.
        // Every other request is answered here too, with 403, and counted (DEC-070): the CSPs should stop any before it is
        // made, so a non-zero count means a fence failed, and even then nothing reaches the network.
        page.AddWebResourceRequestedFilter("*", CoreWebView2WebResourceContext.All, CoreWebView2WebResourceRequestSourceKinds.All);
        page.WebResourceRequested += (_, args) =>
        {
            if (!args.Request.Uri.StartsWith("https://" + PageHost + "/", StringComparison.OrdinalIgnoreCase))
            {
                Interlocked.Increment(ref _offPageRequests);
                args.Response = environment.CreateWebResourceResponse(null, 403, "Forbidden", "Content-Type: text/plain; charset=utf-8");
                return;
            }

            var (status, reason, body, headers) = PageFile(args.Request.Uri, _pageFolder);
            args.Response = environment.CreateWebResourceResponse(body, status, reason, headers);
            if (status == 200 && headers.Contains(WorkerPolicy, StringComparison.Ordinal))
            {
                _workerPolicyServed = true;
            }
        };
        page.NavigationStarting += (_, args) =>
        {
            if (!args.Uri.StartsWith("https://" + PageHost + "/", StringComparison.OrdinalIgnoreCase))
            {
                args.Cancel = true;
                Interlocked.Increment(ref _blockedNavigations);
            }
        };
        page.NavigationCompleted += (_, args) =>
        {
            if (!args.IsSuccess)
            {
                _ready.TrySetException(new InvalidOperationException($"the drawing stage page did not load ({args.WebErrorStatus})"));
            }
        };
        page.NewWindowRequested += (_, args) => args.Handled = true;
        page.WebMessageReceived += (_, args) => OnPageMessage(args.WebMessageAsJson);

        _web = controller;
        page.Navigate(PageUrl);
    }

    private void OnPageMessage(string json)
    {
        string? type = null;
        try
        {
            if (JsonNode.Parse(json) is JsonObject message && message["type"] is JsonValue value && value.TryGetValue<string>(out var text))
            {
                type = text;
                if (type == "figures")
                {
                    NoteFigureBoxes(message);
                }
            }
        }
        catch (JsonException)
        {
            // Not JSON we understand: it still goes to the listeners, who check what they read.
        }

        if (type == "ready")
        {
            _ready.TrySetResult();
        }

        MessageReceived?.Invoke(json);
    }

    /// <summary>
    /// The response for a request to the page's host: an .html, .js or .css file under the page folder with its type
    /// (the worker's with <see cref="WorkerPolicy"/>), or 404 for anything else: another host, another type, a path
    /// that leaves the folder, a file that isn't there.
    /// </summary>
    internal static (int Status, string Reason, Stream? Body, string Headers) PageFile(string uri, string pageFolder)
    {
        const string NotFound = "Content-Type: text/plain; charset=utf-8";
        if (!Uri.TryCreate(uri, UriKind.Absolute, out var parsed) || parsed.Scheme != Uri.UriSchemeHttps
            || !string.Equals(parsed.Host, PageHost, StringComparison.OrdinalIgnoreCase))
        {
            return (404, "Not Found", null, NotFound);
        }

        var relative = Uri.UnescapeDataString(parsed.AbsolutePath).TrimStart('/');
        var type = Path.GetExtension(relative).ToLowerInvariant() switch
        {
            ".html" => "text/html; charset=utf-8",
            ".js" => "text/javascript; charset=utf-8",
            ".css" => "text/css; charset=utf-8",
            _ => null,
        };
        var folder = Path.GetFullPath(pageFolder).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var path = Path.GetFullPath(Path.Combine(folder, relative));
        if (type is null || !path.StartsWith(folder, StringComparison.OrdinalIgnoreCase) || !File.Exists(path))
        {
            return (404, "Not Found", null, NotFound);
        }

        var headers = $"Content-Type: {type}\r\nCache-Control: no-store";
        if (string.Equals(relative, "stage-worker.js", StringComparison.OrdinalIgnoreCase))
        {
            headers += $"\r\nContent-Security-Policy: {WorkerPolicy}";
        }

        return (200, "OK", new MemoryStream(File.ReadAllBytes(path)), headers);
    }

    private void Place()
    {
        if (_web is null || _hwnd == 0)
        {
            return;
        }

        _web.IsVisible = true;
        SetWindowPos(_hwnd, HWND_TOPMOST, _frame.X, _frame.Y, _frame.Width, _frame.Height, SWP_NOACTIVATE | SWP_SHOWWINDOW);
    }

    private nint WindowProcedure(nint handle, uint message, nint wParam, nint lParam)
    {
        switch (message)
        {
            case WM_NCHITTEST:
                return _takesMouse ? HTCLIENT : HTTRANSPARENT;
            case WM_MOUSEACTIVATE:
                return MA_NOACTIVATE;
            case WM_SETCURSOR when _takesMouse && _web is not null && (lParam.ToInt64() & 0xFFFF) == HTCLIENT:
                SetCursor(_web.Cursor);
                return 1;
            case WM_MOUSEMOVE or WM_LBUTTONDOWN or WM_LBUTTONUP or WM_RBUTTONDOWN or WM_RBUTTONUP or WM_MOUSEWHEEL when _web is not null:
            {
                // Over a box the page reported: the mouse goes into the page (visual hosting has no window of its own).
                var x = (short)(lParam.ToInt64() & 0xFFFF);
                var y = (short)((lParam.ToInt64() >> 16) & 0xFFFF);
                var point = new Point(x, y);
                if (message == WM_MOUSEWHEEL)
                {
                    // The wheel's position comes in screen coordinates; the others in client coordinates.
                    var client = new NativePoint { X = x, Y = y };
                    ScreenToClient(handle, ref client);
                    point = new Point(client.X, client.Y);
                }

                if (message == WM_LBUTTONDOWN)
                {
                    _dragging = true;
                    SetCapture(handle);
                }

                if (message == WM_LBUTTONUP)
                {
                    _dragging = false;
                    ReleaseCapture();
                }

                var keys = (CoreWebView2MouseEventVirtualKeys)(int)(wParam.ToInt64() & 0xFFFF);
                var data = message == WM_MOUSEWHEEL ? (uint)(short)((wParam.ToInt64() >> 16) & 0xFFFF) : 0;
                _web.SendMouseInput((CoreWebView2MouseEventKind)(int)message, keys, data, point);
                return 0;
            }

            default:
                return DefWindowProc(handle, message, wParam, lParam);
        }
    }

    // ------------------------------------------------------------------ taking the mouse over figures and cards

    /// <summary>The page's "figures" message: each box in page pixels, turned into screen pixels. Malformed or
    /// non-finite numbers count as 0; a page can only ever make the window take the mouse inside its own monitor.</summary>
    private void NoteFigureBoxes(JsonObject message)
    {
        var boxes = new List<PixelRect>();
        if (message["rects"] is JsonArray rects)
        {
            foreach (var node in rects.Take(64))
            {
                double Value(string key) => node is JsonObject o && o[key] is JsonValue v && v.TryGetValue<double>(out var d) && double.IsFinite(d) ? d : 0;
                // Clipped to the monitor in double first: a huge page number cast straight to int overflows.
                var left = Math.Clamp(_frame.X + Value("x") * _scale, _frame.X, _frame.Right);
                var top = Math.Clamp(_frame.Y + Value("y") * _scale, _frame.Y, _frame.Bottom);
                var right = Math.Clamp(_frame.X + (Value("x") + Math.Max(0, Value("w"))) * _scale, left, _frame.Right);
                var bottom = Math.Clamp(_frame.Y + (Value("y") + Math.Max(0, Value("h"))) * _scale, top, _frame.Bottom);
                boxes.Add(new PixelRect((int)left, (int)top, (int)(right - left), (int)(bottom - top)));
            }
        }

        _figureBoxes = boxes;
        if (boxes.Count > 0 && _pointerWatch is null)
        {
            _pointerWatch = new System.Windows.Forms.Timer { Interval = 40 };
            _pointerWatch.Tick += (_, _) => WatchPointer();
            _pointerWatch.Start();
        }
        else if (boxes.Count == 0)
        {
            _pointerWatch?.Dispose();
            _pointerWatch = null;
            SetTakesMouse(false);
        }
    }

    /// <summary>Takes the mouse while the pointer is over a box (or a drag that started on one goes on), else lets it through.</summary>
    private void WatchPointer()
    {
        if (!GetCursorPos(out var cursor))
        {
            return;
        }

        var over = _figureBoxes.Any(box => cursor.X >= box.X && cursor.X < box.Right && cursor.Y >= box.Y && cursor.Y < box.Bottom);
        SetTakesMouse(over || _dragging);
    }

    private void SetTakesMouse(bool value)
    {
        if (_takesMouse == value || _hwnd == 0)
        {
            return;
        }

        _takesMouse = value;
        var style = (long)GetWindowLongPtr(_hwnd, GWL_EXSTYLE);
        style = value ? style & ~(long)WS_EX_TRANSPARENT : style | WS_EX_TRANSPARENT;
        SetWindowLongPtr(_hwnd, GWL_EXSTYLE, (nint)style);
        if (!value)
        {
            _web?.SendMouseInput(CoreWebView2MouseEventKind.Leave, CoreWebView2MouseEventVirtualKeys.None, 0, new Point());
        }
    }

    // ------------------------------------------------------------------ Win32 and composition interop

    internal const uint WS_POPUP = 0x80000000;
    internal const uint WS_EX_NOREDIRECTIONBITMAP = 0x00200000;
    internal const uint WS_EX_LAYERED = 0x00080000;
    internal const uint WS_EX_TRANSPARENT = 0x00000020;
    internal const uint WS_EX_TOOLWINDOW = 0x00000080;
    internal const uint WS_EX_TOPMOST = 0x00000008;
    internal const uint WS_EX_NOACTIVATE = 0x08000000;
    internal const uint ExStyle = WS_EX_NOREDIRECTIONBITMAP | WS_EX_LAYERED | WS_EX_TRANSPARENT | WS_EX_TOOLWINDOW | WS_EX_TOPMOST | WS_EX_NOACTIVATE;
    private const uint LWA_ALPHA = 0x2;
    private const int SW_HIDE = 0;
    private static readonly nint HWND_TOPMOST = -1;
    private const uint SWP_NOACTIVATE = 0x0010;
    private const uint SWP_SHOWWINDOW = 0x0040;
    internal const uint WM_NCHITTEST = 0x0084;
    internal const uint WM_MOUSEACTIVATE = 0x0021;
    private const uint WM_SETCURSOR = 0x0020;
    internal const nint HTTRANSPARENT = -1;
    internal const nint HTCLIENT = 1;
    internal const nint MA_NOACTIVATE = 3;
    private const int GWL_EXSTYLE = -20;
    private const uint WM_MOUSEMOVE = 0x0200, WM_LBUTTONDOWN = 0x0201, WM_LBUTTONUP = 0x0202, WM_RBUTTONDOWN = 0x0204, WM_RBUTTONUP = 0x0205, WM_MOUSEWHEEL = 0x020A;
    private const int DQTYPE_THREAD_CURRENT = 2;
    private const int DQTAT_COM_STA = 2;

    [UnmanagedFunctionPointer(CallingConvention.Winapi)]
    private delegate nint WndProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WNDCLASSEX
    {
        public uint cbSize;
        public uint style;
        public nint lpfnWndProc;
        public int cbClsExtra;
        public int cbWndExtra;
        public nint hInstance;
        public nint hIcon;
        public nint hCursor;
        public nint hbrBackground;
        public string? lpszMenuName;
        public string lpszClassName;
        public nint hIconSm;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct NativePoint
    {
        public int X;
        public int Y;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct DispatcherQueueOptions
    {
        public int dwSize;
        public int threadType;
        public int apartmentType;
    }

    [ComImport, Guid("29E691FA-4567-4DCA-B319-D0F207EB6807"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
    private interface ICompositorDesktopInterop
    {
        void CreateDesktopWindowTarget(nint hwndTarget, [MarshalAs(UnmanagedType.Bool)] bool isTopmost, out nint result);

        void EnsureOnThread(uint threadId);
    }

    [DllImport("CoreMessaging.dll")]
    private static extern int CreateDispatcherQueueController(DispatcherQueueOptions options, out nint dispatcherQueueController);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern ushort RegisterClassEx(ref WNDCLASSEX lpwcx);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool UnregisterClass(string lpClassName, nint hInstance);

    [DllImport("user32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern nint CreateWindowEx(uint dwExStyle, string lpClassName, string lpWindowName, uint dwStyle, int x, int y, int nWidth, int nHeight,
        nint hWndParent, nint hMenu, nint hInstance, nint lpParam);

    [DllImport("user32.dll", CharSet = CharSet.Unicode)]
    private static extern nint DefWindowProc(nint hWnd, uint msg, nint wParam, nint lParam);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool DestroyWindow(nint hWnd);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern nint GetModuleHandle(string? lpModuleName);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetLayeredWindowAttributes(nint hwnd, uint crKey, byte bAlpha, uint dwFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ShowWindow(nint hWnd, int nCmdShow);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(nint hWnd, nint hWndInsertAfter, int x, int y, int cx, int cy, uint uFlags);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ScreenToClient(nint hWnd, ref NativePoint point);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetCursorPos(out NativePoint point);

    [DllImport("user32.dll")]
    private static extern nint SetCapture(nint hWnd);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool ReleaseCapture();

    [DllImport("user32.dll")]
    private static extern nint SetCursor(nint hCursor);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongPtrW")]
    private static extern nint GetWindowLongPtr(nint hWnd, int nIndex);

    [DllImport("user32.dll", EntryPoint = "SetWindowLongPtrW")]
    private static extern nint SetWindowLongPtr(nint hWnd, int nIndex, nint dwNewLong);
}
