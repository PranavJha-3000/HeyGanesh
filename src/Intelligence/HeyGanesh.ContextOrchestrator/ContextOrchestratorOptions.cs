using HeyGanesh.ElementSelection;

namespace HeyGanesh.ContextOrchestrator;

/// <summary>
/// Tuning for <see cref="ContextOrchestratorService"/>. Mirrors the shape of the other modules'
/// options types (immutable, set once).
/// </summary>
/// <remarks>
/// Deliberately holds no confidence threshold, no timeout of its own, and no fallback rule: the
/// 0.8/0.5 tiers belong to the legs, and the decision to fall back is the documented failure contract
/// of Element Selection, not a policy this module invents (DEC-009). The confidence-gate that will
/// eventually branch on a tier is a future Jev component and does not exist yet.
/// </remarks>
public sealed class ContextOrchestratorOptions
{
    /// <summary>Defaults: Element Selection's own target (cursor window), capture frames retained on the result.</summary>
    public static ContextOrchestratorOptions Default { get; } = new();

    /// <summary>
    /// The window the PRIMARY leg walks. Null (the default) leaves the choice to Element Selection,
    /// which defaults to the window under the cursor — the same place the capture module's
    /// cursor-first monitor ordering is looking, so the two legs agree about "where the user is".
    /// </summary>
    public SelectionTarget? Target { get; init; }

    /// <summary>
    /// Whether the frames captured for this session ride along on the returned
    /// <see cref="GuidanceResult"/>. On by default: the Screen Overlay will need the frame the
    /// coordinates were resolved against, and a bug report needs it more. Turn it off only if a caller
    /// cares about the verdict alone and wants to avoid handing out a JPEG of the user's desktop.
    /// </summary>
    public bool RetainCaptures { get; init; } = true;
}
