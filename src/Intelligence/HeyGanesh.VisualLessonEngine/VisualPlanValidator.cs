using System.Text.RegularExpressions;
using HeyGanesh.Abstractions;

namespace HeyGanesh.VisualLessonEngine;

/// <summary>What the caller may do with a validated plan.</summary>
public enum LessonOutcome
{
    /// <summary>Draw and speak every beat as authored.</summary>
    Valid = 0,

    /// <summary>Some beats lost their drawing (speech kept); draw and speak the rest.</summary>
    PartiallyDegraded = 1,

    /// <summary>Too broken to draw: speak <see cref="LessonValidationResult.FallbackText"/> with a notice.</summary>
    TextFallback = 2,

    /// <summary>Nothing usable, not even speech.</summary>
    Rejected = 3,
}

/// <summary>
/// The validator's verdict. <see cref="Plan"/> is the only plan a renderer may receive (null for
/// TextFallback and Rejected); <see cref="FallbackText"/> is what to say when there is nothing to draw.
/// </summary>
public sealed record LessonValidationResult(
    LessonOutcome Outcome,
    VisualPlan? Plan,
    string FallbackText,
    IReadOnlyList<LessonIssue> Issues);

/// <summary>
/// Holds a parsed plan to the v1 contract. Issues are emitted in plan order — surfaces, then beats,
/// then each beat's ops — so two runs over the same input report the same sequence (LS-6).
/// Never throws on plan content; only a null argument (a caller bug) throws.
/// </summary>
public sealed class VisualPlanValidator
{
    public const string SupportedSchema = "heyganesh.visualplan/1";

    private static readonly Regex IdPattern = new(@"^[a-z][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant);

    private readonly LessonLimits _limits;

    public VisualPlanValidator(LessonLimits? limits = null)
    {
        _limits = limits ?? LessonLimits.Default;
    }

    public LessonValidationResult Validate(ParseResult parsed, PixelRect virtualDesktop)
    {
        ArgumentNullException.ThrowIfNull(parsed);

        var issues = new List<LessonIssue>(parsed.Issues);
        if (parsed.Plan is null)
        {
            return new LessonValidationResult(LessonOutcome.Rejected, null, string.Empty, issues);
        }

        var plan = parsed.Plan;
        CheckPlan(plan, issues);
        var degraded = CheckBeats(plan, issues, virtualDesktop);
        return Decide(plan, issues, degraded);
    }

    private void CheckPlan(VisualPlan plan, List<LessonIssue> issues)
    {
        if (!string.Equals(plan.Schema, SupportedSchema, StringComparison.Ordinal))
        {
            issues.Add(Plan(LessonIssueCodes.SchemaUnsupported, $"schema '{plan.Schema}' is not {SupportedSchema}"));
        }

        CheckText(plan.Title, _limits.MaxTitleChars, "title", null, issues);

        if (plan.Surfaces.Count > _limits.MaxSurfaces)
        {
            issues.Add(Plan(LessonIssueCodes.LimitSurfaces, $"{plan.Surfaces.Count} surfaces (max {_limits.MaxSurfaces})"));
        }

        if (plan.Beats.Count == 0 || plan.Beats.Count > _limits.MaxBeats)
        {
            issues.Add(Plan(LessonIssueCodes.LimitBeats, $"{plan.Beats.Count} beats (1..{_limits.MaxBeats})"));
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var surface in plan.Surfaces)
        {
            CheckId(surface.Id, "surface", null, seen, issues);
            CheckText(surface.Title, _limits.MaxTitleChars, "surface title", null, issues);
        }
    }

    /// <summary>
    /// Running state of the beat walk. Element ids are unique plan-wide (<see cref="EverUsed"/>), but
    /// only elements still on a card (<see cref="Live"/>) can be pointed at — a clear removes them,
    /// and an element written in a later beat does not exist yet. That ordering is what lets a
    /// renderer play beats strictly in sequence without ever meeting a dangling reference.
    /// </summary>
    private sealed class Board
    {
        public required HashSet<string> Surfaces { get; init; }
        public required PixelRect Desktop { get; init; }
        public HashSet<string> EverUsed { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Live { get; set; } = new(StringComparer.Ordinal);
    }

    /// <summary>Walks the beats in order and returns the indices of beats that lose their drawing.</summary>
    private HashSet<int> CheckBeats(VisualPlan plan, List<LessonIssue> issues, PixelRect desktop)
    {
        var degraded = new HashSet<int>();
        var parserFlagged = issues.Where(i => i.BeatId is not null).Select(i => i.BeatId!).ToHashSet(StringComparer.Ordinal);
        var beatIds = new HashSet<string>(StringComparer.Ordinal);
        var board = new Board { Surfaces = plan.Surfaces.Select(s => s.Id).ToHashSet(StringComparer.Ordinal), Desktop = desktop };
        for (var index = 0; index < plan.Beats.Count; index++)
        {
            var beat = plan.Beats[index];
            var issuesBefore = issues.Count;
            var liveBefore = new Dictionary<string, string>(board.Live, StringComparer.Ordinal);
            CheckId(beat.Id, "beat", beat.Id, beatIds, issues);
            CheckText(beat.Say, _limits.MaxSayChars, "say", beat.Id, issues);
            if (beat.Ops.Count > _limits.MaxOpsPerBeat)
            {
                issues.Add(new LessonIssue(LessonIssueCodes.LimitOps, beat.Id, $"{beat.Ops.Count} ops (max {_limits.MaxOpsPerBeat})"));
            }

            foreach (var op in beat.Ops)
            {
                CheckOp(op, beat.Id, board, issues);
            }

            // A degraded beat is never drawn, so nothing it wrote or cleared happens: roll the board
            // back, and any later beat pointing at its elements fails its own reference check.
            if (issues.Count > issuesBefore || parserFlagged.Contains(beat.Id))
            {
                degraded.Add(index);
                board.Live = liveBefore;
            }
        }

        return degraded;
    }

    private void CheckOp(LessonOp op, string beatId, Board board, List<LessonIssue> issues)
    {
        switch (op)
        {
            case WriteOp w:
                Create(w.Surface, w.Id, beatId, board, issues);
                CheckText(w.Text, _limits.MaxTextChars, "write text", beatId, issues);
                CheckOptionalText(w.Math, _limits.MaxTextChars, "math", beatId, issues);
                break;
            case StepOp s:
                Create(s.Surface, s.Id, beatId, board, issues);
                CheckText(s.Text, _limits.MaxTextChars, "step text", beatId, issues);
                if (s.Index < 1)
                {
                    issues.Add(new LessonIssue(LessonIssueCodes.ValueInvalid, beatId, $"step index {s.Index} (1-based)"));
                }

                break;
            case IconOp i:
                Create(i.Surface, i.Id, beatId, board, issues);
                if (!LessonVocabulary.Glyphs.Contains(i.Glyph))
                {
                    issues.Add(new LessonIssue(LessonIssueCodes.GlyphUnknown, beatId, $"glyph '{i.Glyph}'"));
                }

                break;
            case PlotOp p:
                Create(p.Surface, p.Id, beatId, board, issues);
                CheckText(p.Expr, _limits.MaxTextChars, "plot expr", beatId, issues);
                // The app computes the curve (PlotExpression), so an expression it cannot parse is
                // refused here — the beat degrades before a renderer ever sees it.
                if (!string.IsNullOrWhiteSpace(p.Expr) && p.Expr.Length <= _limits.MaxTextChars && !PlotExpression.TryParse(p.Expr, out _, out var exprError))
                {
                    issues.Add(new LessonIssue(LessonIssueCodes.ValueInvalid, beatId, $"plot expr: {exprError}"));
                }

                if (!(double.IsFinite(p.XMin) && double.IsFinite(p.XMax) && p.XMin < p.XMax))
                {
                    issues.Add(new LessonIssue(LessonIssueCodes.ValueInvalid, beatId, $"plot range [{p.XMin}, {p.XMax}]"));
                }

                break;
            case ArrowOp a:
                CheckSurface(a.Surface, beatId, board, issues);
                CheckOptionalText(a.Label, _limits.MaxTextChars, "arrow label", beatId, issues);
                CheckTarget(a.From, beatId, board, issues, a.Surface);
                CheckTarget(a.To, beatId, board, issues, a.Surface);
                break;
            case HighlightOp h:
                CheckTarget(h.Target, beatId, board, issues);
                break;
            case ClearOp c:
                CheckSurface(c.Surface, beatId, board, issues);
                foreach (var gone in board.Live.Where(kv => kv.Value == c.Surface).Select(kv => kv.Key).ToList())
                {
                    board.Live.Remove(gone);
                }

                break;
            case ScreenMarkOp m:
                CheckMark(m.Region, beatId, board.Desktop, issues);
                break;
            case UnknownOp u:
                issues.Add(new LessonIssue(LessonIssueCodes.OpUnknown, beatId, $"op '{u.Name}' is not in v1"));
                break;
        }
    }

    private static void Create(string surface, string id, string beatId, Board board, List<LessonIssue> issues)
    {
        CheckSurface(surface, beatId, board, issues);
        CheckId(id, "element", beatId, board.EverUsed, issues);
        board.Live[id] = surface;
    }

    private static void CheckSurface(string surface, string beatId, Board board, List<LessonIssue> issues)
    {
        if (!board.Surfaces.Contains(surface))
        {
            issues.Add(new LessonIssue(LessonIssueCodes.RefSurface, beatId, $"surface '{surface}' is not declared"));
        }
    }

    /// <summary>
    /// The target must be live now; when <paramref name="onSurface"/> is given (an arrow), it must also
    /// be on that card — an arrow is drawn inside one card and cannot reach into another.
    /// </summary>
    private static void CheckTarget(string target, string beatId, Board board, List<LessonIssue> issues, string? onSurface = null)
    {
        if (!board.Live.TryGetValue(target, out var surface))
        {
            issues.Add(new LessonIssue(LessonIssueCodes.RefTarget, beatId, $"element '{target}' does not exist at this beat"));
        }
        else if (onSurface is not null && surface != onSurface)
        {
            issues.Add(new LessonIssue(LessonIssueCodes.RefTarget, beatId, $"element '{target}' is on '{surface}', not '{onSurface}'"));
        }
    }

    private static void CheckMark(PixelRect r, string beatId, PixelRect d, List<LessonIssue> issues)
    {
        if (r.Width <= 0 || r.Height <= 0)
        {
            issues.Add(new LessonIssue(LessonIssueCodes.ValueInvalid, beatId, $"mark {r} has no area"));
        }
        // In long: PixelRect.Right/Bottom are unchecked int sums and wrap negative for huge input.
        else if (r.X < d.X || r.Y < d.Y || (long)r.X + r.Width > (long)d.X + d.Width || (long)r.Y + r.Height > (long)d.Y + d.Height)
        {
            issues.Add(new LessonIssue(LessonIssueCodes.MarkOutOfBounds, beatId, $"mark {r} is outside the desktop {d}"));
        }
    }

    private static void CheckText(string value, int max, string what, string? beatId, List<LessonIssue> issues)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            issues.Add(new LessonIssue(LessonIssueCodes.FieldMissing, beatId, $"{what} is empty"));
        }
        else if (value.Length > max)
        {
            issues.Add(new LessonIssue(LessonIssueCodes.FieldTooLong, beatId, $"{what} is {value.Length} chars (max {max})"));
        }
    }

    private static void CheckOptionalText(string? value, int max, string what, string beatId, List<LessonIssue> issues)
    {
        if (value is not null && value.Length > max)
        {
            issues.Add(new LessonIssue(LessonIssueCodes.FieldTooLong, beatId, $"{what} is {value.Length} chars (max {max})"));
        }
    }

    private static void CheckId(string id, string what, string? beatId, HashSet<string> seen, List<LessonIssue> issues)
    {
        if (!IdPattern.IsMatch(id))
        {
            issues.Add(new LessonIssue(LessonIssueCodes.IdInvalid, beatId, $"{what} id '{id}'"));
        }
        else if (!seen.Add(id))
        {
            issues.Add(new LessonIssue(LessonIssueCodes.IdDuplicate, beatId, $"{what} id '{id}' used twice"));
        }
    }

    private static LessonIssue Plan(string code, string message) => new(code, null, message);

    /// <summary>
    /// The degrade policy (FEATURE-007 Phase 1, M7 S7.1): speech survives whenever any exists. A
    /// plan-level issue, or degraded beats at or above <see cref="LessonLimits.FallbackDegradedRatio"/>,
    /// means a drawing too gappy to teach from — speak the whole text instead of showing fragments.
    /// </summary>
    private LessonValidationResult Decide(VisualPlan plan, List<LessonIssue> issues, HashSet<int> degraded)
    {
        // The say limit bounds what is spoken, not only what is drawn: an over-long say is treated as
        // having no usable speech (never truncated — cutting a sentence can change what it teaches).
        bool Speakable(string say) => !string.IsNullOrWhiteSpace(say) && say.Length <= _limits.MaxSayChars;
        var fallback = string.Join(" ", plan.Beats.Select(b => b.Say).Where(Speakable));
        if (fallback.Length == 0)
        {
            return new LessonValidationResult(LessonOutcome.Rejected, null, string.Empty, issues);
        }

        if (issues.Any(i => i.BeatId is null) || (double)degraded.Count / plan.Beats.Count >= _limits.FallbackDegradedRatio)
        {
            return new LessonValidationResult(LessonOutcome.TextFallback, null, fallback, issues);
        }

        if (degraded.Count == 0)
        {
            return new LessonValidationResult(LessonOutcome.Valid, plan, fallback, issues);
        }

        // Degraded beats keep their sentence and lose their drawing; a degraded beat with nothing
        // to say has nothing left at all and is dropped.
        var beats = plan.Beats
            .Select((b, i) => degraded.Contains(i) ? b with { Ops = Array.Empty<LessonOp>(), Degraded = true } : b)
            .Where(b => !(b.Degraded && !Speakable(b.Say)))
            .ToList();
        return new LessonValidationResult(LessonOutcome.PartiallyDegraded, plan with { Beats = beats }, fallback, issues);
    }
}
