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

/// <summary>Any op name outside v1 (or a missing one). Never rendered; always degrades its beat.</summary>
public sealed record UnknownOp(string Name) : LessonOp;
