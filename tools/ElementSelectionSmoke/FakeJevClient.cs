using HeyGanesh.ElementSelection;

namespace HeyGanesh.Tools.ElementSelectionSmoke;

/// <summary>
/// Offline fake used with --fake: answers the choice question with the FIRST offered non-"none"
/// option — i.e. the top-ranked real candidate from the real enumeration — so the
/// enumerate → request → parse → map path is exercised without network, a key, or fabricated
/// geometry. --fake-none flips the answer to the "none" escape option (not-found path).
/// </summary>
internal sealed class FakeJevClient : IJevClient
{
    private readonly bool _returnNone;

    public FakeJevClient(bool returnNone) => _returnNone = returnNone;

    public Task<JevRawResponse> EvaluateAsync(JevRequest request, CancellationToken cancellationToken)
    {
        string choiceId = "none";
        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);

        if (request.Questions.TryGetValue("target", out var target) && target.Criteria is { } criteria)
        {
            if (!_returnNone)
            {
                choiceId = criteria.Keys.FirstOrDefault(k => k != "none") ?? "none";
            }

            double winnerProb = choiceId == "none" ? 0.62 : 0.91;
            double rest = Math.Max(0.0, (1.0 - winnerProb) / Math.Max(1, criteria.Count - 1));
            foreach (var key in criteria.Keys)
            {
                probabilities[key] = key == choiceId ? winnerProb : rest;
            }
        }

        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["target"] = new JevAnswer("choice", choiceId, null, 0.88, probabilities),
            ["plausible"] = new JevAnswer("noul", null, _returnNone ? 0.12 : 0.96, null, null),
        };

        return Task.FromResult(new JevRawResponse("offline-fake", answers, 128, 24, TimeSpan.FromMilliseconds(3)));
    }
}
