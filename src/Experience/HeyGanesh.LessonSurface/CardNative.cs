using System.Runtime.InteropServices;

namespace HeyGanesh.LessonSurface;

/// <summary>The few Win32 calls the card windows need: style/affinity read-back, caption dragging and rounded corners.</summary>
internal static class CardNative
{
    public const int WsExNoActivate = 0x08000000;
    public const int WsExToolWindow = 0x00000080;
    public const int WsExTopmost = 0x00000008;
    public const int GwlExStyle = -20;
    public const int WmNcLButtonDown = 0x00A1;
    public const int HtCaption = 2;

    // DWMWA_WINDOW_CORNER_PREFERENCE / DWMWCP_ROUND (Windows 11): DWM rounds the borderless card for us (DEC-062).
    public const int DwmwaWindowCornerPreference = 33;
    public const int DwmwcpRound = 2;

    // Glass (DEC-063): Acrylic behind the whole client area. Proven on a never-active card by the step 2b spike.
    public const int DwmwaUseImmersiveDarkMode = 20;
    public const int DwmwaSystemBackdropType = 38;
    public const int DwmsbtTransientWindow = 3;

    [StructLayout(LayoutKind.Sequential)]
    public struct Margins
    {
        public int Left, Right, Top, Bottom;
    }

    [DllImport("dwmapi.dll")]
    public static extern int DwmExtendFrameIntoClientArea(IntPtr hwnd, ref Margins margins);

    [DllImport("dwmapi.dll")]
    public static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int value, int size);

    [DllImport("dwmapi.dll")]
    public static extern int DwmGetWindowAttribute(IntPtr hwnd, int attribute, out int value, int size);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    public static extern int GetWindowLong(IntPtr hwnd, int index);

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool GetWindowDisplayAffinity(IntPtr hwnd, out uint affinity);

    [DllImport("user32.dll")]
    public static extern IntPtr GetForegroundWindow();

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static extern bool ReleaseCapture();

    [DllImport("user32.dll", EntryPoint = "SendMessageW")]
    public static extern IntPtr SendMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam);
}
