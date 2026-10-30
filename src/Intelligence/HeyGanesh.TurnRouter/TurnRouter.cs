using HeyGanesh.ElementSelection;
using System.Diagnostics;
using System.Text.Json.Nodes;

namespace HeyGanesh.TurnRouter;

/// <summary>
/// Decides how one turn is taught with a single Jev call that answers up to three typed choices:
/// intent (point / explain / lesson), dimension (2D / 3D) and catalogue model (DEC-035).
/// </summary>
/// <remarks>
/// The pattern is typesafe-computer-use's: the classifier picks, code decides facts. Everything that
/// can be computed (does the screen match, which models exist) is computed by the caller and sent as
/// state; Jev only chooses among option ids we offered, and an id we never offered refuses the whole
/// route, exactly as Element Selection refuses an unknown candidate id. Provider faults and
/// cancellation are never caught here: the caller decides what a failed router means.
/// </remarks>
public sealed class TurnRouter
{
    public const string IntentQuestionId = "intent";
    public const string DimensionQuestionId = "dimension";
    public const string ModelQuestionId = "model";

    /// <summary>The model question's "no listed model fits" option.</summary>
    public const string NoModelOptionId = "none";

    private const string Flat = "2d";
    private const string Scene = "3d";

    private readonly IJevClient _client;
    private readonly TurnRouterOptions _options;

    public TurnRouter(IJevClient client, TurnRouterOptions? options = null)
    {
        _client = client ?? throw new ArgumentNullException(nameof(client));
        _options = options ?? new TurnRouterOptions();
    }

    /// <summary>Asks Jev once and maps its answers to a <see cref="TurnRoute"/>. Throws only for a bad request, a provider fault or cancellation.</summary>
    public async Task<TurnRoute> RouteAsync(RouteRequest request, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (string.IsNullOrWhiteSpace(request.Question))
        {
            throw new ArgumentException("A route needs the user's question.", nameof(request));
        }

        var models = Distinct(request.Models);
        var jevRequest = BuildRequest(request, models);

        var clock = Stopwatch.StartNew();
        var response = await _client.EvaluateAsync(jevRequest, cancellationToken).ConfigureAwait(false);
        clock.Stop();

        return Map(response, models, request.Schematics, clock.Elapsed);
    }

    private JevRequest BuildRequest(RouteRequest request, IReadOnlyList<RouteModel> models)
    {
        // Question ids are never shown to the model, so each instruction carries the question itself.
        var asked = $"The user of a desktop tutor asked: \"{request.Question}\". ";

        var questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
        {
            [IntentQuestionId] = new()
            {
                Type = "choice",
                Instructions = asked + "How should the tutor answer? Choose exactly one option id.",
                Criteria = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    ["point"] = "Find and highlight one thing on the user's screen; the highlight is the whole answer.",
                    ["explain"] = "Explain in a few spoken sentences; nothing needs drawing.",
                    ["lesson"] = "Teach with drawn step-by-step cards: a concept, a process, a derivation or a structure.",
                },
            },
        };

        if (models.Count > 0)
        {
            // Asked even when the intent may turn out not to be a lesson: one call answers everything,
            // and the mapping simply ignores these two answers for point and explain.
            questions[DimensionQuestionId] = new()
            {
                Type = "choice",
                Instructions = asked + "If this is taught as a drawn lesson, is it best drawn flat or as a 3D model? Choose exactly one option id.",
                Criteria = new Dictionary<string, string?>(StringComparer.Ordinal)
                {
                    [Flat] = "Flat cards: text, equations, plots, arrows and steps.",
                    [Scene] = request.Schematics
                        ? "A 3D model the user can turn: a physical object or structure, from the list of models or, when none fits, a simplified 3D drawing of it."
                        : "A 3D model the user can turn and click: a physical object or structure from the list of models.",
                },
            };

            var criteria = new Dictionary<string, string?>(StringComparer.Ordinal);
            foreach (var model in models)
            {
                criteria[model.Id] = $"{model.Title} ({model.Subject})";
            }

            criteria[NoModelOptionId] = request.Schematics
                ? "None of the listed models shows what the user asked about; a simplified 3D drawing of it is made instead."
                : "None of the listed models shows what the user asked about.";
            questions[ModelQuestionId] = new()
            {
                Type = "choice",
                Instructions = asked + "Which listed 3D model best shows what the user asked about? Choose exactly one option id.",
                Criteria = criteria,
            };
        }

        // Facts computed in code. The question is deliberately not repeated here: it already lives in
        // every instruction.
        var state = new JsonObject
        {
            ["window_title"] = request.WindowTitle,
            ["screen_match"] = request.ScreenMatchFound is { } found ? (found ? "found" : "not found") : "not checked",
            ["question_words"] = request.Question.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries).Length,
            ["model_count"] = models.Count,
        };

        return new JevRequest { Model = _options.ModelId, State = state, Questions = questions };
    }

    private TurnRoute Map(JevRawResponse response, IReadOnlyList<RouteModel> models, bool schematics, TimeSpan latency)
    {
        var common = new TurnRoute
        {
            JevModel = response.Model,
            Latency = latency,
            InputTokens = response.InputTokens,
            OutputTokens = response.OutputTokens,
        };

        var intentAnswer = response.Answers.GetValueOrDefault(IntentQuestionId);
        var intentScore = Score(intentAnswer);
        common = common with { IntentScore = intentScore };

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

        if (intent != TurnIntent.Lesson)
        {
            return common with { Routed = true, Intent = intent };
        }

        if (models.Count == 0)
        {
            return common with { Routed = true, Intent = intent, Dimension = LessonDimension.Flat };
        }

        var dimensionAnswer = response.Answers.GetValueOrDefault(DimensionQuestionId);
        var modelAnswer = response.Answers.GetValueOrDefault(ModelQuestionId);
        var dimensionScore = Score(dimensionAnswer);
        var modelScore = Score(modelAnswer);
        common = common with { DimensionScore = dimensionScore, ModelScore = modelScore };

        // An id we never offered is a refusal whatever the other answers say: Jev answering outside its
        // options means none of this call can be trusted.
        if (dimensionAnswer?.Choice is { } d && d != Flat && d != Scene)
        {
            return common with { Reason = $"Jev chose dimension '{d}', which was never offered." };
        }

        if (modelAnswer?.Choice is { } m && m != NoModelOptionId && models.All(x => x.Id != m))
        {
            return common with { Reason = $"Jev chose model '{m}', which was never offered." };
        }

        var lesson = common with { Routed = true, Intent = intent, Dimension = LessonDimension.Flat };

        // Every unsure step on the way to 3D lands on a 2D lesson, which can always teach: the lesson
        // was wanted, only the canvas was in doubt.
        if (dimensionAnswer?.Choice != Scene)
        {
            return dimensionAnswer is null ? lesson with { Reason = "no dimension answer; drawing in 2D." } : lesson;
        }

        if (dimensionScore < _options.MinScore)
        {
            return lesson with { Reason = $"3D scored {dimensionScore:0.00}, below {_options.MinScore:0.00}; drawing in 2D." };
        }

        if (modelAnswer?.Choice is null)
        {
            return lesson with { Reason = "3D was chosen but no model answer came back; drawing in 2D." };
        }

        if (modelAnswer.Choice == NoModelOptionId)
        {
            // DEC-051: a caller that can draw a lesson's own simplified model gets one instead of 2D. Its
            // score is gated like any model's, so an unsure "none" is still a 2D lesson.
            if (!schematics)
            {
                return lesson with { Reason = "3D was chosen but no listed model fits; drawing in 2D." };
            }

            if (modelScore < _options.MinScore)
            {
                return lesson with { Reason = $"model 'none' scored {modelScore:0.00}, below {_options.MinScore:0.00}; drawing in 2D." };
            }

            return lesson with { Dimension = LessonDimension.Scene3d, Schematic = true, Reason = "no listed model fits; the lesson draws a simplified 3D model." };
        }

        if (modelScore < _options.MinScore)
        {
            return lesson with { Reason = $"model '{modelAnswer.Choice}' scored {modelScore:0.00}, below {_options.MinScore:0.00}; drawing in 2D." };
        }

        return lesson with { Dimension = LessonDimension.Scene3d, ModelId = modelAnswer.Choice };
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

    /// <summary>First entry wins on a repeated id; a model with the reserved id "none" is a caller bug.</summary>
    private static IReadOnlyList<RouteModel> Distinct(IReadOnlyList<RouteModel>? models)
    {
        var list = new List<RouteModel>();
        foreach (var model in models ?? Array.Empty<RouteModel>())
        {
            ArgumentNullException.ThrowIfNull(model);
            if (model.Id == NoModelOptionId)
            {
                throw new ArgumentException($"'{NoModelOptionId}' is reserved for the no-model option.", nameof(models));
            }

            if (list.All(x => x.Id != model.Id))
            {
                list.Add(model);
            }
        }

        return list;
    }
}
