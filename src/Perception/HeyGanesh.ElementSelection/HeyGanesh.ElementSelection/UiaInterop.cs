using HeyGanesh.ScreenCapture;
using System.Runtime.InteropServices;

namespace HeyGanesh.ElementSelection;

/// <summary>
/// Hand-declared classic-COM interop for UI Automation (UIAutomationClient.dll).
/// </summary>
/// <remarks>
/// GUID provenance — every one below was read from this machine's registry before being committed
/// (HKCR\Interface\{iid} friendly names; HKCR\CLSID\{clsid} "UIAutomation8 Client Central Class").
/// Do not "fix" a mismatch against memory or blog posts: the registry is the ground truth.
///
/// Vtable containment rule: only interface PREFIXES are declared. All element properties are read
/// through GetCachedPropertyValue (slot 9) by property id — the declaration never reaches the deep
/// property-getter region, which is where a mis-ordered slot would corrupt reads. BoundingRectangle
/// comes back as the documented 4-double SAFEARRAY (left, top, width, height) via VARIANT marshaling.
/// The ES harness series proves these declarations against a real window on every run.
/// </remarks>
internal static partial class UiaInterop
{
    // ---- registry-verified IIDs / CLSIDs --------------------------------------

    /// <summary>IUIAutomation (HKCR\Interface "IUIAutomation").</summary>
    internal const string IUIAutomationIid = "30CBE57D-D9D0-452A-AB13-7AC5AC4825EE";

    /// <summary>IUIAutomationElement (HKCR\Interface "IUIAutomationElement").</summary>
    internal const string IUIAutomationElementIid = "D22108AA-8AC5-49A5-837B-37BBB3D7591E";

    /// <summary>IUIAutomationCacheRequest (HKCR\Interface "IUIAutomationCacheRequest").</summary>
    internal const string IUIAutomationCacheRequestIid = "B32A92B5-BC25-4078-9C08-D7EE95C48E03";

    /// <summary>IUIAutomationElementArray (HKCR\Interface "IUIAutomationElementArray").</summary>
    internal const string IUIAutomationElementArrayIid = "14314595-B4BC-4055-95F2-58F2E42C9855";

    /// <summary>IUIAutomationCondition — marker interface for condition objects.</summary>
    internal const string IUIAutomationConditionIid = "352FFBA8-0973-437C-A61F-F64CAFD81DF9";

    /// <summary>CUIAutomation8 coclass (HKCR\CLSID "UIAutomation8 Client Central Class").</summary>
    internal const string CUIAutomation8Clsid = "E22AD333-B25F-460C-83D0-0581107395C9";

    // ---- property ids (stable, documented UIA_PropertyIds) --------------------

    internal const int PropBoundingRectangle = 30001;
    internal const int PropControlType = 30004;
    internal const int PropName = 30005;
    internal const int PropIsOffscreen = 30008;
    internal const int PropIsEnabled = 30010;

    // ---- tree scopes (TreeScope_Subtree) --------------------------------------

    internal const int TreeScopeSubtree = 7;

    /// <summary>CoClass activator: CoCreateInstance(CUIAutomation8) cast to the base IUIAutomation.</summary>
    [ComImport]
    [Guid(CUIAutomation8Clsid)]
    internal sealed class CUIAutomation8
    {
    }

    [StructLayout(LayoutKind.Sequential)]
    internal struct POINT
    {
        public int X;
        public int Y;
    }
}
