using System.Runtime.CompilerServices;
using System.Text;
using HeyGanesh.Providers.Core;

namespace HeyGanesh.Tools.PlannerEval;

/// <summary>
/// Wraps the client one case is planned with and keeps what crossed it: every request (so the checks can
/// read the prompt that was sent) and every answer's full text, in call order (a repair makes two). The
/// answers are what a recording stores and a replay feeds back. Adds nothing to a request; never disposes
/// the inner client, which the run owns.
/// </summary>
internal sealed class RecordingLlmClient : ILlmClient
{
    private readonly ILlmClient _inner;

    public RecordingLlmClient(ILlmClient inner) => _inner = inner;

    public string ProviderId => _inner.ProviderId;

    public string ModelId => _inner.ModelId;

    public ProviderCapability Capabilities => _inner.Capabilities;

    public List<LlmRequest> Requests { get; } = new();

    public List<string> Answers { get; } = new();

    /// <summary>True when the last stream stopped before it finished: its answer is partial and cannot be replayed as-is.</summary>
    public bool LastAnswerPartial { get; private set; }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default)
    {
        Requests.Add(request);
        var response = await _inner.GenerateAsync(request, ct).ConfigureAwait(false);
        Answers.Add(response.Text ?? string.Empty);
        LastAnswerPartial = false;
        return response;
    }

    public async IAsyncEnumerable<LlmStreamChunk> StreamAsync(LlmRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        Requests.Add(request);
        var text = new StringBuilder();
        var index = Answers.Count;
        Answers.Add(string.Empty);
        LastAnswerPartial = true;
        await foreach (var chunk in _inner.StreamAsync(request, ct).ConfigureAwait(false))
        {
            text.Append(chunk.TextDelta);
            Answers[index] = text.ToString();
            yield return chunk;
        }

        LastAnswerPartial = false;
    }

    public void Dispose()
    {
    }
}
