using System.Runtime.CompilerServices;
using HeyGanesh.Providers.Core;

namespace HeyGanesh.Tools.LessonPlannerSmoke;

/// <summary>
/// Stands in for any provider: records every request and answers from a queue of recorded model
/// answers. Capabilities are configurable so the planner's Vision decision can be exercised.
/// </summary>
internal sealed class FakeLlmClient : ILlmClient
{
    private readonly Queue<string> _answers;

    public FakeLlmClient(ProviderCapability capabilities, params string[] answers)
    {
        Capabilities = capabilities;
        _answers = new Queue<string>(answers);
    }

    public string ProviderId => "fake";

    public string ModelId => "fake-model";

    public ProviderCapability Capabilities { get; }

    public List<LlmRequest> Recorded { get; } = new();

    /// <summary>When set, GenerateAsync throws this instead of answering.</summary>
    public Exception? Throw { get; init; }

    /// <summary>When true, GenerateAsync never answers until cancelled.</summary>
    public bool Hang { get; init; }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default)
    {
        Recorded.Add(request);
        if (Throw is not null)
        {
            throw Throw;
        }

        if (Hang)
        {
            await Task.Delay(Timeout.Infinite, ct);
        }

        return new LlmResponse
        {
            Text = _answers.Count > 0 ? _answers.Dequeue() : string.Empty,
            ModelId = "fake-model",
            InputTokens = 100,
            OutputTokens = 200,
            Latency = TimeSpan.FromMilliseconds(5),
        };
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        await Task.Yield();
        throw new NotSupportedException("the planner does not stream in v1");
#pragma warning disable CS0162 // an iterator needs a yield to compile; it is never reached
        yield break;
#pragma warning restore CS0162
    }

    public void Dispose()
    {
    }
}
