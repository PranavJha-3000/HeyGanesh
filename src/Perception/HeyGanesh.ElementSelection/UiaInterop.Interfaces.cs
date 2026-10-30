using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;
using System.Runtime.InteropServices;

namespace HeyGanesh.ElementSelection;

// COM interface declarations for UiaInterop — prefixes only (see UiaInterop.cs remarks).

[ComImport]
[Guid(UiaInterop.IUIAutomationIid)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface UiaAutomation
{
    // IUIAutomation prefix (slots 0..17), UIAutomationClient.h member order: element-resolution
    // methods first, then the tree walker, its walkers/conditions (11..16), then CreateCacheRequest.
    // Declaration truncates at 17 — nothing later is needed. The earlier layout (conditions before
    // walkers, CreateCacheRequest at 11) was proven wrong by the harness (ES series) with a clean
    // E_NOINTERFACE on cache-request cast — the wrong slot had returned a TreeWalker.
    [PreserveSig] int CompareRuntimeIds(IntPtr id1, IntPtr id2);
    [PreserveSig] int CompareElements(IntPtr el1, IntPtr el2);

    IntPtr GetRootElement();
    IntPtr GetRootElementBuildCache(IntPtr cacheRequest);
    IntPtr GetFocusedElement();
    IntPtr GetFocusedElementBuildCache(IntPtr cacheRequest);

    IntPtr GetElementFromHandle(IntPtr hwnd);
    IntPtr GetElementFromHandleBuildCache(IntPtr hwnd, IntPtr cacheRequest);
    IntPtr GetElementFromPoint(UiaInterop.POINT point);
    IntPtr GetElementFromPointBuildCache(UiaInterop.POINT point, IntPtr cacheRequest);

    // 10..13 — tree walkers (declared for slot alignment; unused)
    IntPtr CreateTreeWalker(IntPtr condition);
    IntPtr GetControlViewWalker();
    IntPtr GetContentViewWalker();
    IntPtr GetRawViewWalker();

    // 14..16 — conditions
    IntPtr GetRawViewCondition();
    object ControlViewCondition { [return: MarshalAs(UnmanagedType.Interface)] get; }
    object ContentViewCondition { [return: MarshalAs(UnmanagedType.Interface)] get; }

    // 17 — the only other member we call.
    object CreateCacheRequest { [return: MarshalAs(UnmanagedType.Interface)] get; }
}


/// <summary>
/// IUIAutomationElement PREFIX ONLY (slots 0..9). Everything else is reached via
/// GetCachedPropertyValue by property id. Never append members here against UIAutomationClient.h
/// from memory — extend only after re-verifying the slot order.
/// </summary>
[ComImport]
[Guid(UiaInterop.IUIAutomationElementIid)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface UiaElement
{
    // 0
    [PreserveSig] int SetFocus();

    // 1
    [PreserveSig] int GetRuntimeId(out IntPtr runtimeId);

    // 2..5
    IntPtr FindFirst(int scope, IntPtr condition);
    IntPtr FindAll(int scope, IntPtr condition);
    IntPtr FindFirstBuildCache(int scope, IntPtr condition, IntPtr cacheRequest);
    IntPtr FindAllBuildCache(int scope, IntPtr condition, IntPtr cacheRequest);

    // 6
    IntPtr BuildUpdatedCache(IntPtr cacheRequest);

    // 7..8
    [return: MarshalAs(UnmanagedType.Struct)]
    object GetCurrentPropertyValue(int propertyId);
    [return: MarshalAs(UnmanagedType.Struct)]
    object GetCurrentPropertyValueEx(int propertyId, bool ignoreDefaultValue);

    // 9 — the workhorse: every property this module needs is read through here.
    [return: MarshalAs(UnmanagedType.Struct)]
    object GetCachedPropertyValue(int propertyId);
}

/// <summary>IUIAutomationCacheRequest prefix (slot 0 only; the rest is never used).</summary>
[ComImport]
[Guid(UiaInterop.IUIAutomationCacheRequestIid)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface UiaCacheRequest
{
    // 0
    void AddProperty(int propertyId);
}

/// <summary>IUIAutomationElementArray: complete interface (2 members total).</summary>
[ComImport]
[Guid(UiaInterop.IUIAutomationElementArrayIid)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface UiaElementArray
{
    // 0
    int Length { get; }

    // 1
    [return: MarshalAs(UnmanagedType.Interface)]
    UiaElement GetElement(int index);
}

/// <summary>Marker interface — conditions are passed as IntPtr through FindAllBuildCache.</summary>
[ComImport]
[Guid(UiaInterop.IUIAutomationConditionIid)]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface UiaCondition
{
}
