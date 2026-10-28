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
/// Where a plan will be shown, and what it may point at. <see cref="VirtualDesktop"/> bounds every
/// mark. With <see cref="RequireGroundedMarks"/>, a mark must also sit inside the grounded target
/// box (inflated by <see cref="GroundedMarginPx"/>) — a model-planned lesson may only mark what
/// Element Selection / Vision actually found; with no box, every mark is refused.
/// </summary>
public sealed record ValidationContext(PixelRect VirtualDesktop)
{
    public bool RequireGroundedMarks { get; init; }

    public PixelRect? GroundedTarget { get; init; }

    public int GroundedMarginPx { get; init; } = 24;

    /// <summary>
    /// The 3D vocabulary, and the caller's statement that its canvas can draw a scene3d surface
    /// (DEC-028). Null — the default — means no 3D: a plan declaring a scene loses its drawing and is
    /// spoken. Kept off until a renderer exists, because CardSurface would lay a scene out as a card.
    /// </summary>
    public ModelCatalog? Scene3d { get; init; }
}

/// <summary>
/// Holds a parsed plan to the v1 contract. Issues are emitted in plan order — surfaces, then beats,
/// then each beat's ops — so two runs over the same input report the same sequence (LS-6).
/// Never throws on plan content; only a null argument (a caller bug) throws.
/// </summary>
public sealed class VisualPlanValidator
{
    public const string SupportedSchema = "heyganesh.visualplan/1";

    /// <summary>The 1.1 minor (DEC-028): v1 plus the scene3d surface and its ops. Both ids are accepted.</summary>
    public const string Schema11 = "heyganesh.visualplan/1.1";

    private static readonly Regex IdPattern = new(@"^[a-z][a-z0-9-]{0,31}\z", RegexOptions.CultureInvariant);

    private readonly LessonLimits _limits;

    public VisualPlanValidator(LessonLimits? limits = null)
    {
        _limits = limits ?? LessonLimits.Default;
    }

    /// <summary>Validates with marks allowed anywhere on the desktop (fixtures, hand-authored lessons).</summary>
    public LessonValidationResult Validate(ParseResult parsed, PixelRect virtualDesktop) =>
        Validate(parsed, new ValidationContext(virtualDesktop));

    public LessonValidationResult Validate(ParseResult parsed, ValidationContext context)
    {
        ArgumentNullException.ThrowIfNull(parsed);
        ArgumentNullException.ThrowIfNull(context);

        var issues = new List<LessonIssue>(parsed.Issues);
        if (parsed.Plan is null)
        {
            return new LessonValidationResult(LessonOutcome.Rejected, null, string.Empty, issues);
        }

        var plan = parsed.Plan;
        CheckPlan(plan, issues, context);
        var degraded = CheckBeats(plan, issues, context);
        return Decide(plan, issues, degraded);
    }

    private void CheckPlan(VisualPlan plan, List<LessonIssue> issues, ValidationContext context)
    {
        var is11 = string.Equals(plan.Schema, Schema11, StringComparison.Ordinal);
        if (!is11 && !string.Equals(plan.Schema, SupportedSchema, StringComparison.Ordinal))
        {
            issues.Add(Plan(LessonIssueCodes.SchemaUnsupported, $"schema '{plan.Schema}' is not {SupportedSchema} or {Schema11}"));
        }

        // Scene rules are plan-level, like every surface rule: a scene the host cannot draw, or one the
        // schema does not allow, costs the whole drawing and the lesson is spoken instead.
        var scenes = plan.Surfaces.Count(s => s.Kind == SurfaceKind.Scene3d);
        if (scenes > 0)
        {
            if (!is11)
            {
                issues.Add(Plan(LessonIssueCodes.SchemaUnsupported, $"a scene3d surface needs schema {Schema11}"));
            }

            if (context.Scene3d is null)
            {
                issues.Add(Plan(LessonIssueCodes.SurfaceUnsupported, "scene3d is not available on this canvas"));
            }

            if (scenes > _limits.MaxScenes)
            {
                issues.Add(Plan(LessonIssueCodes.LimitScenes, $"{scenes} scene3d surfaces (max {_limits.MaxScenes})"));
            }
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
        public required ValidationContext Context { get; init; }
        public HashSet<string> EverUsed { get; } = new(StringComparer.Ordinal);
        public Dictionary<string, string> Live { get; set; } = new(StringComparer.Ordinal);

        /// <summary>Declared kind per surface id (first declaration wins; duplicates are already reported).</summary>
        public Dictionary<string, SurfaceKind> Kinds { get; } = new(StringComparer.Ordinal);

        /// <summary>
        /// Model id per instance id, plan-wide. Not rolled back: an instance is usable only while it is
        /// also in <see cref="Live"/>, which is — so a degraded model beat's instance never becomes usable.
        /// </summary>
        public Dictionary<string, string> Instances { get; } = new(StringComparer.Ordinal);

        public ModelCatalog Catalog => Context.Scene3d ?? ModelCatalog.Empty;
    }

    /// <summary>Walks the beats in order and returns the indices of beats that lose their drawing.</summary>
    private HashSet<int> CheckBeats(VisualPlan plan, List<LessonIssue> issues, ValidationContext context)
    {
        var degraded = new HashSet<int>();
        var parserFlagged = issues.Where(i => i.BeatId is not null).Select(i => i.BeatId!).ToHashSet(StringComparer.Ordinal);
        var beatIds = new HashSet<string>(StringComparer.Ordinal);
        var board = new Board { Surfaces = plan.Surfaces.Select(s => s.Id).ToHashSet(StringComparer.Ordinal), Context = context };
        foreach (var surface in plan.Surfaces)
        {
            board.Kinds.TryAdd(surface.Id, surface.Kind);
        }

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
                CheckSurface(a.Surface, beatId, board, issues, SurfaceKind.Card);
                CheckOptionalText(a.Label, _limits.MaxTextChars, "arrow label", beatId, issues);
                CheckTarget(a.From, beatId, board, issues, a.Surface);
                CheckTarget(a.To, beatId, board, issues, a.Surface);
                break;
            case HighlightOp h:
                if (IsPartRef(h.Target))
                {
                    CheckPartRef(h.Target, beatId, board, issues);
                }
                else
                {
                    CheckTarget(h.Target, beatId, board, issues);
                }

                break;
            case ModelOp mo:
                CheckModel(mo, beatId, board, issues);
                break;
            case LabelOp l:
                // The target is checked before the label exists, so a label can never point at itself.
                CheckSurface(l.Surface, beatId, board, issues, SurfaceKind.Scene3d);
                CheckSceneTarget(l.Target, l.Surface, beatId, board, issues);
                CheckText(l.Text, _limits.MaxTextChars, "label text", beatId, issues);
                CheckId(l.Id, "element", beatId, board.EverUsed, issues);
                board.Live[l.Id] = l.Surface;
                break;
            case ExplodeOp e:
                CheckInstance(e.Target, beatId, board, issues);
                if (!(double.IsFinite(e.Amount) && e.Amount >= 0 && e.Amount <= 1))
                {
                    issues.Add(new LessonIssue(LessonIssueCodes.ValueInvalid, beatId, $"explode amount {e.Amount} (0..1)"));
                }

                break;
            case AnimateOp an:
                if (CheckInstance(an.Target, beatId, board, issues) is { } animated && !animated.Animations.Contains(an.Animation))
                {
                    issues.Add(new LessonIssue(LessonIssueCodes.AnimationUnknown, beatId, $"model '{animated.Id}' has no animation '{an.Animation}'"));
                }

                break;
            case CameraOp cam:
                CheckSurface(cam.Surface, beatId, board, issues, SurfaceKind.Scene3d);
                CheckSceneTarget(cam.Focus, cam.Surface, beatId, board, issues);
                break;
            case ClearOp c:
                CheckSurface(c.Surface, beatId, board, issues);
                foreach (var gone in board.Live.Where(kv => kv.Value == c.Surface).Select(kv => kv.Key).ToList())
                {
                    board.Live.Remove(gone);
                }

                break;
            case ScreenMarkOp m:
                CheckMark(m.Region, beatId, board.Context, issues);
                break;
            case UnknownOp u:
                issues.Add(new LessonIssue(LessonIssueCodes.OpUnknown, beatId, $"op '{u.Name}' is not in the contract"));
                break;
        }
    }

    private static void Create(string surface, string id, string beatId, Board board, List<LessonIssue> issues)
    {
        CheckSurface(surface, beatId, board, issues, SurfaceKind.Card);
        CheckId(id, "element", beatId, board.EverUsed, issues);
        board.Live[id] = surface;
    }

    /// <summary>
    /// The surface must be declared and, when <paramref name="kind"/> is given, be of that kind — card
    /// ops draw on cards, scene ops on the scene. <c>clear</c> passes no kind: it works on both.
    /// </summary>
    private static void CheckSurface(string surface, string beatId, Board board, List<LessonIssue> issues, SurfaceKind? kind = null)
    {
        if (!board.Surfaces.Contains(surface))
        {
            issues.Add(new LessonIssue(LessonIssueCodes.RefSurface, beatId, $"surface '{surface}' is not declared"));
        }
        else if (kind is { } wanted && board.Kinds[surface] != wanted)
        {
            issues.Add(new LessonIssue(LessonIssueCodes.RefSurfaceKind, beatId, $"surface '{surface}' is a {board.Kinds[surface]}, this op needs a {wanted}"));
        }
    }

    private void CheckModel(ModelOp op, string beatId, Board board, List<LessonIssue> issues)
    {
        CheckSurface(op.Surface, beatId, board, issues, SurfaceKind.Scene3d);
        if (!board.Catalog.TryGet(op.Model, out _))
        {
            issues.Add(new LessonIssue(LessonIssueCodes.ModelUnknown, beatId, $"model '{op.Model}' is not in the catalogue"));
        }

        var live = board.Live.Count(kv => kv.Value == op.Surface && board.Instances.ContainsKey(kv.Key));
        if (live >= _limits.MaxModelsPerScene)
        {
            issues.Add(new LessonIssue(LessonIssueCodes.LimitModels, beatId, $"model '{op.Id}' would be instance {live + 1} on '{op.Surface}' (max {_limits.MaxModelsPerScene})"));
        }

        CheckId(op.Id, "element", beatId, board.EverUsed, issues);
        board.Instances[op.Id] = op.Model;
        board.Live[op.Id] = op.Surface;
    }

    /// <summary>A live model instance, returned with its catalogue entry; null (and an issue) otherwise.</summary>
    private static CatalogModel? CheckInstance(string target, string beatId, Board board, List<LessonIssue> issues)
    {
        if (board.Live.ContainsKey(target) && board.Instances.TryGetValue(target, out var modelId) && board.Catalog.TryGet(modelId, out var model))
        {
            return model;
        }

        issues.Add(new LessonIssue(LessonIssueCodes.RefTarget, beatId, $"'{target}' is not a model instance at this beat"));
        return null;
    }

    private static bool IsPartRef(string target) => target.Contains('.', StringComparison.Ordinal);

    /// <summary>
    /// "instance.part": the instance must be live and its catalogue model must have the part. An
    /// invented part is <c>part.unknown</c>, not <c>ref.target</c>, so evals can tell "made up a part"
    /// from "referred to something too early".
    /// </summary>
    private static string? CheckPartRef(string target, string beatId, Board board, List<LessonIssue> issues)
    {
        var dot = target.IndexOf('.', StringComparison.Ordinal);
        var instance = target[..dot];
        var part = target[(dot + 1)..];
        if (CheckInstance(instance, beatId, board, issues) is not { } model)
        {
            return null;
        }

        if (!model.Parts.Contains(part))
        {
            issues.Add(new LessonIssue(LessonIssueCodes.PartUnknown, beatId, $"model '{model.Id}' has no part '{part}'"));
        }

        return instance;
    }

    /// <summary>A label or camera target: a live instance, or a part of one, on the same scene.</summary>
    private static void CheckSceneTarget(string target, string surface, string beatId, Board board, List<LessonIssue> issues)
    {
        var instance = IsPartRef(target)
            ? CheckPartRef(target, beatId, board, issues)
            : CheckInstance(target, beatId, board, issues) is null ? null : target;
        if (instance is not null && board.Live[instance] != surface)
        {
            issues.Add(new LessonIssue(LessonIssueCodes.RefTarget, beatId, $"'{target}' is on '{board.Live[instance]}', not '{surface}'"));
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

    private static void CheckMark(PixelRect r, string beatId, ValidationContext context, List<LessonIssue> issues)
    {
        var d = context.VirtualDesktop;
        if (r.Width <= 0 || r.Height <= 0)
        {
            issues.Add(new LessonIssue(LessonIssueCodes.ValueInvalid, beatId, $"mark {r} has no area"));
        }
        // In long: PixelRect.Right/Bottom are unchecked int sums and wrap negative for huge input.
        else if (r.X < d.X || r.Y < d.Y || (long)r.X + r.Width > (long)d.X + d.Width || (long)r.Y + r.Height > (long)d.Y + d.Height)
        {
            issues.Add(new LessonIssue(LessonIssueCodes.MarkOutOfBounds, beatId, $"mark {r} is outside the desktop {d}"));
        }
        else if (context.RequireGroundedMarks && !InsideGrounded(r, context))
        {
            issues.Add(new LessonIssue(LessonIssueCodes.MarkUngrounded, beatId,
                context.GroundedTarget is null ? "no grounded target: marks are not allowed" : $"mark {r} is not on the grounded target {context.GroundedTarget}"));
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

    private static bool InsideGrounded(PixelRect r, ValidationContext context)
    {
        if (context.GroundedTarget is not { } g)
        {
            return false;
        }

        long m = context.GroundedMarginPx;
        return r.X >= g.X - m && r.Y >= g.Y - m
            && (long)r.X + r.Width <= (long)g.X + g.Width + m
            && (long)r.Y + r.Height <= (long)g.Y + g.Height + m;
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
