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

    private static readonly Regex IdPattern = new("^[a-z][a-z0-9-]{0,31}$", RegexOptions.CultureInvariant);

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
        CheckBeats(plan, issues);
        return Decide(plan, issues);
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
        }
    }

    private void CheckBeats(VisualPlan plan, List<LessonIssue> issues)
    {
        var beatIds = new HashSet<string>(StringComparer.Ordinal);
        var elementIds = new HashSet<string>(StringComparer.Ordinal);
        foreach (var beat in plan.Beats)
        {
            CheckId(beat.Id, "beat", beat.Id, beatIds, issues);
            CheckText(beat.Say, _limits.MaxSayChars, "say", beat.Id, issues);
            if (beat.Ops.Count > _limits.MaxOpsPerBeat)
            {
                issues.Add(new LessonIssue(LessonIssueCodes.LimitOps, beat.Id, $"{beat.Ops.Count} ops (max {_limits.MaxOpsPerBeat})"));
            }

            foreach (var op in beat.Ops)
            {
                CheckOp(op, beat.Id, elementIds, issues);
            }
        }
    }

    private void CheckOp(LessonOp op, string beatId, HashSet<string> elementIds, List<LessonIssue> issues)
    {
        switch (op)
        {
            case WriteOp w:
                CheckId(w.Id, "element", beatId, elementIds, issues);
                CheckText(w.Text, _limits.MaxTextChars, "write text", beatId, issues);
                break;
            case StepOp s:
                CheckId(s.Id, "element", beatId, elementIds, issues);
                CheckText(s.Text, _limits.MaxTextChars, "step text", beatId, issues);
                if (s.Index < 1)
                {
                    issues.Add(new LessonIssue(LessonIssueCodes.ValueInvalid, beatId, $"step index {s.Index} (1-based)"));
                }

                break;
            case IconOp i:
                CheckId(i.Id, "element", beatId, elementIds, issues);
                if (!LessonVocabulary.Glyphs.Contains(i.Glyph))
                {
                    issues.Add(new LessonIssue(LessonIssueCodes.GlyphUnknown, beatId, $"glyph '{i.Glyph}'"));
                }

                break;
            case PlotOp p:
                CheckId(p.Id, "element", beatId, elementIds, issues);
                CheckText(p.Expr, _limits.MaxTextChars, "plot expr", beatId, issues);
                if (!(double.IsFinite(p.XMin) && double.IsFinite(p.XMax) && p.XMin < p.XMax))
                {
                    issues.Add(new LessonIssue(LessonIssueCodes.ValueInvalid, beatId, $"plot range [{p.XMin}, {p.XMax}]"));
                }

                break;
            case UnknownOp u:
                issues.Add(new LessonIssue(LessonIssueCodes.OpUnknown, beatId, $"op '{u.Name}' is not in v1"));
                break;
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

    private static LessonValidationResult Decide(VisualPlan plan, List<LessonIssue> issues)
    {
        var fallback = string.Join(" ", plan.Beats.Select(b => b.Say).Where(s => !string.IsNullOrWhiteSpace(s)));
        if (fallback.Length == 0)
        {
            return new LessonValidationResult(LessonOutcome.Rejected, null, string.Empty, issues);
        }

        if (issues.Any(i => i.BeatId is null))
        {
            return new LessonValidationResult(LessonOutcome.TextFallback, null, fallback, issues);
        }

        return new LessonValidationResult(issues.Count == 0 ? LessonOutcome.Valid : LessonOutcome.PartiallyDegraded, plan, fallback, issues);
    }
}
