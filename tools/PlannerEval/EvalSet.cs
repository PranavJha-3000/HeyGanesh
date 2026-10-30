using System.Text.Json;
using System.Text.Json.Serialization;
using HeyGanesh.Abstractions;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>What a case expects the lesson to draw on. "either" leaves the choice to the planner.</summary>
public enum ExpectedCanvas
{
    Either = 0,
    TwoD = 1,
    ThreeD = 2,
}

/// <summary>
/// A synthetic screen for a case: text written for the set, never captured from a machine.
/// <see cref="Target"/> is a grounded box in physical pixels (x, y, width, height) or null.
/// </summary>
public sealed record EvalScreen(string Description, int[]? Target)
{
    public PixelRect? TargetBox => Target is { Length: 4 } t ? new PixelRect(t[0], t[1], t[2], t[3]) : null;
}

/// <summary>
/// One student question and what its lesson is checked for. Every field but <see cref="Id"/>,
/// <see cref="Subject"/> and <see cref="Question"/> is optional; a check whose field is empty does not apply.
/// </summary>
public sealed record EvalCase
{
    public required string Id { get; init; }

    public required string Subject { get; init; }

    /// <summary>The question as the student would say it (the video cases verbatim, wake phrase removed).</summary>
    public required string Question { get; init; }

    public EvalScreen? Screen { get; init; }

    /// <summary>The case whose lesson this one follows up (it is sent as the previous lesson).</summary>
    public string? Previous { get; init; }

    public ExpectedCanvas Canvas { get; init; }

    /// <summary>The library model a 3D lesson must use, when one fits.</summary>
    public string? Model { get; init; }

    /// <summary>An object the student asked for that the library does not have; reported, never hidden (K-6).</summary>
    public string? LibraryMiss { get; init; }

    /// <summary>K-3: every group must have at least one term in the spoken text.</summary>
    public string[][] Mention { get; init; } = Array.Empty<string[]>();

    /// <summary>K-4: misconceptions that must not appear anywhere in the lesson.</summary>
    public string[] Avoid { get; init; } = Array.Empty<string>();

    /// <summary>K-8: at least one of these, taken from the screen text, must appear in the lesson.</summary>
    public string[] ScreenTerms { get; init; } = Array.Empty<string>();

    /// <summary>K-9: a word the screen text tries to make the tutor say.</summary>
    public string? Canary { get; init; }

    /// <summary>K-10: a fact check; the first beat must open with yes / no / partly.</summary>
    public bool AnswerFirst { get; init; }

    /// <summary>The golden lesson in tools/LessonSmoke/fixtures that answers this case, if any.</summary>
    public string? Golden { get; init; }

    /// <summary>3D (and "either") cases are offered the Part Library catalogue, as the App Shell offers it for a 3D route.</summary>
    public bool OffersScene3d => Canvas != ExpectedCanvas.TwoD;
}

public sealed record EvalSet(string Version, IReadOnlyList<EvalCase> Cases)
{
    public const int ExpectedCount = 30;

    public static JsonSerializerOptions Json { get; } = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        ReadCommentHandling = JsonCommentHandling.Skip,
        Converters = { new JsonStringEnumConverter(new CanvasNaming()) },
    };

    public EvalCase this[string id] => Cases.First(c => c.Id == id);

    public static EvalSet Load(string path) =>
        JsonSerializer.Deserialize<EvalSet>(File.ReadAllText(path), Json) ?? throw new InvalidDataException($"{path}: empty");

    /// <summary>
    /// Why the set cannot be scored, or none. A set that fails here is never run: a duplicate id would
    /// merge two reports, and a follow-up to a missing case would be scored without its previous lesson.
    /// </summary>
    public IReadOnlyList<string> Problems(IReadOnlySet<string> libraryModels)
    {
        var problems = new List<string>();
        if (Cases.Count != ExpectedCount)
        {
            problems.Add($"{Cases.Count} cases, expected {ExpectedCount}");
        }

        foreach (var dup in Cases.GroupBy(c => c.Id).Where(g => g.Count() > 1))
        {
            problems.Add($"duplicate id {dup.Key}");
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var c in Cases)
        {
            if (c.Previous is { } p && !seen.Contains(p))
            {
                problems.Add($"{c.Id}: previous {p} is not an earlier case");
            }

            if (c.Model is { } m && !libraryModels.Contains(m))
            {
                problems.Add($"{c.Id}: model {m} is not in the Part Library");
            }

            if (c.Model is not null && c.Canvas != ExpectedCanvas.ThreeD)
            {
                problems.Add($"{c.Id}: names a model but does not expect 3D");
            }

            if (c.Canary is not null && c.Screen?.Description.Contains(c.Canary, StringComparison.Ordinal) != true)
            {
                problems.Add($"{c.Id}: the canary is not in the screen text, so nothing tries to inject it");
            }

            if (c.ScreenTerms.Length > 0 && c.Screen is null)
            {
                problems.Add($"{c.Id}: screen terms without a screen");
            }

            if (string.IsNullOrWhiteSpace(c.Question) || c.Mention.Length == 0)
            {
                problems.Add($"{c.Id}: needs a question and at least one mention group");
            }

            seen.Add(c.Id);
        }

        return problems;
    }

    /// <summary>"2d" / "3d" / "either" in the file.</summary>
    private sealed class CanvasNaming : JsonNamingPolicy
    {
        public override string ConvertName(string name) => name switch
        {
            nameof(ExpectedCanvas.TwoD) => "2d",
            nameof(ExpectedCanvas.ThreeD) => "3d",
            _ => name.ToLowerInvariant(),
        };
    }
}
