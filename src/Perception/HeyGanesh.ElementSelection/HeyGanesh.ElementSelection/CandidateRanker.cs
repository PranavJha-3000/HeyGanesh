namespace HeyGanesh.ElementSelection;

/// <summary>
/// Deterministic lexical pre-rank whose only job is to cut the tree down to MaxCandidates before Jev
/// sees it. Jev does the real selection; this exists so a dense window's hundreds of elements stay
/// inside the provider's ~32k-token state budget. Question words that also appear in a candidate's
/// name or control type score higher; area breaks ties (bigger targets first); the original tree
/// order breaks final ties, so the result is reproducible for identical inputs.
/// </summary>
public static class CandidateRanker
{
    // Minimal stopword set: without it, "where is the Save button" scores every button equally on
    // "where"/"is"/"the". Deliberately tiny — extra stopwords can only lower scores, never corrupt.
    private static readonly HashSet<string> StopWords = new(StringComparer.OrdinalIgnoreCase)
    {
        "where", "is", "the", "a", "an", "on", "in", "this", "that", "screen", "window",
        "me", "my", "find", "show", "how", "do", "i", "to", "it", "of", "for", "at", "and", "or",
    };

    public static IReadOnlyList<UiaCandidate> RankAndCap(
        IReadOnlyList<UiaCandidate> candidates,
        string question,
        int cap)
    {
        var tokens = ContentTokens(question);

        return candidates
            .Select((candidate, index) => (candidate, index, score: Score(candidate, tokens)))
            .OrderByDescending(t => t.score)
            .ThenByDescending(t => (long)t.candidate.Rect.Width * t.candidate.Rect.Height)
            .ThenBy(t => t.index)
            .Take(cap)
            .Select(t => t.candidate)
            .ToArray();
    }

    private static double Score(UiaCandidate candidate, HashSet<string> questionTokens)
    {
        if (questionTokens.Count == 0)
        {
            return 0; // No usable content words ("sanity", a single stopword...) — area decides.
        }

        var candidateTokens = ContentTokens(candidate.Name + " " + candidate.ControlType);
        int hits = questionTokens.Count(candidateTokens.Contains);
        return (double)hits / questionTokens.Count;
    }

    internal static HashSet<string> ContentTokens(string text)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var word = new System.Text.StringBuilder();

        foreach (var ch in text)
        {
            if (char.IsLetterOrDigit(ch))
            {
                word.Append(char.ToLowerInvariant(ch));
            }
            else if (word.Length > 0)
            {
                Add(set, word.ToString());
                word.Clear();
            }
        }

        if (word.Length > 0)
        {
            Add(set, word.ToString());
        }

        return set;

        static void Add(HashSet<string> set, string token)
        {
            if (token.Length > 1 && !StopWords.Contains(token))
            {
                set.Add(token);
            }
        }
    }
}
