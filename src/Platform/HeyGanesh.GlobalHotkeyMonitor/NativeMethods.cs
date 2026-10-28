using System.Runtime.InteropServices;
using Microsoft.Win32.SafeHandles;

namespace HeyGanesh.GlobalHotkeyMonitor;

/// <summary>
/// Win32 interop used by <see cref="GlobalHotkeyMonitor"/>.
/// </summary>
/// <remarks>
/// Listen-only by construction: there is no input injection here, and the only hook API
/// used is the observation-only low-level keyboard hook.
/// </remarks>
internal static class NativeMethods
{
    /// <summary>WH_KEYBOARD_LL — process-wide, callback runs on the installing thread.</summary>
    internal const int WH_KEYBOARD_LL = 13;

    internal const int WM_KEYDOWN = 0x0100;
    internal const int WM_KEYUP = 0x0101;
    internal const int WM_SYSKEYDOWN = 0x0104;
    internal const int WM_SYSKEYUP = 0x0105;
    internal const int WM_QUIT = 0x0012;

    internal const uint PM_NOREMOVE = 0x0000;

    /// <summary>LLKHF_INJECTED — the event was synthesized (SendInput / SendKeys), not typed.</summary>
    internal const uint LLKHF_INJECTED = 0x00000010;

    /// <summary>High bit of <see cref="GetAsyncKeyState"/>'s result: key is currently down.</summary>
    internal const int KeyPressedMask = unchecked((int)0x8000);

    // Modifiers, sided where the keyboard reports a side.
    internal const uint VK_SHIFT = 0x10;
    internal const uint VK_CONTROL = 0x11;
    internal const uint VK_MENU = 0x12;
    internal const uint VK_LWIN = 0x5B;
    internal const uint VK_RWIN = 0x5C;
    internal const uint VK_LSHIFT = 0xA0;
    internal const uint VK_RSHIFT = 0xA1;
    internal const uint VK_LCONTROL = 0xA2;
    internal const uint VK_RCONTROL = 0xA3;
    internal const uint VK_LMENU = 0xA4;
    internal const uint VK_RMENU = 0xA5;

    internal delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

    /// <summary>KBDLLHOOKSTRUCT — the lParam of a WH_KEYBOARD_LL notification.</summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct KbdLlHookStruct
    {
        internal uint VirtualKey;
        internal uint ScanCode;
        internal uint Flags;
        internal uint Time;
        internal UIntPtr ExtraInfo;
    }

    /// <summary>MSG — only used to keep the pump thread's queue alive; never inspected.</summary>
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
    }

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "SetWindowsHookExW")]
    internal static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn, IntPtr hMod, uint dwThreadId);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool UnhookWindowsHookEx(IntPtr hhk);

    [DllImport("user32.dll", SetLastError = true)]
    internal static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "GetMessageW")]
    internal static extern int GetMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

    [DllImport("user32.dll", SetLastError = true, CharSet = CharSet.Unicode, EntryPoint = "PeekMessageW")]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PeekMessage(out Msg lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax, uint wRemoveMsg);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    internal static extern bool PostThreadMessage(uint idThread, uint msg, IntPtr wParam, IntPtr lParam);

    /// <summary>Physical key state, used to resynchronise when the hook starts mid-hold.</summary>
    [DllImport("user32.dll")]
    internal static extern short GetAsyncKeyState(int vKey);

    [DllImport("kernel32.dll")]
    internal static extern uint GetCurrentThreadId();
}

/// <summary>
/// Owns the HHOOK so it is always released — including on exception paths — instead of
/// relying on process exit.
/// </summary>
internal sealed class SafeHookHandle : SafeHandleZeroOrMinusOneIsInvalid
{
    internal SafeHookHandle(IntPtr hookHandle)
        : base(ownsHandle: true)
    {
        SetHandle(hookHandle);
    }

    protected override bool ReleaseHandle() => NativeMethods.UnhookWindowsHookEx(handle);
}
