using HeyGanesh.ScreenCapture;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace HeyGanesh.ElementSelection;

/// <summary>
/// Enumerates the UI Automation tree of the target window and distills it into interactive
/// candidates, using the hand-declared interop in <see cref="UiaInterop"/> (see its GUID/vtable
/// containment remarks). The whole subtree is fetched in ONE cross-process pass via a cache request:
/// per-property access would mean one cross-process round trip each, which is how UIA gets its
/// reputation for being slow.
/// </summary>
public sealed partial class CandidateEnumerator
{
    private readonly SelectionOptions _options;

    public CandidateEnumerator(SelectionOptions? options = null)
    {
        _options = options ?? SelectionOptions.Default;
    }

    /// <summary>
    /// Walks the target window's UIA tree. Throws <see cref="Win32Exception"/> if the window rect
    /// cannot be read, and <see cref="InvalidOperationException"/> when no window can be resolved or
    /// UIA denies the tree (elevated/protected host — the UIPI blind spot, same family as the
    /// hotkey/capture ones). Callers treat that as "no candidates", i.e. the documented
    /// Vision-fallback trigger, not as a crash.
    /// </summary>
    public CandidateSet Enumerate(SelectionTarget? target = null)
    {
        target ??= SelectionTarget.CursorWindow;

        var hwnd = ResolveWindow(target.Mode);
        if (hwnd == IntPtr.Zero)
        {
            throw new InvalidOperationException(
                "No target window could be resolved (nothing under the cursor / no foreground window).");
        }

        if (!NativeMethods.GetWindowRect(hwnd, out var winRect))
        {
            throw new Win32Exception(Marshal.GetLastWin32Error(), $"GetWindowRect failed for hwnd 0x{hwnd:X}.");
        }

        var title = ReadWindowTitle(hwnd);
        var windowRect = new PixelRect(
            winRect.Left, winRect.Top, winRect.Right - winRect.Left, winRect.Bottom - winRect.Top);

        object automationObj = new UiaInterop.CUIAutomation8();
        var automation = (UiaAutomation)automationObj;

        var cacheRequestObj = automation.CreateCacheRequest;
        var cacheRequest = (UiaCacheRequest)cacheRequestObj;
        var cacheRequestPtr = Marshal.GetIUnknownForObject(cacheRequest);
        cacheRequest.AddProperty(UiaInterop.PropName);
        cacheRequest.AddProperty(UiaInterop.PropControlType);
        cacheRequest.AddProperty(UiaInterop.PropBoundingRectangle);
        cacheRequest.AddProperty(UiaInterop.PropIsEnabled);
        cacheRequest.AddProperty(UiaInterop.PropIsOffscreen);

        // ControlViewCondition is the same condition the SDK's own tree walkers use: it excludes
        // raw/unexposed nodes. Returned as a plain COM object; passed on as its IUnknown.
        var conditionObj = automation.ControlViewCondition;
        var conditionPtr = Marshal.GetIUnknownForObject(conditionObj);

        try
        {
            return EnumerateCore(automation, hwnd, title, windowRect, cacheRequestPtr, conditionPtr);
        }
        finally
        {
            Marshal.Release(conditionPtr);
            Marshal.FinalReleaseComObject(conditionObj);
            Marshal.Release(cacheRequestPtr);
            Marshal.FinalReleaseComObject(cacheRequest);
        }
    }
}
