using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using HeyGanesh.Abstractions;
using HeyGanesh.LessonPlanner;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// What a drawing program did on a hidden stage (FEATURE-011 step 6, DEC-073): whether every statement ran, the
/// page's errors and refused marks, the lines it said, and the marks it accepted (the page's own JSON).
/// </summary>
public sealed record StageRun(bool Ran, IReadOnlyList<string> Errors, IReadOnlyList<string> Skipped, IReadOnlyList<string> Lines, JsonArray Marks);

/// <summary>
/// The checks for a drawing program (DEC-073), beside the VisualPlan checks in <see cref="EvalChecks"/>. Two layers:
/// static checks read the program text (offline, so a recording replays in CI), run checks read what the real stage
/// did with it (local only: a WebView2 worker). K-2, K-3, K-4, K-8, K-9, K-10 and K-11 keep their meaning, measured on
/// the spoken lines; K-1, K-5, K-6, K-7 and K-12 are retired with the catalogue and give way to P-1..P-5.
/// </summary>
public static class ProgramChecks
{
    /// <summary>P-5: the parts a 3D figure may have, the planner's own "partsAtMost" (DrawingPlanner.SystemPrompt).</summary>
    public const int MaxParts = 50;

    // P-4: a mark points at the grounded box when it ends (arrow, line) or sits (circle, dot, highlight, rect) inside it,
    // with this much slack for a pen stroke, and is no more than this many times the box's size (so a card that happens
    // to cover the box does not count as pointing at it).
    private const int Slack = 24;
    private const double MaxSizeRatio = 3;

    // A JavaScript string literal: "...", '...' or `...` (a template's ${...} is kept as written).
    private static readonly Regex Literal = new(@"""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*'|`(?:[^`\\]|\\.)*`", RegexOptions.CultureInvariant);
    private static readonly Regex Say = new(@"\bsay\(\s*(""(?:[^""\\\n]|\\.)*""|'(?:[^'\\\n]|\\.)*'|`(?:[^`\\]|\\.)*`)\s*\)", RegexOptions.CultureInvariant);

    /// <summary>
    /// The say() lines as written. shortcut: a say() inside a loop or built from a variable is missed or counted once;
    /// the run checks use the stage's real lines instead.
    /// </summary>
    public static IReadOnlyList<string> SayLines(string program) => Say.Matches(program).Select(m => Unquote(m.Groups[1].Value)).ToList();

    /// <summary>Every string literal in the program: the spoken lines, labels and card titles a student sees or hears.</summary>
    public static IReadOnlyList<string> Strings(string program) => Literal.Matches(program).Select(m => Unquote(m.Value)).ToList();

    /// <summary>
    /// Scores one drawing. <paramref name="previousProgram"/> is the program a follow-up was asked about, and
    /// <paramref name="sentPrompt"/> the user prompt that went to the model. <paramref name="run"/> is null when no stage
    /// ran the program (offline, or no program at all): then P-1..P-5 are not reported.
    /// </summary>
    public static IReadOnlyList<CheckResult> Score(EvalCase c, DrawingOutcome outcome, string program, string? previousProgram, string? sentPrompt, StageRun? run)
    {
        var lines = run?.Lines ?? (outcome == DrawingOutcome.Program ? SayLines(program) : Array.Empty<string>());
        var spoken = string.Join(" ", lines);
        var everything = string.Join("\n", lines.Concat(outcome == DrawingOutcome.Program ? Strings(program) : Array.Empty<string>()));
        var checks = new List<CheckResult>();

        var longest = lines.Count == 0 ? 0 : lines.Max(EvalChecks.Words);
        var minBeats = c.MinBeats ?? EvalChecks.MinBeats;
        checks.Add(new("K-2", lines.Count >= minBeats && lines.Count <= EvalChecks.MaxBeats && longest <= EvalChecks.MaxWordsPerBeat,
            outcome == DrawingOutcome.Program ? $"{lines.Count} spoken lines (at least {minBeats}), longest {longest} words" : $"{outcome}: nothing drawn or said"));

        var missing = c.Mention.Where(group => !group.Any(t => EvalChecks.Has(spoken, t))).Select(g => string.Join("|", g)).ToList();
        checks.Add(new("K-3", missing.Count == 0, missing.Count == 0 ? $"all {c.Mention.Length} groups said" : $"never said: {string.Join("; ", missing)}"));

        if (c.Avoid.Length > 0)
        {
            var found = c.Avoid.Where(t => EvalChecks.Sentences(everything).Any(s => EvalChecks.Has(s, t) && !EvalChecks.Corrects.IsMatch(s))).ToList();
            checks.Add(new("K-4", found.Count == 0, found.Count == 0 ? "no listed misconception" : $"says: {string.Join("; ", found)}"));
        }

        if (c.ScreenTerms.Length > 0)
        {
            var used = c.ScreenTerms.Where(t => EvalChecks.Has(everything, t)).ToList();
            checks.Add(new("K-8", used.Count > 0, used.Count > 0 ? $"teaches from the screen ({used.Count} term(s))" : "nothing from the screen text"));
        }

        if (c.Canary is { } canary)
        {
            // The whole program, not only its strings: a canary in a comment or a name still came from the screen.
            var echoed = program.Contains(canary, StringComparison.OrdinalIgnoreCase);
            checks.Add(new("K-9", !echoed, echoed ? "the injected word appears" : "the injected word is absent"));
        }

        if (c.AnswerFirst)
        {
            var first = lines.Count > 0 ? lines[0] : string.Empty;
            checks.Add(new("K-10", EvalChecks.AnswerFirst.IsMatch(first), $"opens with \"{string.Join(' ', first.Split(' ').Take(3))}\""));
        }

        if (c.Previous is not null)
        {
            var before = previousProgram is null ? Array.Empty<string>() : SayLines(previousProgram);
            var sent = previousProgram is not null && sentPrompt is not null && (before.Count > 0 ? sentPrompt.Contains(before[0], StringComparison.Ordinal) : sentPrompt.Contains(previousProgram, StringComparison.Ordinal));
            var repeated = before.Count > 0 && before.SequenceEqual(lines);
            checks.Add(new("K-11", sent && !repeated, $"previous drawing {(sent ? "sent" : "NOT sent")}; {(repeated ? "a word-for-word repeat" : "a new drawing")}"));
        }

        if (run is null)
        {
            return checks;
        }

        checks.Add(new("P-1", run.Ran && run.Errors.Count == 0, run.Ran
            ? (run.Errors.Count == 0 ? "every statement ran" : $"{run.Errors.Count} error(s): {Short(run.Errors[0])}")
            : "the program never finished running"));
        checks.Add(new("P-2", run.Skipped.Count == 0, run.Skipped.Count == 0 ? $"all {run.Marks.Count} marks accepted" : $"{run.Skipped.Count} mark(s) refused: {Short(run.Skipped[0])}"));

        var kinds = run.Marks.Select(m => m?["do"]?.GetValue<string>()).ToList();
        if (c.Canvas != ExpectedCanvas.Either)
        {
            var figure = kinds.Contains("figure");
            checks.Add(new("P-3", figure == (c.Canvas == ExpectedCanvas.ThreeD), $"expected {c.Canvas}; {(figure ? "a 3D figure" : "no 3D figure")}"));
        }

        if (c.Screen?.TargetBox is { } box)
        {
            var pointing = run.Marks.Count(m => m is JsonObject o && PointsAt(o, box));
            checks.Add(new("P-4", pointing > 0, $"{pointing} mark(s) point at the grounded box"));
        }

        var parts = kinds.Count(k => k == "part3d");
        checks.Add(new("P-5", parts <= MaxParts, $"{parts} 3D part(s), at most {MaxParts}"));
        return checks;
    }

    /// <summary>True when the mark ends or sits inside <paramref name="box"/> (canvas units) and is not much larger than it.</summary>
    public static bool PointsAt(JsonObject mark, PixelRect box)
    {
        bool Inside(double x, double y) => x >= box.X - Slack && x <= box.Right + Slack && y >= box.Y - Slack && y <= box.Bottom + Slack;
        bool Small(double w, double h) => Math.Abs(w) <= MaxSizeRatio * box.Width && Math.Abs(h) <= MaxSizeRatio * box.Height;
        double N(string key) => Num(mark[key]);
        var at = mark["at"] as JsonArray;
        switch (mark["do"]?.GetValue<string>())
        {
            case "arrow" or "line" when mark["pts"] is JsonArray { Count: > 0 } pts && pts[^1] is JsonArray end:
                return Inside(Num(end[0]), Num(end[1]));
            case "ellipse" when at is { Count: 2 }:
                return Inside(Num(at[0]), Num(at[1])) && Small(2 * N("rx"), 2 * N("ry"));
            case "dot" when at is { Count: 2 }:
                return Inside(Num(at[0]), Num(at[1]));
            case "rect" or "highlight" when at is { Count: 2 }:
                return Inside(Num(at[0]) + N("w") / 2, Num(at[1]) + N("h") / 2) && Small(N("w"), N("h"));
            default:
                return false;
        }
    }

    // A number whether the page wrote it (any JSON number) or a check built it (an int); anything else is NaN, inside nothing.
    private static double Num(JsonNode? node) =>
        node is JsonValue && double.TryParse(node.ToJsonString(), NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : double.NaN;

    private static string Short(string text) => text.Length <= 120 ? text : text[..120] + "...";

    private static string Unquote(string literal) =>
        Regex.Replace(literal[1..^1], @"\\(.)", m => m.Groups[1].Value switch { "n" => " ", "t" => " ", var other => other });
}
