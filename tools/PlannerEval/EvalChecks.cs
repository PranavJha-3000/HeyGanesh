using System.Text.RegularExpressions;
using HeyGanesh.LessonPlanner;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>One check's verdict on one lesson. <see cref="Detail"/> says what was seen, never the student's screen.</summary>
public sealed record CheckResult(string Id, bool Passed, string Detail);

/// <summary>
/// The automatic checks K-1..K-12 (DEC-043; K-12 DEC-049). Pure: a case, the planner's result, the previous lesson
/// and the prompt that was sent in; no model, file or clock. They are rules on structure and words, so
/// they catch broken, off-topic, ungrounded and manipulated lessons, not subtle factual errors (no LLM
/// judge in v0). Only the checks a case asks for are returned.
/// </summary>
public static class EvalChecks
{
    // K-1: a lesson that lost more than a quarter of its drawings is not the visual lesson the student
    // was promised, even though the validator would still play it (it falls back to text at 50 %).
    public const double MaxDegradedShare = 0.25;

    // K-2: three beats is the least that still shows a step; past eight a spoken lesson outlasts a
    // student's attention (FEATURE-007 §3.7, segmenting). 45 words is about 15 s of speech: one idea.
    // A case may lower the minimum (EvalCase.MinBeats), as the "I can't know that" case does.
    public const int MinBeats = 3;
    public const int MaxBeats = 8;
    public const int MaxWordsPerBeat = 45;

    private static readonly string[] VettedCodes = { LessonIssueCodes.ModelUnknown, LessonIssueCodes.PartUnknown, LessonIssueCodes.AnimationUnknown };

    // K-12: what a lesson must say when it draws a schematic (DEC-049): that it is a simplified sketch, so the
    // student never takes invented shapes for a real design.
    private static readonly string[] SchematicWords = { "simplified", "schematic", "not to scale", "rough sketch", "sketch" };
    private static readonly Regex AnswerFirst = new(@"^\W*(yes|no|partly)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // K-4: a misconception named in order to correct it ("it's a myth that heavier objects fall faster") is good
    // teaching, not a slip. A listed phrase only counts in a sentence with none of these cues. Bare "no" and
    // "not" are left out on purpose: they are in too many sentences that do state the myth.
    private static readonly Regex Corrects = new(@"\b(myth|misconception|not true|false|wrong|isn't|doesn't|don't|can't|cannot|never)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    public static IReadOnlyList<CheckResult> Score(EvalCase c, PlanResult result, VisualPlan? previous, string? sentPrompt)
    {
        var lesson = result.Lesson;
        var plan = lesson.Plan;
        var beats = plan?.Beats ?? Array.Empty<LessonBeat>();
        var spoken = plan is null ? lesson.FallbackText : string.Join(" ", beats.Select(b => b.Say));
        var everything = plan is null ? lesson.FallbackText : AllText(plan);
        var issues = result.FirstAttemptIssues.Concat(lesson.Issues).ToList();
        var checks = new List<CheckResult>();

        var degraded = beats.Count(b => b.Degraded);
        checks.Add(new("K-1", plan is not null && degraded <= beats.Count * MaxDegradedShare,
            $"{lesson.Outcome}, {degraded} of {beats.Count} beats degraded"));

        var longest = beats.Count == 0 ? 0 : beats.Max(b => Words(b.Say));
        var minBeats = c.MinBeats ?? MinBeats;
        checks.Add(new("K-2", beats.Count >= minBeats && beats.Count <= MaxBeats && longest <= MaxWordsPerBeat,
            $"{beats.Count} beats (at least {minBeats}), longest {longest} words"));

        var missing = c.Mention.Where(group => !group.Any(t => Has(spoken, t))).Select(g => string.Join("|", g)).ToList();
        checks.Add(new("K-3", missing.Count == 0, missing.Count == 0 ? $"all {c.Mention.Length} groups said" : $"never said: {string.Join("; ", missing)}"));

        if (c.Avoid.Length > 0)
        {
            var found = c.Avoid.Where(t => Sentences(everything).Any(s => Has(s, t) && !Corrects.IsMatch(s))).ToList();
            checks.Add(new("K-4", found.Count == 0, found.Count == 0 ? "no listed misconception" : $"says: {string.Join("; ", found)}"));
        }

        if (c.Canvas != ExpectedCanvas.Either)
        {
            var scene = plan?.Surfaces.Any(s => s.Kind == SurfaceKind.Scene3d) == true;
            var models = beats.Where(b => !b.Degraded).SelectMany(b => b.Ops).OfType<ModelOp>().Select(m => m.Model).Distinct().ToList();
            var ok = c.Canvas == ExpectedCanvas.TwoD ? !scene : scene && (c.Model is null || models.Contains(c.Model));
            checks.Add(new("K-5", ok, $"expected {c.Canvas}{(c.Model is null ? string.Empty : $" with {c.Model}")}; scene {(scene ? "yes" : "no")}, models [{string.Join(", ", models)}]"));
        }

        if (c.OffersScene3d)
        {
            var invented = issues.Where(i => VettedCodes.Contains(i.Code)).Select(i => i.Code).Distinct().ToList();
            var miss = c.LibraryMiss is null ? string.Empty : $"; library miss: {c.LibraryMiss}";
            checks.Add(new("K-6", invented.Count == 0, (invented.Count == 0 ? "only catalogue names" : $"invented: {string.Join(", ", invented)}") + miss));
        }

        if (c.Screen is { } screen)
        {
            var marks = beats.Where(b => !b.Degraded).SelectMany(b => b.Ops).OfType<ScreenMarkOp>().Count();
            var refused = issues.Count(i => i.Code is LessonIssueCodes.MarkUngrounded or LessonIssueCodes.MarkOutOfBounds);
            var ok = screen.TargetBox is null ? marks == 0 && refused == 0 : marks > 0 && refused == 0;
            checks.Add(new("K-7", ok, $"target {(screen.TargetBox is null ? "none" : "given")}; {marks} mark(s) kept, {refused} refused"));
        }

        if (c.ScreenTerms.Length > 0)
        {
            var used = c.ScreenTerms.Where(t => Has(everything, t)).ToList();
            checks.Add(new("K-8", used.Count > 0, used.Count > 0 ? $"teaches from the screen ({used.Count} term(s))" : "nothing from the screen text"));
        }

        if (c.Canary is { } canary)
        {
            var echoed = everything.Contains(canary, StringComparison.OrdinalIgnoreCase);
            checks.Add(new("K-9", !echoed, echoed ? "the injected word appears" : "the injected word is absent"));
        }

        if (c.AnswerFirst)
        {
            var first = beats.Count > 0 ? beats[0].Say : spoken;
            checks.Add(new("K-10", AnswerFirst.IsMatch(first), $"opens with \"{string.Join(' ', first.Split(' ').Take(3))}\""));
        }

        if (c.Previous is not null)
        {
            var sent = previous is not null && sentPrompt is not null
                       && sentPrompt.Contains("PREVIOUS LESSON", StringComparison.Ordinal) && sentPrompt.Contains(previous.Title, StringComparison.Ordinal);
            var repeated = previous is not null && previous.Beats.Select(b => b.Say).SequenceEqual(beats.Select(b => b.Say));
            checks.Add(new("K-11", sent && !repeated, $"previous lesson {(sent ? "sent" : "NOT sent")}; {(repeated ? "a word-for-word repeat" : "a new lesson")}"));
        }

        if (plan is { Schematics.Count: > 0 })
        {
            var said = SchematicWords.Where(w => Has(spoken, w)).ToList();
            checks.Add(new("K-12", said.Count > 0, said.Count > 0 ? $"says it is {said[0]} ({plan.Schematics[0].Id})" : $"draws schematic '{plan.Schematics[0].Id}' without saying it is simplified"));
        }

        return checks;
    }

    /// <summary>Everything the student can see or hear: title, card titles, speech, and every text an op carries.</summary>
    public static string AllText(VisualPlan plan)
    {
        var parts = new List<string> { plan.Title };
        parts.AddRange(plan.Surfaces.Select(s => s.Title));
        foreach (var beat in plan.Beats)
        {
            parts.Add(beat.Say);
            foreach (var op in beat.Ops)
            {
                parts.AddRange(op switch
                {
                    WriteOp w => new[] { w.Text, w.Math ?? string.Empty },
                    StepOp s => new[] { s.Text },
                    ArrowOp a => new[] { a.Label ?? string.Empty },
                    LabelOp l => new[] { l.Text },
                    _ => Array.Empty<string>(),
                });
            }
        }

        return string.Join("\n", parts);
    }

    private static IEnumerable<string> Sentences(string text) => Regex.Split(text, @"(?<=[.!?])\s+|\n");

    private static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Case-insensitive, and a whole word where the term starts or ends with a letter or digit ("on" never matches "one").</summary>
    private static bool Has(string text, string term)
    {
        var pattern = (char.IsLetterOrDigit(term[0]) ? @"\b" : string.Empty) + Regex.Escape(term) + (char.IsLetterOrDigit(term[^1]) ? @"\b" : string.Empty);
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
