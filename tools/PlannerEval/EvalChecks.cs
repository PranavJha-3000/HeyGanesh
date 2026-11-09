using System.Text.RegularExpressions;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>One check's verdict on one lesson. <see cref="Detail"/> says what was seen, never the student's screen.</summary>
public sealed record CheckResult(string Id, bool Passed, string Detail);

/// <summary>
/// The word rules shared by the drawing checks (<see cref="ProgramChecks"/>, DEC-073), kept from the K checks of
/// DEC-043. The VisualPlan scorer (K-1, K-5, K-6, K-7, K-12 and the beat-based K checks) was deleted with the
/// VisualPlan stack in FEATURE-011 step 10 (DEC-077).
/// </summary>
public static class EvalChecks
{
    // K-2: three beats is the least that still shows a step; past eight a spoken lesson outlasts a
    // student's attention (FEATURE-007 §3.7, segmenting). 45 words is about 15 s of speech: one idea.
    // A case may lower the minimum (EvalCase.MinBeats), as the "I can't know that" case does.
    public const int MinBeats = 3;
    public const int MaxBeats = 8;
    public const int MaxWordsPerBeat = 45;

    internal static readonly Regex AnswerFirst = new(@"^\W*(yes|no|partly)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    // K-4: a misconception named in order to correct it ("it's a myth that heavier objects fall faster") is good
    // teaching, not a slip. A listed phrase only counts in a sentence with none of these cues. Bare "no" and
    // "not" are left out on purpose: they are in too many sentences that do state the myth.
    internal static readonly Regex Corrects = new(@"\b(myth|misconception|not true|false|wrong|isn't|doesn't|don't|can't|cannot|never)\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static IEnumerable<string> Sentences(string text) => Regex.Split(text, @"(?<=[.!?])\s+|\n");

    internal static int Words(string text) => text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length;

    /// <summary>Case-insensitive, and a whole word where the term starts or ends with a letter or digit ("on" never matches "one").</summary>
    internal static bool Has(string text, string term)
    {
        var pattern = (char.IsLetterOrDigit(term[0]) ? @"\b" : string.Empty) + Regex.Escape(term) + (char.IsLetterOrDigit(term[^1]) ? @"\b" : string.Empty);
        return Regex.IsMatch(text, pattern, RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    }
}
