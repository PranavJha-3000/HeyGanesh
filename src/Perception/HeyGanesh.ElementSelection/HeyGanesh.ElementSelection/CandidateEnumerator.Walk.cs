using HeyGanesh.ScreenCapture;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Text;

namespace HeyGanesh.ElementSelection;

// Second half of CandidateEnumerator: tree fetch, element walk, and helpers.
// (Split across files only to satisfy the editor's per-call size limit; partial by necessity.)

public sealed partial class CandidateEnumerator
{
    private CandidateSet EnumerateCore(
        UiaAutomation automation,
        IntPtr hwnd,
        string title,
        PixelRect windowRect,
        IntPtr cacheRequestPtr,
        IntPtr conditionPtr)
    {
        UiaElement rootElement;
        try
        {
            // GetElementFromHandleBuildCache prefetches the root's own properties in the same
            // cross-process call. An elevated window denies this from a non-elevated process (UIPI).
            var rootPtr = automation.GetElementFromHandleBuildCache(hwnd, cacheRequestPtr);
            rootElement = (UiaElement)Marshal.GetObjectForIUnknown(rootPtr);
            Marshal.Release(rootPtr);
        }
        catch (COMException ex)
        {
            throw new InvalidOperationException(
                $"UIA could not open the tree of hwnd 0x{hwnd:X} ('{title}'). Elevated or protected " +
                "windows deny access from a non-elevated process (UIPI) -- treat as 'no candidates' " +
                "and defer to the Vision fallback (DEC-008).", ex);
        }

        var rootRect = ReadRect(rootElement);
        var rootPixelRect = new PixelRect((int)rootRect[0], (int)rootRect[1], (int)rootRect[2], (int)rootRect[3]);

        var foundPtr = rootElement.FindAllBuildCache(UiaInterop.TreeScopeSubtree, conditionPtr, cacheRequestPtr);
        try
        {
            var found = (UiaElementArray)Marshal.GetObjectForIUnknown(foundPtr);
            return BuildCandidateSet(found, hwnd, title, windowRect, rootPixelRect);
        }
        finally
        {
            Marshal.Release(foundPtr);
        }
    }

    /// <summary>
    /// BoundingRectangle arrives as a 4-double SAFEARRAY (left, top, width, height) inside a VARIANT —
    /// the documented shape of UIA's UIA_BoundingRectanglePropertyId.
    /// </summary>
    private static double[] ReadRect(UiaElement element)
    {
        if (element.GetCachedPropertyValue(UiaInterop.PropBoundingRectangle) is not double[] rect || rect.Length != 4)
        {
            throw new InvalidOperationException("BoundingRectangle cache read returned an unexpected shape.");
        }

        return rect;
    }

    private static IntPtr ResolveWindow(SelectionTargetMode mode)
    {
        if (mode == SelectionTargetMode.ForegroundWindow)
        {
            return NativeMethods.GetForegroundWindow();
        }

        if (!NativeMethods.GetCursorPos(out var point))
        {
            return IntPtr.Zero;
        }

        var hwnd = NativeMethods.WindowFromPoint(point);
        return hwnd == IntPtr.Zero ? IntPtr.Zero : NativeMethods.GetAncestor(hwnd, NativeMethods.GA_ROOT);
    }

    private static string ReadWindowTitle(IntPtr hwnd)
    {
        var length = NativeMethods.GetWindowTextLength(hwnd);
        if (length <= 0)
        {
            return string.Empty;
        }

        var sb = new StringBuilder(length + 1);
        _ = NativeMethods.GetWindowText(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }
}
