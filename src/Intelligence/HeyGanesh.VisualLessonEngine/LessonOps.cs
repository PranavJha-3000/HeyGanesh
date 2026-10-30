using HeyGanesh.Abstractions;

namespace HeyGanesh.VisualLessonEngine;

public enum TextStyle
{
    Body = 0,
    Heading = 1,
    Note = 2,
}

public enum MarkShape
{
    Circle = 0,
    Underline = 1,
    Arrow = 2,
}

/// <summary>
/// One drawing operation. The set is closed on purpose: a renderer implements exactly these, and an
/// op name the model invents parses to <see cref="UnknownOp"/> so the validator can drop it visibly
/// instead of the renderer guessing.
/// </summary>
public abstract record LessonOp;

/// <summary>Writes text (optionally with TeX math) on a card, creating element <paramref name="Id"/>.</summary>
public sealed record WriteOp(string Surface, string Id, string Text, TextStyle Style, string? Math) : LessonOp;

/// <summary>A numbered step (1-based) on a card.</summary>
public sealed record StepOp(string Surface, string Id, int Index, string Text) : LessonOp;

/// <summary>An arrow between two existing elements on a card.</summary>
public sealed record ArrowOp(string Surface, string From, string To, string? Label) : LessonOp;

/// <summary>A hand-drawn icon from the fixed vocabulary (<see cref="LessonVocabulary.Glyphs"/>).</summary>
public sealed record IconOp(string Surface, string Id, string Glyph) : LessonOp;

/// <summary>
/// A function plot. The model supplies the expression and range only; the app computes the points,
/// so a plotted curve can never be a hallucinated one.
/// </summary>
public sealed record PlotOp(string Surface, string Id, string Expr, double XMin, double XMax) : LessonOp;

/// <summary>Draws attention to an element that exists at this beat.</summary>
public sealed record HighlightOp(string Target) : LessonOp;

/// <summary>A mark on the student's own screen, in global physical pixels (PerMonitorV2).</summary>
public sealed record ScreenMarkOp(MarkShape Shape, PixelRect Region) : LessonOp;

/// <summary>Wipes a card; its elements stop existing for later beats.</summary>
public sealed record ClearOp(string Surface) : LessonOp;

/// <summary>Where a 3D camera looks from. Closed, so a renderer has exactly these four framings to implement.</summary>
public enum CameraView
{
    Front = 0,
    Side = 1,
    Top = 2,
    Iso = 3,
}

// The 3D ops (schema 1.1, DEC-028). Every name they carry — model, part, animation — is checked against
// the caller's ModelCatalog before a renderer sees it, so a scene can only ever be built from vetted
// content. A part is addressed as "<instanceId>.<partId>"; element ids cannot contain '.', so a part
// reference never collides with an element id.

/// <summary>Places catalogue model <paramref name="Model"/> in a scene, creating instance <paramref name="Id"/>.</summary>
public sealed record ModelOp(string Surface, string Id, string Model) : LessonOp;

/// <summary>A sticky-note label on a leader line, attached to a live instance or one of its parts.</summary>
public sealed record LabelOp(string Surface, string Id, string Target, string Text) : LessonOp;

/// <summary>Pulls a live instance's parts apart: 0 assembled, 1 fully exploded.</summary>
public sealed record ExplodeOp(string Target, double Amount) : LessonOp;

/// <summary>Plays one of the instance's catalogue animations (e.g. an engine's stroke cycle).</summary>
public sealed record AnimateOp(string Target, string Animation) : LessonOp;

/// <summary>Frames a live instance or part from a fixed viewpoint.</summary>
public sealed record CameraOp(string Surface, string Focus, CameraView View) : LessonOp;

/// <summary>Any op name outside the contract (or a missing one). Never rendered; always degrades its beat.</summary>
public sealed record UnknownOp(string Name) : LessonOp;
