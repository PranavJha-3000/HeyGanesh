namespace HeyGanesh.ElementSelection;

// Second half of ElementSelectionService, part 1: Jev request assembly.
// (Split across files only to satisfy the editor's per-call size limit; partial by necessity.)

public sealed partial class ElementSelectionService
{
    private JevRequest BuildRequest(string question, TargetWindowInfo target, IReadOnlyList<UiaCandidate> manifest)
    {
        // Option ids are the criteria keys and come back verbatim in the answer — that exact round
        // trip is what makes candidate→coordinates mapping trustworthy (no fuzzy text matching).
        var criteria = new Dictionary<string, string?>(StringComparer.Ordinal);
        foreach (var candidate in manifest)
        {
            criteria[candidate.Id] =
                $"{candidate.ControlType} '{candidate.Name}' ({candidate.PositionHint}, {candidate.Rect.Width}x{candidate.Rect.Height} px)";
        }

        if (_options.IncludeNoneOption)
        {
            criteria[NoneOptionId] = "No listed element matches the request";
        }

        var choice = new JevQuestion
        {
            Type = "choice",
            Instructions =
                $"The user is looking at the window \"{target.Title}\" and asked: \"{question}\". " +
                "Listed below are interactive UI elements from that window, by option id. " +
                "Which single element best matches the user's request? Answer with exactly one option id. " +
                (_options.IncludeNoneOption
                    ? $"If no element plausibly matches the request, answer \"{NoneOptionId}\"."
                    : string.Empty),
            Criteria = criteria,
        };

        var plausible = new JevQuestion
        {
            Type = "noul",
            Instructions = "Does at least one of the listed UI elements plausibly match the user's request?",
            Criteria = new Dictionary<string, string?>(StringComparer.Ordinal)
            {
                ["true"] = "A listed element plausibly matches the request.",
                ["false"] = "No listed element plausibly matches the request.",
            },
        };

        // State carries only ambient context — the full user request already lives in Instructions
        // (the provider documents that our question ids are never shown to the model).
        var state = new System.Text.Json.Nodes.JsonObject
        {
            ["window_title"] = target.Title,
            ["window_rect"] = $"{target.WindowRect.Width}x{target.WindowRect.Height}+{target.WindowRect.X}+{target.WindowRect.Y}",
            ["candidate_count"] = manifest.Count,
        };

        return new JevRequest
        {
            Model = _options.ModelId,
            State = state,
            Questions = new Dictionary<string, JevQuestion>(StringComparer.Ordinal)
            {
                [TargetQuestionId] = choice,
                [PlausibleQuestionId] = plausible,
            },
        };
    }
}
