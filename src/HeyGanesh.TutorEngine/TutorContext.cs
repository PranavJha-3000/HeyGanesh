namespace HeyGanesh.TutorEngine;

/// <summary>
/// The caller's confidence tier, carried through as a three-tier label. Structurally identical to the
/// legs' and the orchestrator's tiers (the 0.8/0.5 boundary family) but a local type: the Tutor
/// receives the tier, never the enum that produced it, so ES, Vision and ContextOrchestrator all stay
/// outside this assembly's compile-time world (DEC-008/DEC-009/DEC-015).
/// </summary>
public enum TutorConfidenceTier
{
    Low,
    Medium,
    High,
}

/// <summary>
/// Supplemental screen grounding for one teaching turn — optional in the deep sense: an empty context
/// (Found=false, nothing populated, no visual) is a completely valid input, because the question is
/// the teaching task and plenty of questions need no screen at all.
/// </summary>
/// <remarks>
/// <para>
/// <b>Why a snapshot and not <c>GuidanceResult</c>.</b> A reference to ContextOrchestrator would weld
/// the teaching layer to the session state machine and drag capture and leg types along with it. This
/// is the same scalar-snapshot reasoning the plan settled, kept deliberately minimal so that when the
/// orchestrator evolves, mapping new signals into here is a conscious decision, not silent drift.
/// The future shell mapping reads like this, and it lives in the shell, not here:
/// <code>
/// new TutorContext
/// {
///     Found           = guidance.Found,
///     Description     = guidance.Description,
///     BoxOnScreen*    = guidance.BoxOnScreen?.{X,Y,Width,Height},
///     CenterOnScreen* = guidance.CenterOnScreen?.{X,Y},
///     ConfidenceTier  = guidance.Confidence switch { High => High, Medium => Medium, _ => Low },
///     SourceLeg       = guidance.Source.ToString(),
///     ModelId         = guidance.ModelId,
/// }
/// </code>
/// A failed guidance verdict must never be mapped into this type at all — the caller skips the Tutor
/// on <c>Failed</c> (branch first: a Vision not-found can arrive as Found=false WITH Failed=true, the
/// CO-6 quirk), the same way the overlay draws nothing on failure. Teaching from "no verdict" is
/// fabrication; teaching the general concept behind the question is not.
/// </para>
/// <para>
/// <b>Coordinates are metadata, never prose.</b> The box/center fields exist so the provider can
/// ground a screen-referential question and so downstream consumers (the overlay, the future lesson
/// engine) can act. They are not teaching vocabulary: the system prompt forbids stating pixel
/// coordinates, and the explanation refers to things by name and role.
/// </para>
/// </remarks>
public sealed record TutorContext
{
    /// <summary>The caller's "no screen data" context — valid for any question that needs none.</summary>
    public static TutorContext Empty { get; } = new();

    /// <summary>Whether the caller resolved an element/region. False is a real answer, not an error.</summary>
    public bool Found { get; init; }

    /// <summary>What the caller's leg said it found ("the Save button", "code editor"). Untrusted screen text.</summary>
    public string? Description { get; init; }

    /// <summary>Resolved box in global physical pixels (all four null when nothing was boxed).</summary>
    public int? BoxOnScreenX { get; init; }

    public int? BoxOnScreenY { get; init; }
    public int? BoxOnScreenWidth { get; init; }
    public int? BoxOnScreenHeight { get; init; }

    /// <summary>Center of the box in global physical pixels (both null when nothing was boxed).</summary>
    public int? CenterOnScreenX { get; init; }

    public int? CenterOnScreenY { get; init; }

    /// <summary>Confidence tier carried from the caller's verdict. Defaults Low because absence is Low.</summary>
    public TutorConfidenceTier ConfidenceTier { get; init; } = TutorConfidenceTier.Low;

    /// <summary>Label of the leg that produced the grounding ("ElementSelection", "Vision"); null when nothing resolved.</summary>
    public string? SourceLeg { get; init; }

    /// <summary>Provider model id behind that label, for diagnostics. Never a key, never a URL.</summary>
    public string? ModelId { get; init; }

    /// <summary>
    /// Optional bounded visual derived by the caller from an already-retained capture — a crop around the
    /// box, or a downscaled frame for "explain what is shown here". The Tutor never captures, decodes, or
    /// re-reads the screen: these bytes arrive fully detached from their source, and a turn without a
    /// visual stays the normal, cheap path (DEC-015).
    /// </summary>
    public TutorVisual? Visual { get; init; }

    /// <summary>
    /// Structural validation only (shape, not size — size caps live with the service and its options).
    /// All-or-nothing on the box and center: half a rectangle is exactly the provenance drift that made
    /// the legs keep their coordinates in one type. Caller bug → <see cref="ArgumentException"/>.
    /// </summary>
    internal void ValidateStructure()
    {
        var box = new[] { BoxOnScreenX, BoxOnScreenY, BoxOnScreenWidth, BoxOnScreenHeight };
        var boxSet = box.Count(v => v is not null);
        if (boxSet is > 0 and < 4)
        {
            throw new ArgumentException(
                "A screen box is all four values or none — a half-known rectangle cannot be taught as fact.",
                nameof(BoxOnScreenX));
        }

        if (boxSet == 4 && (BoxOnScreenWidth <= 0 || BoxOnScreenHeight <= 0))
        {
            throw new ArgumentException("Screen box dimensions must be positive.", nameof(BoxOnScreenWidth));
        }

        var centerSet = (CenterOnScreenX is null) == (CenterOnScreenY is null);
        if (!centerSet)
        {
            throw new ArgumentException(
                "A screen center is a pair or nothing at all.",
                nameof(CenterOnScreenX));
        }

        if (CenterOnScreenX is not null && boxSet == 0)
        {
            throw new ArgumentException(
                "A center without a box is ungrounded metadata: supply the box or drop the center.",
                nameof(CenterOnScreenX));
        }
    }
}