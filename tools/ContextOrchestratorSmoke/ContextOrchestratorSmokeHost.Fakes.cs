using HeyGanesh.ElementSelection;
using HeyGanesh.Vision;

namespace HeyGanesh.Tools.ContextOrchestratorSmoke;

/// <summary>
/// The behaviour the offline fake Jev client answers with. Each mode maps to one row of the
/// orchestrator's contract rather than to a candidate: TopCandidate/None are verdicts, Unmapped and
/// Error are the two documented ways the primary leg fails.
/// </summary>
internal enum FakeJevMode
{
    /// <summary>Answer the first offered non-"none" option — the top-ranked real candidate. Primary verdict.</summary>
    TopCandidate,

    /// <summary>Answer the "none" escape option — an honest no-match, which must NOT reach Vision.</summary>
    None,

    /// <summary>Answer an id that was never offered — the hallucination guard fails the primary leg, so Vision must run once.</summary>
    Unmapped,

    /// <summary>Throw a provider exception — the orchestrator must propagate it, never turn it into a verdict.</summary>
    Error,

    /// <summary>Answer the top candidate but unsure (blended 0.325, the Low tier): Vision must check it once (DEC-037).</summary>
    Low,
}

/// <summary>
/// Offline fake for the Element Selection transport seam. It answers over the REAL enumeration of the
/// real window, so ranking, id assignment and mapping all still run; only the provider call is faked.
/// </summary>
internal sealed class FakeJevClient : IJevClient
{
    private readonly FakeJevMode _mode;
    private readonly int _delayMs;

    public FakeJevClient(FakeJevMode mode, int delayMs)
    {
        _mode = mode;
        _delayMs = delayMs;
    }

    public async Task<JevRawResponse> EvaluateAsync(JevRequest request, CancellationToken cancellationToken)
    {
        // The delay exists so cancellation and the re-entrancy guard can be observed while a session is
        // genuinely in flight; it is inside the client because that is the leg that can be made slow.
        if (_delayMs > 0)
        {
            await Task.Delay(_delayMs, cancellationToken).ConfigureAwait(false);
        }

        if (_mode == FakeJevMode.Error)
        {
            throw new JevClientException("offline fake provider failure (HTTP 500)", 500);
        }

        var choiceId = _mode switch
        {
            FakeJevMode.None => "none",
            FakeJevMode.Unmapped => "c999",
            _ => FirstOfferedId(request) ?? "none",
        };

        var probabilities = new Dictionary<string, double>(StringComparer.Ordinal);
        if (request.Questions.TryGetValue("target", out var target) && target.Criteria is { } criteria)
        {
            double winnerProb = choiceId == "none" ? 0.62 : _mode == FakeJevMode.Low ? 0.35 : 0.91;
            double rest = Math.Max(0.0, (1.0 - winnerProb) / Math.Max(1, criteria.Count - 1));
            foreach (var key in criteria.Keys)
            {
                probabilities[key] = key == choiceId ? winnerProb : rest;
            }
        }

        var answers = new Dictionary<string, JevAnswer>(StringComparer.Ordinal)
        {
            ["target"] = new JevAnswer("choice", choiceId, null, _mode == FakeJevMode.Low ? 0.30 : 0.88, probabilities),
            ["plausible"] = new JevAnswer("noul", null, choiceId == "none" ? 0.12 : 0.96, null, null),
        };

        return new JevRawResponse("offline-fake", answers, 128, 24, TimeSpan.FromMilliseconds(3));
    }

    private static string? FirstOfferedId(JevRequest request)
        => request.Questions.TryGetValue("target", out var target) && target.Criteria is { } criteria
            ? criteria.Keys.FirstOrDefault(k => k != "none")
            : null;
}

/// <summary>
/// Offline fake for the Vision transport seam, counting the calls it receives. The box is expressed on
/// the model's 0..1000 grid like a real answer, so Vision's own mapping into screen pixels still runs.
/// <c>Found = false</c> models the model answering honestly that the element is not on screen.
/// </summary>
internal sealed class CountingVisionClient : IVisionClient
{
    private readonly bool _notFound;
    private int _calls;

    public CountingVisionClient(bool notFound) => _notFound = notFound;

    /// <summary>How many provider calls this client has answered.</summary>
    public int Count => Volatile.Read(ref _calls);

    public Task<VisionRawResponse> AskAsync(VisionRequest request, CancellationToken cancellationToken)
    {
        Interlocked.Increment(ref _calls);

        var json = _notFound
            ? """{"found": false, "note": "offline fake: the element is not on this frame"}"""
            : """{"found": true, "element": "offline fallback probe", "box": {"x1": 240, "y1": 120, "x2": 360, "y2": 220}, "confidence": 0.86, "note": "fake"}""";

        return Task.FromResult(new VisionRawResponse(json, "offline-fake", 10, 20, TimeSpan.FromMilliseconds(5)));
    }
}
