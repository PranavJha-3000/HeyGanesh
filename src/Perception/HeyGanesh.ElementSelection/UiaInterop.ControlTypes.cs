namespace HeyGanesh.ElementSelection;

// Control-type friendly names for UiaInterop (stable, documented UIA_ControlTypeIds values).

internal static partial class UiaInterop
{
    /// <summary>Friendly names for the control-type ids this module filters on. Unmapped ids fall back to "Type&lt;id&gt;" rather than a wrong name.</summary>
    internal static readonly System.Collections.Generic.Dictionary<int, string> ControlTypeNames = new()
    {
        [50000] = "Button",
        [50001] = "Calendar",
        [50002] = "CheckBox",
        [50003] = "ComboBox",
        [50004] = "Edit",
        [50005] = "Hyperlink",
        [50006] = "Image",
        [50007] = "ListItem",
        [50008] = "List",
        [50009] = "Menu",
        [50010] = "MenuBar",
        [50011] = "MenuItem",
        [50012] = "ProgressBar",
        [50013] = "RadioButton",
        [50014] = "ScrollBar",
        [50015] = "Slider",
        [50016] = "Spinner",
        [50017] = "StatusBar",
        [50018] = "Tab",
        [50019] = "TabItem",
        [50020] = "Text",
        [50021] = "ToolBar",
        [50022] = "ToolTip",
        [50023] = "Tree",
        [50024] = "TreeItem",
        [50025] = "Custom",
        [50026] = "Group",
        [50027] = "Thumb",
        [50028] = "DataGrid",
        [50029] = "DataItem",
        [50030] = "Document",
        [50031] = "SplitButton",
        [50032] = "Window",
        [50033] = "Pane",
        [50034] = "Header",
        [50035] = "HeaderItem",
        [50036] = "Table",
        [50037] = "TitleBar",
        [50038] = "Separator",
    };

    /// <summary>
    /// Reverse lookup: localized/English control-type name → canonical name, case-insensitive
    /// ("button" → "Button"). GetCachedPropertyValue(UIA_ControlTypePropertyId) has been observed to
    /// deliver the localized NAME (VT_BSTR) instead of the documented VT_I4 id on some provider
    /// stacks (measured live: "window", not 50032) — accept both shapes.
    /// </summary>
    internal static readonly System.Collections.Generic.Dictionary<string, string> ControlTypeNamesByName =
        ControlTypeNames.Values.ToDictionary(n => n, n => n, StringComparer.OrdinalIgnoreCase);

}
