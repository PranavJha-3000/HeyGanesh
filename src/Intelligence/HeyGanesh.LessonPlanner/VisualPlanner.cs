using HeyGanesh.Providers.Core;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.LessonPlanner;

/// <summary>
/// Asks a model for a lesson and returns it validated. Multi-model by construction (DEC-024): it is
/// handed an <see cref="ILlmClient"/> and decides only from that client's advertised capabilities.
/// Multimodal: the screen image goes only to a model with <see cref="ProviderCapability.Vision"/>;
/// otherwise the prompt says plainly that no image is attached, and the result records why.
/// </summary>
/// <remarks>
/// Never throws on model output: an answer that is not a lesson becomes one repair call and then a
/// validated outcome (possibly <see cref="LessonOutcome.Rejected"/>). Provider faults and
/// cancellation propagate unchanged for the shell to map. The client is caller-owned; this class
/// never disposes it.
/// </remarks>
public sealed partial class VisualPlanner : IDisposable
{
    private readonly ILlmClient _client;
    private readonly bool _ownsClient;
    private readonly LessonPlannerOptions _options;
    private readonly VisualPlanValidator _validator = new();

    /// <summary>A planner over a caller-owned client: <see cref="Dispose"/> never touches it.</summary>
    public VisualPlanner(ILlmClient client, LessonPlannerOptions? options = null)
        : this(client, options, ownsClient: false)
    {
    }

    private VisualPlanner(ILlmClient client, LessonPlannerOptions? options, bool ownsClient)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? LessonPlannerOptions.Default;
        _ownsClient = ownsClient;
    }

    /// <summary>
    /// A planner over whatever provider is assigned to the Tutor role (no provider named here). The
    /// registry builds a fresh client (and its HTTP stack) on every Resolve, so this planner owns that
    /// client and disposes it — create one per session, not one per question.
    /// </summary>
    public static VisualPlanner FromRegistry(IProviderRegistry registry, LessonPlannerOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(registry);
        var resolved = registry.Resolve(ProviderRole.Tutor);
        return new VisualPlanner(resolved.LlmClient ?? throw new InvalidOperationException($"the Tutor role ({resolved.ProviderId}) has no LLM client"), options, ownsClient: true);
    }

    public void Dispose()
    {
        if (_ownsClient)
        {
            _client.Dispose();
        }
    }

    public async Task<PlanResult> PlanAsync(PlanRequest request, CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(request);
        return await RunAttemptsAsync(prepared, firstAnswer: null, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>What every planning call needs, built once: the shared half of PlanAsync and PlanStreamingAsync.</summary>
    private sealed record Prepared(ValidationContext Context, LlmImagePart? Image, string? ImageNote, string System, LlmMessage User);

    private Prepared Prepare(PlanRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new ArgumentException("a lesson needs a question", nameof(request));
        }

        var (image, imageNote) = ChooseImage(request.Screen);
        var context = new ValidationContext(request.VirtualDesktop)
        {
            RequireGroundedMarks = true,
            GroundedTarget = request.Screen?.TargetBox,
            GroundedMarginPx = _options.MarkMarginPx,
            Scene3d = request.Scene3d,
            SchematicGeometry = request.SchematicGeometry,
        };

        var user = new LlmMessage
        {
            Role = "user",
            Text = PlannerPrompt.User(request, _options, image is not null, imageNote),
            Images = image is null ? null : new[] { image },
        };
        // Prompt and validator see the same catalogue (DEC-032): 3D is offered exactly when it would be accepted.
        var system = PlannerPrompt.System(LessonLimits.Default, request.Scene3d, schematics: request.Scene3d is not null && request.SchematicGeometry is not null);
        return new Prepared(context, image, imageNote, system, user);
    }

    /// <summary>
    /// The attempt loop: ask, validate, repair once if needed, keep the better answer. With
    /// <paramref name="firstAnswer"/> the first attempt is that answer (a finished stream, DEC-041)
    /// instead of a call; everything after it is unchanged.
    /// </summary>
    private async Task<PlanResult> RunAttemptsAsync(Prepared prepared, LlmResponse? firstAnswer, CancellationToken cancellationToken)
    {
        var (context, image, imageNote, system, user) = prepared;
        var messages = new List<LlmMessage> { user };

        LessonValidationResult? best = null;
        IReadOnlyList<LessonIssue> firstIssues = Array.Empty<LessonIssue>();
        int attempts = 0, input = 0, output = 0;
        var latency = TimeSpan.Zero;
        var modelId = _client.ModelId;
        string? repairError = null;

        while (attempts < Math.Max(1, _options.MaxAttempts))
        {
            attempts++;
            LlmResponse response;
            try
            {
                response = attempts == 1 && firstAnswer is not null ? firstAnswer : await _client.GenerateAsync(new LlmRequest
                {
                    SystemPrompt = system,
                    Messages = messages.ToArray(),
                    MaxOutputTokens = _options.MaxOutputTokens,
                    Temperature = _options.Temperature,
                }, cancellationToken).ConfigureAwait(false);
            }
            catch (ProviderException ex) when (best is not null)
            {
                // The repair call failed (timeout, rate limit, 5xx) but a usable lesson is already in
                // hand: teach with it rather than throw it away. The first call's faults still propagate.
                repairError = $"{ex.Kind}: repair call failed; kept the first answer";
                break;
            }

            input += response.InputTokens;
            output += response.OutputTokens;
            latency += response.Latency;
            if (!string.IsNullOrEmpty(response.ModelId))
            {
                modelId = response.ModelId;
            }

            var lesson = _validator.Validate(VisualPlanParser.Parse(response.Text ?? string.Empty), context);
            if (attempts == 1)
            {
                firstIssues = lesson.Issues;
            }

            // Keep the better answer; on a tie the later (repaired) one.
            if (best is null || Rank(lesson.Outcome) >= Rank(best.Outcome))
            {
                best = lesson;
            }

            if (lesson.Outcome == LessonOutcome.Valid)
            {
                break;
            }

            messages.Add(new LlmMessage { Role = "assistant", Text = response.Text ?? string.Empty });
            messages.Add(new LlmMessage { Role = "user", Text = PlannerPrompt.Repair(lesson.Issues) });
        }

        return new PlanResult(best!, attempts, image is not null, imageNote, modelId, input, output, latency, firstIssues, repairError);
    }

    private (LlmImagePart? Image, string? Note) ChooseImage(ScreenGrounding? screen)
    {
        if (screen?.Jpeg is not { Length: > 0 } jpeg)
        {
            return (null, null);
        }

        if (!_client.Capabilities.HasFlag(ProviderCapability.Vision))
        {
            return (null, $"model '{_client.ModelId}' cannot read images; planning from the screen description only");
        }

        if (jpeg.Length > _options.MaxImageBytes)
        {
            return (null, $"screen image is {jpeg.Length} bytes (max {_options.MaxImageBytes}); planning from the description only");
        }

        return (new LlmImagePart { MimeType = "image/jpeg", Data = Convert.ToBase64String(jpeg) }, null);
    }

    private static int Rank(LessonOutcome outcome) => outcome switch
    {
        LessonOutcome.Valid => 3,
        LessonOutcome.PartiallyDegraded => 2,
        LessonOutcome.TextFallback => 1,
        _ => 0,
    };
}
