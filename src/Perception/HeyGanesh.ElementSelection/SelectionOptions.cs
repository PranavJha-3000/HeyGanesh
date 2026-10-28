namespace HeyGanesh.ElementSelection;

/// <summary>Which window the candidate enumeration walks.</summary>
public enum SelectionTargetMode
{
    /// <summary>The top-level window under the mouse cursor — the default, because that is where the user is looking (same principle as the capture module's cursor-first ordering).</summary>
    CursorWindow,

    /// <summary>The window that currently has keyboard focus.</summary>
    ForegroundWindow,
}

/// <summary>Target window selection for one <see cref="ElementSelectionService.SelectAsync"/> call.</summary>
public sealed record SelectionTarget(SelectionTargetMode Mode = SelectionTargetMode.CursorWindow)
{
    public static SelectionTarget CursorWindow { get; } = new(SelectionTargetMode.CursorWindow);
    public static SelectionTarget ForegroundWindow { get; } = new(SelectionTargetMode.ForegroundWindow);
}

/// <summary>
/// Tuning for <see cref="ElementSelectionService"/> and <see cref="TypeSafeJevClient"/> — mirrors the
/// shape of <c>VisionOptions</c>. No API key lives here: the key is resolved from the process
/// environment inside the client (DEC-008 extends DEC-007's stopgap pattern to the second key).
/// </summary>
public sealed class SelectionOptions
{
    /// <summary>Defaults: <c>jev-1.13.0</c>, 120-candidate cap, none-option on.</summary>
    public static SelectionOptions Default { get; } = new();

    /// <summary>
    /// Upper bound on candidates offered to Jev. A dense window can enumerate hundreds of named
    /// interactive elements; the cap keeps the request comfortably inside the provider's ~32k-token
    /// state budget. The excess is dropped by the pre-rank (see <see cref="CandidateRanker"/>) —
    /// Jev never sees the raw tree.
    /// </summary>
    public int MaxCandidates { get; init; } = 120;

    /// <summary>
    /// Model identifier for the TypeSafe System One API. Only fully versioned ids or aliases are
    /// valid: <c>jev-1.13.0</c>, <c>jev-latest</c>, <c>jev-preview</c> — <c>jev-1.13</c> is rejected
    /// by the service with HTTP 400 (confirmed against the API, do not "fix" this back).
    /// </summary>
    public string ModelId { get; init; } = "jev-1.13.0";

    /// <summary>System One base URL. Override to route through the future API key security proxy.</summary>
    public string BaseUrl { get; init; } = "https://api.typesafe.ai";

    /// <summary>How long a single provider call may take before it is abandoned.</summary>
    public TimeSpan PerCallTimeout { get; init; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// Control types worth offering to Jev. Null = <see cref="DefaultInteractableControlTypes"/>.
    /// </summary>
    public IReadOnlySet<string>? InteractableControlTypes { get; init; }

    /// <summary>
    /// Whether a synthetic <c>none</c> option is appended to the Jev choice. Load-bearing: without it
    /// Jev would be forced to mis-pick when nothing on screen matches, and an honest
    /// <c>Found = false</c> would be impossible (DEC-008).
    /// </summary>
    public bool IncludeNoneOption { get; init; } = true;

    /// <summary>
    /// The built-in interactable set. Text / Pane / Group / Custom are deliberately excluded: they
    /// are the bulk of dense trees and are not interaction targets. "Where does it say X" questions
    /// are the Vision fallback's strength (visual text grounding), not this module's — that split is
    /// part of the fallback composition contract in DEC-008.
    /// </summary>
    public static IReadOnlySet<string> DefaultInteractableControlTypes { get; } =
        new HashSet<string>(StringComparer.Ordinal)
        {
            "Button", "CheckBox", "ComboBox", "Edit", "Hyperlink", "ListItem", "MenuItem",
            "RadioButton", "Slider", "Spinner", "SplitButton", "TabItem", "TreeItem", "DataItem",
        };
}
