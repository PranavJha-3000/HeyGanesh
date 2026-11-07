using HeyGanesh.ElementSelection;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace HeyGanesh.TurnRouter;

/// <summary>
/// Decides how one turn is taught with a single Jev call that answers one typed choice: point, explain
/// or lesson (DEC-035). Since DEC-075 the router no longer asks 2D or 3D, or which catalogue model: the
/// drawing program decides that itself on the drawing stage.
/// </summary>
/// <remarks>
/// The pattern is typesafe-computer-use's: the classifier picks, code decides facts. Everything that
/// can be computed (does the screen match) is computed by the caller and sent as state; Jev only
/// chooses among option ids we offered, and an id we never offered refuses the route, exactly as
/// Element Selection refuses an unknown candidate id. Provider faults and cancellation are never
/// caught here: the caller decides what a failed router means.
/// </remarks>
public sealed class TurnRouter
{
    public const string IntentQuestionId = "intent";

    private readonly IJevClient _client;
    private readonly TurnRouterOptions _options;

    public TurnRouter(IJevClient client, TurnRouterOptions? options = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? new TurnRouterOptions();
    }

    /// <summary>Asks Jev once and maps its answer to a <see cref="TurnRoute"/>. Throws only for a bad request, a provider fault or cancellation.</summary>
    public async Task<TurnRoute> RouteAsync(RouteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new ArgumentException("A route needs the user's question.", nameof(request));
        }

        var jevRequest = BuildRequest(request);

        var clock = Stopwatch.StartNew();
        var response = await _client.EvaluateAsync(jevRequest, cancellationToken).ConfigureAwait(false);
        clock.Stop();

        return Map(response, clock.Elapsed);
    }

    private JevRequest BuildRequest(RouteRequest request)
    {
        // Question ids are never shown to the model, so the instruction carries the question itself.
        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            [IntentQuestionId] = new()
            {
                Type = "choice",
                Instructions = $"The user of a desktop tutor asked: \"{request.Question}\". How should the tutor answer? Choose exactly one option id.",
                Criteria = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["point"] = "Find and highlight one thing on the user's screen; the highlight is the whole answer.",
                    ["explain"] = "Explain in a few spoken sentences; nothing needs drawing.",
                    ["lesson"] = "Teach with a spoken drawing, flat or 3D: a concept, a process, a derivation, a structure or an object.",
                },
            },
        };

        // Facts computed in code. The question is deliberately not repeated here: it already lives in
        // the instruction.
        var state = new JsonObject
        {
            ["window_title"] = request.WindowTitle,
            ["screen_match"] = request.ScreenMatchFound is { } found ? (found ? "found" : "not found") : "not checked",
            ["question_words"] = request.Question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
        };

        return new JevRequest { Model = _options.ModelId, State = state, Questions = questions };
    }

    private TurnRoute Map(JevRawResponse response, TimeSpan latency)
    {
        var intentAnswer = response.Answers.GetValueOrDefault(IntentQuestionId);
        var intentScore = Score(intentAnswer);
        var common = new TurnRoute
        {
            JevModel = response.Model,
            Latency = latency,
            InputTokens = response.InputTokens,
            OutputTokens = response.OutputTokens,
            IntentScore = intentScore,
        };

        TurnIntent intent;
        switch (intentAnswer?.Choice)
        {
            case null:
                return common with { Reason = "Jev returned no answer for the intent question." };
            case "point": intent = TurnIntent.Point; break;
            case "explain": intent = TurnIntent.Explain; break;
            case "lesson": intent = TurnIntent.Lesson; break;
            default:
                return common with { Reason = $"Jev chose intent '{intentAnswer.Choice}', which was never offered." };
        }

        if (intentScore < _options.MinScore)
        {
            return common with { Reason = $"intent '{intentAnswer.Choice}' scored {intentScore:0.00}, below {_options.MinScore:0.00}." };
        }

        return common with { Routed = true, Intent = intent };
    }

    /// <summary>
    /// Jev's own confidence when it gives one, otherwise the chosen option's probability, otherwise 0 —
    /// so a bare answer with no score is treated as unsure rather than trusted.
    /// </summary>
    private static double Score(JevAnswer? answer)
    {
        if (answer is null)
        {
            return 0;
        }

        if (answer.Confidence is { } confidence)
        {
            return confidence;
        }

        return answer.Choice is not null && answer.Probabilities?.TryGetValue(answer.Choice, out var p) == true ? p : 0;
    }
}
