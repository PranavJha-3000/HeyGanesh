using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;

namespace HeyGanesh.ElementSelection;

// Second half of ElementSelectionService: raw-answer → SelectionResult mapping, including the
// hallucination guard (choice id must exist in the manifest we offered — Jev can never make us
// emit coordinates for something that is not a real element).

public sealed partial class ElementSelectionService
{
    private SelectionResult MapResponse(
        JevRawResponse response,
        CandidateSet set,
        IReadOnlyList<UiaCandidate> manifest,
        TimeSpan latency)
    {
        var targetAnswer = response.Answers.GetValueOrDefault(TargetQuestionId);
        var plausibleAnswer = response.Answers.GetValueOrDefault(PlausibleQuestionId);
        var probabilities = targetAnswer?.Probabilities;

        double? winnerProbability = null;
        if (probabilities is not null && targetAnswer!.Choice is not null &&
            probabilities.TryGetValue(targetAnswer.Choice, out var wp))
        {
            winnerProbability = wp;
        }

        double? noneProbability = probabilities is not null && probabilities.TryGetValue(NoneOptionId, out var np)
            ? np
            : null;

        var common = new SelectionResult
        {
            Target = set.Target,
            TotalEnumerated = set.TotalEnumerated,
            TotalAfterFilter = set.InteractiveAfterFilter,
            Candidates = manifest,
            ModelId = response.Model,
            Latency = latency,
            InputTokens = response.InputTokens,
            OutputTokens = response.OutputTokens,
            JevConfidence = targetAnswer?.Confidence,
            WinnerProbability = winnerProbability,
            NoneProbability = noneProbability,
            PlausibleProbability = plausibleAnswer?.Noul,
            Probabilities = probabilities,
        };

        var choice = targetAnswer?.Choice;

        if (choice is null)
        {
            return common with
            {
                Found = false,
                Confidence = ConfidenceLevel.Low,
                FailureReason = "Jev returned no choice for the 'target' question (missing or malformed answer).",
            };
        }

        if (choice == NoneOptionId)
        {
            // A valid, successful "nothing on this window matches" answer — not a failure, and no
            // coordinates exist to hand out (that honesty is why the none option exists, DEC-008).
            return common with { Found = false, Confidence = ConfidenceLevel.Low, Choice = choice };
        }

        var selected = manifest.FirstOrDefault(c => c.Id == choice);
        if (selected is null)
        {
            // Hallucination guard: Jev answered an id we never offered. Refusing to fabricate
            // coordinates here is the whole reason ids round-trip verbatim. Documented fallback trigger.
            return common with
            {
                Found = false,
                Confidence = ConfidenceLevel.Low,
                Choice = choice,
                FailureReason = $"Jev chose option id '{choice}' which is not in the offered manifest " +
                                $"({manifest.Count} candidates) -- refusing to fabricate coordinates.",
            };
        }

        var box = selected.Rect;
        float blended = targetAnswer!.Confidence.HasValue && winnerProbability.HasValue
            ? (float)(0.5 * targetAnswer.Confidence.Value + 0.5 * winnerProbability.Value)
            : (float)(targetAnswer.Confidence ?? winnerProbability ?? 0.5);

        return common with
        {
            Found = true,
            Description = selected.Name,
            BoxOnScreen = box,
            CenterOnScreen = new PointI(box.X + box.Width / 2, box.Y + box.Height / 2),
            // Same 0.8/0.5 boundaries as Vision — keep the two modules' tiers comparable.
            Confidence = blended >= 0.8f ? ConfidenceLevel.High
                       : blended >= 0.5f ? ConfidenceLevel.Medium
                       : ConfidenceLevel.Low,
            Choice = choice,
            Selected = selected,
        };
    }
}
