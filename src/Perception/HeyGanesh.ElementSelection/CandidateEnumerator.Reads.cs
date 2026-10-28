using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;
using System.ComponentModel;

namespace HeyGanesh.ElementSelection;

// Fourth part of CandidateEnumerator: the cached-property readers the interactive filter depends on,
// plus the coarse position hint.
// (Split across files only to satisfy the editor's per-call size limit; partial by necessity.)

public sealed partial class CandidateEnumerator
{
    /// <summary>
    /// Canonical control-type name → UIA_ControlTypeIds value, built once from
    /// <see cref="UiaInterop.ControlTypeNames"/>. Needed because the cached ControlType property
    /// arrives as a NAME on some provider stacks and as an id on others (see TryReadControlType).
    /// </summary>
    private static readonly Dictionary<string, int> ControlTypeIdsByName = BuildControlTypeIdsByName();

    private static Dictionary<string, int> BuildControlTypeIdsByName()
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in UiaInterop.ControlTypeNames)
        {
            map[pair.Value] = pair.Key;
        }

        return map;
    }

    /// <summary>
    /// The element's UIA Name, trimmed. Empty or whitespace names are rejected: a nameless control
    /// has nothing for Jev to match a question against (<see cref="UiaCandidate.Name"/> is
    /// documented as never empty).
    /// </summary>
    private static bool TryReadName(UiaElement element, out string name)
    {
        name = string.Empty;
        if (element.GetCachedPropertyValue(UiaInterop.PropName) is not string value || string.IsNullOrWhiteSpace(value))
        {
            return false;
        }

        name = value.Trim();
        return true;
    }

    /// <summary>
    /// Control type, accepted in BOTH shapes it is observed in: the documented VT_I4 id and the
    /// localized VT_BSTR name (measured live as "window", not 50032 — see UiaInterop.ControlTypes.cs).
    /// An unrecognized shape is rejected rather than guessed, because a wrong name would silently
    /// slip through — or be wrongly dropped by — the interactable-type filter.
    /// </summary>
    private static bool TryReadControlType(UiaElement element, out string controlType, out int controlTypeId)
    {
        controlType = string.Empty;
        controlTypeId = 0;

        switch (element.GetCachedPropertyValue(UiaInterop.PropControlType))
        {
            case int id when UiaInterop.ControlTypeNames.TryGetValue(id, out var namedById):
                controlType = namedById;
                controlTypeId = id;
                return true;

            case string named when UiaInterop.ControlTypeNamesByName.TryGetValue(named, out var canonical) &&
                                   ControlTypeIdsByName.TryGetValue(canonical, out var idForName):
                controlType = canonical;
                controlTypeId = idForName;
                return true;

            default:
                return false; // Unknown VARIANT shape: not a type we can trust.
        }
    }

    /// <summary>
    /// Boolean cached properties (IsEnabled / IsOffscreen). VT_BOOL marshals to bool; some provider
    /// stacks hand back a VT_I4 0/1 instead, so both are accepted. Anything else — including a
    /// property that never arrived in the cache — is "unknown", and every caller fails closed on it.
    /// </summary>
    private static bool TryReadBool(UiaElement element, int propertyId, out bool value)
    {
        switch (element.GetCachedPropertyValue(propertyId))
        {
            case bool boolean:
                value = boolean;
                return true;

            case int integer when integer == 0 || integer == 1:
                value = integer == 1;
                return true;

            default:
                value = false;
                return false;
        }
    }

    /// <summary>
    /// BoundingRectangle as the documented 4-double (left, top, width, height), truncated into the
    /// same physical-pixel <see cref="PixelRect"/> shape the root uses in EnumerateCore. Unlike the
    /// root — where missing geometry is a genuine error and ReadRect throws — a single unreadable
    /// element is simply skipped. Every reader here is called under the per-element guard in
    /// BuildCandidateSet, so a stale node (UIA_E_ELEMENTNOTAVAILABLE) cannot fail the whole walk.
    /// </summary>
    private static bool TryReadRect(UiaElement element, out PixelRect rect)
    {
        rect = default;
        if (element.GetCachedPropertyValue(UiaInterop.PropBoundingRectangle) is not double[] values || values.Length != 4)
        {
            return false;
        }

        rect = new PixelRect((int)values[0], (int)values[1], (int)values[2], (int)values[3]);
        return true;
    }

    /// <summary>
    /// Coarse 3x3 placement of an element inside its window — "top-left", "center", "bottom-right",
    /// etc. This ships in the Jev option descriptions built by ElementSelectionService.Request.cs so
    /// two identically named controls in one dialog stay distinguishable WITHOUT putting raw
    /// coordinates into the prompt: Jev must never be invited to reason in pixels.
    /// </summary>
    private static string ComputePositionHint(PixelRect rect, PixelRect windowRect)
    {
        var row = WindowBand(rect.Y + rect.Height / 2, windowRect.Y, windowRect.Height, "top", "middle", "bottom");
        var column = WindowBand(rect.X + rect.Width / 2, windowRect.X, windowRect.Width, "left", "center", "right");

        // Dead centre reads better as "center" than "middle-center".
        return row == "middle" && column == "center" ? "center" : $"{row}-{column}";
    }

    /// <summary>Maps one axis centre to its third of the window. A degenerate window rect yields the middle band.</summary>
    private static string WindowBand(int value, int origin, int size, string low, string middle, string high)
    {
        if (size <= 0)
        {
            return middle; // Collapsed / minimised window: no meaningful banding.
        }

        var offset = value - origin;
        if (offset < size / 3)
        {
            return low;
        }

        return offset >= size - size / 3 ? high : middle;
    }
}