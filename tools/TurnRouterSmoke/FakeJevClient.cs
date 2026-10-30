using HeyGanesh.ElementSelection;

namespace HeyGanesh.Tools.TurnRouterSmoke;

/// <summary>
/// Records every request and answers with a scripted response, or throws a scripted exception. Honours
/// cancellation the way a real HTTP client would: a cancelled token throws before anything is "sent".
/// </summary>
internal sealed class FakeJevClient : IJevClient
{
    private readonly Func<JevRequest, JevRawResponse>? _answer;
    private readonly Exception? _throw;

    public FakeJevClient(Func<JevRequest, JevRawResponse> answer) => _answer = answer;

    public FakeJevClient(Exception toThrow) => _throw = toThrow;

    public List<JevRequest> Recorded { get; } = new();

    public Task<JevRawResponse> EvaluateAsync(JevRequest request, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        Recorded.Add(request);
        if (_throw is not null)
        {
            throw _throw;
        }

        return Task.FromResult(_answer!(request));
    }

    /// <summary>A response whose answers are (question id, choice, confidence) triples; a null choice leaves the answer out.</summary>
    public static JevRawResponse Answers(params (string Id, string? Choice, double? Confidence)[] answers)
    {
        var map = new Dictionary<string, JevAnswer>(StringComparer.Ordinal);
        foreach (var (id, choice, confidence) in answers)
        {
            if (choice is not null)
            {
                map[id] = new JevAnswer("choice", choice, null, confidence, null);
            }
        }

        return new JevRawResponse("jev-1.13.0", map, 420, 0, TimeSpan.FromMilliseconds(150));
    }
}
