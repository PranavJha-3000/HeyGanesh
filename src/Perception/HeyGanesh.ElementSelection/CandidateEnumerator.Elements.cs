using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;
using System.ComponentModel;
using System.Runtime.InteropServices;

namespace HeyGanesh.ElementSelection;

// Third part of CandidateEnumerator: the DEC-008 interactive filter and the CandidateSet assembly.
// (Split across files only to satisfy the editor's per-call size limit; partial by necessity.)

public sealed partial class CandidateEnumerator
{
    /// <summary>
    /// Distills the raw control-view subtree fetched in one cached pass by EnumerateCore into the
    /// interactive <see cref="CandidateSet"/> the service ranks and Jev then chooses from.
    /// </summary>
    /// <remarks>
    /// Filter order is cheap-first and follows DEC-008: a non-empty name, a usable rectangle,
    /// on-screen, enabled, and a control type in the interactable set (Text / Pane / Group / Custom
    /// are deliberately absent from that set — dense text is the Vision fallback's job).
    ///
    /// Ids stay EMPTY here. "c001.." is assigned by <see cref="ElementSelectionService"/> after
    /// ranking, because an id has to mean rank position, not tree position.
    ///
    /// Failure policy: a single unreadable element is skipped, never fatal. The documented
    /// Vision-fallback triggers are window-level (no window resolved, UIA access denied by UIPI,
    /// empty tree), so one broken node must not turn a usable window into a failure. The
    /// zero-survivor case is still surfaced as Failed, one layer up.
    ///
    /// COM lifetime: <paramref name="found"/> and the per-element RCWs belong to the caller —
    /// EnumerateCore releases the array pointer in its finally block. Nothing here may release or
    /// re-wrap them.
    /// </remarks>
    private CandidateSet BuildCandidateSet(
        UiaElementArray found,
        IntPtr hwnd,
        string title,
        PixelRect windowRect,
        PixelRect rootPixelRect)
    {
        var allowed = _options.InteractableControlTypes ?? SelectionOptions.DefaultInteractableControlTypes;
        var total = found.Length;
        var candidates = new List<UiaCandidate>(Math.Min(total, 64));

        for (int i = 0; i < total; i++)
        {
            UiaElement element;
            try
            {
                element = found.GetElement(i);
            }
            catch (COMException)
            {
                // Node died or is mid-destruction between the cached pass and this read: skip it and
                // keep the walk alive.
                continue;
            }

            try
            {
                if (TryBuildCandidate(element, allowed, windowRect, out var candidate))
                {
                    candidates.Add(candidate);
                }
            }
            catch (COMException)
            {
                // UIA_E_ELEMENTNOTAVAILABLE (0x80040201) is the normal outcome of reading a node that
                // went stale between the cached pass and this read — a live window mutates while we
                // walk it. One stale node must not abort the whole window, so it is counted in
                // TotalEnumerated and simply left out of the candidates.
                continue;
            }
            catch (InvalidCastException)
            {
                continue; // A VARIANT shape this module does not model.
            }
        }

        var target = new TargetWindowInfo(hwnd, title, windowRect, rootPixelRect);
        return new CandidateSet(target, candidates, total, candidates.Count);
    }

    /// <summary>
    /// One element → zero or one candidate. A false result means "not an interactive target" or
    /// "its cached properties were not usable" — a normal outcome for a tree walk, not an error.
    /// </summary>
    private static bool TryBuildCandidate(
        UiaElement element,
        IReadOnlySet<string> allowedControlTypes,
        PixelRect windowRect,
        out UiaCandidate candidate)
    {
        candidate = null!;

        if (!TryReadName(element, out var name))
        {
            return false; // Nameless elements are useless to a selector (UiaCandidate.Name is never empty).
        }

        if (!TryReadRect(element, out var rect) || rect.Width <= 0 || rect.Height <= 0)
        {
            return false; // No cached geometry, or a collapsed element: nothing to point at.
        }

        if (!TryReadBool(element, UiaInterop.PropIsOffscreen, out var isOffscreen) || isOffscreen)
        {
            return false; // Fail closed: an unreadable IsOffscreen is treated as "not visible".
        }

        if (!TryReadBool(element, UiaInterop.PropIsEnabled, out var isEnabled) || !isEnabled)
        {
            return false;
        }

        if (!TryReadControlType(element, out var controlType, out var controlTypeId) ||
            !allowedControlTypes.Contains(controlType))
        {
            return false;
        }

        candidate = new UiaCandidate(
            Id: string.Empty,
            Name: name,
            ControlType: controlType,
            ControlTypeId: controlTypeId,
            Rect: rect,
            PositionHint: ComputePositionHint(rect, windowRect));
        return true;
    }
}
