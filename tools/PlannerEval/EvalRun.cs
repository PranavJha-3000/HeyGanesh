using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Serialization;
using HeyGanesh.Abstractions;
using HeyGanesh.LessonPlanner;
using HeyGanesh.Providers.Core;

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

    /// <summary>A drawing program's run (DEC-073): planned by DrawingPlanner and scored by <see cref="ProgramChecks"/>.</summary>
    public bool Drawing { get; init; }

    /// <summary>For a drawing: Program, None or NoProgram.</summary>
    public string? Outcome { get; init; }

    /// <summary>For a drawing: the program the stage was given (empty for None and NoProgram).</summary>
    public string? Program { get; init; }

    [JsonIgnore]
    public List<string> SystemPrompts { get; init; } = new();
}

/// <summary>
/// The case loop shared by PE-9, <c>--record</c> and <c>--replay</c> (DEC-044/DEC-073): build the drawing request
/// the App Shell would build, draw it through the real DrawingPlanner over whatever client the caller gives, score
/// it. Follow-ups are sent the program last drawn for their previous case. The VisualPlan loop went with the
/// VisualPlan stack in FEATURE-011 step 10 (DEC-077).
/// </summary>
internal static class EvalRun
{
    public static readonly PixelRect Desktop = new(0, 0, 2560, 1440);

    /// <summary>The drawing request the App Shell will build for a case (step 7): the eval desktop is the canvas.</summary>
    public static DrawingRequest DrawRequest(EvalCase c, DrawingFollowUp? followUp) => new()
    {
        Question = c.Question,
        CanvasWidth = Desktop.Width,
        CanvasHeight = Desktop.Height,
        PointedAtBox = c.Screen?.TargetBox,
        ScreenText = c.Screen?.Description,
        FollowUp = followUp,
    };

    /// <summary>
    /// Draws and scores one case (DEC-073): an authentication failure and
    /// cancellation propagate, any other provider fault is recorded on the case. A follow-up is asked about the program
    /// last drawn for its previous case. With <paramref name="stage"/> the program also runs on the real stage and gets
    /// P-1..P-5; without one (offline) only the static checks.
    /// shortcut: a follow-up's own drawing is remembered alone, not merged with the one it kept; the set's follow-ups
    /// are one deep, so nothing reads it yet.
    /// </summary>
    public static async Task<CaseRun> RunDrawingAsync(EvalCase c, ILlmClient client, IDictionary<string, DrawingFollowUp> drawings, ProgramStage? stage, CancellationToken ct = default)
    {
        var followUp = c.Previous is { } p && drawings.TryGetValue(p, out var f) ? f : null;
        var recording = new RecordingLlmClient(client);
        DrawingResult result;
        try
        {
            result = await new DrawingPlanner(recording).DrawAsync(DrawRequest(c, followUp), null, ct).ConfigureAwait(false);
        }
        catch (ProviderException ex) when (ex.Kind != ProviderErrorKind.AuthFailed)
        {
            return new CaseRun
            {
                Id = c.Id,
                Drawing = true,
                Checks = new(),
                Answers = recording.Answers,
                Partial = recording.LastAnswerPartial,
                Error = ex.Kind.ToString(),
                SystemPrompts = Prompts(recording),
            };
        }

        var sent = recording.Requests.Count > 0 ? recording.Requests[0].Messages.Last(m => m.Role == "user").Text : null;
        var run = stage is not null && result.Outcome == DrawingOutcome.Program ? await stage.RunAsync(result.Program, Desktop.Width, Desktop.Height).ConfigureAwait(false) : null;
        var checks = ProgramChecks.Score(c, result.Outcome, result.Program, followUp?.Program, sent, run);
        if (result.Outcome == DrawingOutcome.Program)
        {
            drawings[c.Id] = new DrawingFollowUp((followUp?.Questions ?? Array.Empty<string>()).Append(c.Question).ToList(), result.Program);
        }

        return new CaseRun
        {
            Id = c.Id,
            Drawing = true,
            Outcome = result.Outcome.ToString(),
            Program = result.Program,
            Checks = checks.ToDictionary(r => r.Id, r => r.Passed),
            Details = checks.Select(r => $"{(r.Passed ? "pass" : "fail")} {r.Id} {r.Detail}").ToList(),
            Answers = recording.Answers,
            Partial = recording.LastAnswerPartial,
            // The first program text the stage could have started on: the drawing's "first beat".
            FirstBeatMs = result.FirstProgramLatency?.TotalMilliseconds,
            LatencyMs = result.Latency.TotalMilliseconds,
            InputTokens = result.InputTokens,
            OutputTokens = result.OutputTokens,
            Attempts = 1,
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
