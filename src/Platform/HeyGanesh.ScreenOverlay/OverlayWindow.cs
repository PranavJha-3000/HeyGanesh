using System.Runtime.InteropServices;
using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;

namespace HeyGanesh.ScreenOverlay;

/// <summary>
/// One top-level overlay window: one per monitor per show, created on the pump thread, click-through
/// and capture-excluded from the moment it exists.
/// </summary>
/// <remarks>
/// The window lives only between <see cref="Create"/> and <see cref="Destroy"/>. Every style and
/// handler here serves one contract — <b>render, never interact</b>: <c>WM_NCHITTEST</c> returns
/// <c>HTTRANSPARENT</c> for every point (WS_EX_TRANSPARENT alone is not sufficient for a top-level
/// window, DEC-011), <c>WS_EX_NOACTIVATE</c> plus <c>WM_MOUSEACTIVATE</c>/<c>MA_NOACTIVATE</c> make
/// activation impossible, and there is no handler that could respond to a click, a key or focus.
/// </remarks>
internal sealed class OverlayWindow
{
    /// <summary>Registered once per process; the smoke host asserts styles against this exact class name.</summary>
    internal const string ClassName = "HeyGanesh.ScreenOverlay.Window";

    private readonly ScreenOverlayOptions _options;
    private readonly PixelRect _monitorBounds;
    private readonly OverlayDraw _draw;
    private OverlayPInvoke.WindowProc? _procKeepAlive;

    private IntPtr _hwnd;

    internal OverlayWindow(ScreenOverlayOptions options, PixelRect monitorBounds, OverlayDraw draw)
    {
        _options = options;
        _monitorBounds = monitorBounds;
        _draw = draw;
    }

    /// <summary>The real top-level HWND handed to the capture-exclusion contract.</summary>
    internal IntPtr Handle => _hwnd;

    /// <summary>Win32 failure text when creation failed; checked by the service right after <see cref="Create"/>.</summary>
    internal string? CreationError { get; private set; }

    /// <summary>Creates the window at the monitor's exact bounds. Called only on the pump thread.</summary>
    internal void Create()
    {
        var instance = OverlayPInvoke.GetModuleHandle(null);

        // The class registers once per process; a second service instance in the same process gets
        // ERROR_CLASS_ALREADY_EXISTS, which is success for our purposes.
        _procKeepAlive = WindowProc;
        var windowClass = new OverlayPInvoke.WindowClassEx
        {
            Size = (uint)Marshal.SizeOf<OverlayPInvoke.WindowClassEx>(),
            WindowProc = Marshal.GetFunctionPointerForDelegate(_procKeepAlive),
            Instance = instance,
            ClassName = ClassName,
        };

        var atom = OverlayPInvoke.RegisterClassEx(ref windowClass);
        if (atom == 0)
        {
            var error = Marshal.GetLastWin32Error();
            if (error != OverlayNative.ERROR_CLASS_ALREADY_EXISTS)
            {
                CreationError = $"RegisterClassEx failed with Win32 error {error}.";
                return;
            }
        }

        _hwnd = OverlayPInvoke.CreateWindowEx(
            OverlayNative.OverlayExtendedStyle,
            ClassName,
            string.Empty,
            OverlayNative.WS_POPUP,
            _monitorBounds.X,
            _monitorBounds.Y,
            _monitorBounds.Width,
            _monitorBounds.Height,
            IntPtr.Zero,
            IntPtr.Zero,
            instance,
            IntPtr.Zero);

        if (_hwnd == IntPtr.Zero)
        {
            CreationError = $"CreateWindowEx failed with Win32 error {Marshal.GetLastWin32Error()}.";
            return;
        }

        // Colour key: anything the renderer paints in ColorKey becomes fully transparent.
        OverlayPInvoke.SetLayeredWindowAttributes(_hwnd, OverlayNative.ColorKey, 0, OverlayNative.LWA_COLORKEY);
    }

    /// <summary>
    /// Paints once while still hidden (so no unpainted frame can flash), then shows without
    /// activating: <c>SW_SHOWNOACTIVATE</c>, and <c>SetWindowPos</c>/<c>SWP_NOACTIVATE</c> applies the
    /// topmost flag without making us the foreground window.
    /// </summary>
    internal void Show()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        OverlayPInvoke.SendMessage(_hwnd, OverlayNative.WM_PAINT, IntPtr.Zero, IntPtr.Zero);
        OverlayPInvoke.ShowWindow(_hwnd, OverlayNative.SW_SHOWNOACTIVATE);
        OverlayPInvoke.SetWindowPos(
            _hwnd,
            OverlayNative.HWND_TOPMOST,
            0, 0, 0, 0,
            OverlayNative.SWP_NOMOVE | OverlayNative.SWP_NOSIZE |
            OverlayNative.SWP_NOACTIVATE | OverlayNative.SWP_SHOWWINDOW);
    }

    /// <summary>Destroys the window; safe to call twice. Called only on the pump thread.</summary>
    internal void Destroy()
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        OverlayPInvoke.DestroyWindow(_hwnd);
        _hwnd = IntPtr.Zero;
    }

    /// <summary>The whole interaction contract of the overlay, in four messages.</summary>
    private IntPtr WindowProc(IntPtr hwnd, uint message, IntPtr wParam, IntPtr lParam)
    {
        switch (message)
        {
            case OverlayNative.WM_NCHITTEST:
                // Explicit click-through: every point reports "not mine", so the OS keeps hit-testing
                // the windows underneath. WS_EX_TRANSPARENT alone is not a sufficient guarantee.
                return OverlayNative.HTTRANSPARENT;

            case OverlayNative.WM_MOUSEACTIVATE:
                // Belt to the WS_EX_NOACTIVATE braces: even a direct click could not activate us.
                return OverlayNative.MA_NOACTIVATE;

            case OverlayNative.WM_ERASEBKGND:
                // WM_PAINT repaints fully; erasing first would flash the colour key as solid magenta.
                return (IntPtr)1;

            case OverlayNative.WM_PAINT:
                OverlayRenderer.Paint(hwnd, _draw, _options);
                return IntPtr.Zero;

            default:
                return OverlayPInvoke.DefWindowProc(hwnd, message, wParam, lParam);
        }
    }
}
