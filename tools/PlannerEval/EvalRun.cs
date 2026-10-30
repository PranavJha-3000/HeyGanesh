using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using HeyGanesh.Abstractions;
using HeyGanesh.LessonPlanner;
using HeyGanesh.PartLibrary;
using HeyGanesh.Providers.Core;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// One case planned and scored: what a recording stores per case and what a replay reproduces. Never
/// holds a key: answers are model output, the screen text is the set's own, errors are kinds only.
/// </summary>
public sealed record CaseRun
{
    public required string Id { get; init; }

    /// <summary>Check id → passed. Only the checks the case asks for.</summary>
    public required Dictionary<string, bool> Checks { get; init; }

    /// <summary>Every answer the model gave this case, in call order (a repair makes two).</summary>
    public required List<string> Answers { get; init; }

    /// <summary>The last stream stopped before it finished, so replaying the answer text would not reproduce the run.</summary>
    public bool Partial { get; init; }

    /// <summary>A provider fault's kind, when the case could not be planned ("Timeout", "RateLimited", ...).</summary>
    public string? Error { get; init; }

    public double? FirstBeatMs { get; init; }

    public double LatencyMs { get; init; }

    public int InputTokens { get; init; }

    public int OutputTokens { get; init; }

    public int Attempts { get; init; }

    public string? StreamNote { get; init; }

    public string? LibraryMiss { get; init; }

    /// <summary>One line per check for the report: "K-3 never said: shape".</summary>
    public List<string> Details { get; init; } = new();

    [JsonIgnore]
    public VisualPlan? Plan { get; init; }

    [JsonIgnore]
    public List<string> SystemPrompts { get; init; } = new();
}

/// <summary>
/// The case loop shared by PE-2, <c>--record</c> and <c>--replay</c> (DEC-044): build the request the App
/// Shell would build, plan it through the real streaming planner over whatever client the caller gives,
/// score it. Follow-ups are sent the lesson last planned for their previous case.
/// </summary>
internal static class EvalRun
{
    public static readonly PixelRect Desktop = new(0, 0, 2560, 1440);

    public static PlanRequest Request(EvalCase c, VisualPlan? previous) => new()
    {
        Question = c.Question,
        Screen = c.Screen is { } s ? new ScreenGrounding(s.Description, s.TargetBox, null, 0, 0) : null,
        VirtualDesktop = Desktop,
        PreviousPlan = previous,
        Scene3d = c.OffersScene3d ? ModelLibrary.ToCatalog() : null,
        // A 3D case may also get a schematic for an object the library lacks (DEC-049), as the shell's 3D canvas will.
        SchematicGeometry = c.OffersScene3d ? Schematics.Check : null,
    };

    /// <summary>
    /// Plans and scores one case. An authentication failure and cancellation propagate (the whole run is
    /// over); any other provider fault is recorded on the case, so one bad call never loses the other 29.
    /// </summary>
    public static async Task<CaseRun> RunOneAsync(EvalCase c, ILlmClient client, IDictionary<string, VisualPlan> lessons, CancellationToken ct = default)
    {
        var previous = c.Previous is { } p && lessons.TryGetValue(p, out var prev) ? prev : null;
        var recording = new RecordingLlmClient(client);
        using var planner = new VisualPlanner(recording);
        PlanResult result;
        try
        {
            result = await planner.PlanStreamingAsync(Request(c, previous), null, ct).ConfigureAwait(false);
        }
        catch (ProviderException ex) when (ex.Kind != ProviderErrorKind.AuthFailed)
        {
            return new CaseRun
            {
                Id = c.Id,
                Checks = new(),
                Answers = recording.Answers,
                Partial = recording.LastAnswerPartial,
                Error = ex.Kind.ToString(),
                LibraryMiss = c.LibraryMiss,
                SystemPrompts = Prompts(recording),
            };
        }

        var sent = recording.Requests.Count > 0 ? recording.Requests[0].Messages.Last(m => m.Role == "user").Text : null;
        var checks = EvalChecks.Score(c, result, previous, sent);
        if (result.Lesson.Plan is { } plan)
        {
            lessons[c.Id] = plan;
        }

        return new CaseRun
        {
            Id = c.Id,
            Checks = checks.ToDictionary(r => r.Id, r => r.Passed),
            // Lower case on purpose: an upper-case FAIL in a report line reads as a failed row to the CI log scan.
            Details = checks.Select(r => $"{(r.Passed ? "pass" : "fail")} {r.Id} {r.Detail}").ToList(),
            Answers = recording.Answers,
            Partial = recording.LastAnswerPartial,
            FirstBeatMs = result.FirstBeatLatency?.TotalMilliseconds,
            LatencyMs = result.Latency.TotalMilliseconds,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            Attempts = result.Attempts,
            StreamNote = result.StreamNote,
            LibraryMiss = c.LibraryMiss,
            Plan = result.Lesson.Plan,
            SystemPrompts = Prompts(recording),
        };
    }

    /// <summary>
    /// The planner prompt's fingerprint: SHA-256 over every distinct system prompt sent (2D and 3D cases send
    /// different ones), sorted. A baseline records it, and a replay against that baseline fails when it
    /// changes, because frozen answers say nothing about a new prompt (DEC-044).
    /// </summary>
    public static string PromptHash(IEnumerable<CaseRun> runs)
    {
        var prompts = runs.SelectMany(r => r.SystemPrompts).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal);
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(string.Join("\n\u0000\n", prompts)))).ToLowerInvariant();
    }

    private static List<string> Prompts(RecordingLlmClient recording) =>
        recording.Requests.Select(r => r.SystemPrompt ?? string.Empty).Distinct(StringComparer.Ordinal).ToList();
}
