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

    /// <summary>Zero-based call index at which <see cref="Throw"/> fires; -1 = every call.</summary>
    public int ThrowOnCall { get; init; } = -1;

    public bool Disposed { get; private set; }

    /// <summary>When true, GenerateAsync never answers until cancelled.</summary>
    public bool Hang { get; init; }

    // Streaming (LP-11, DEC-041): answers are replayed in ChunkChars pieces. FirstTokenDelay and
    // CharsPerSecond simulate a model's time to first token and output speed; FailAfterChars makes the
    // stream throw a provider fault once that much text has gone out.
    public int ChunkChars { get; init; } = 7;

    public TimeSpan FirstTokenDelay { get; init; }

    public double CharsPerSecond { get; init; }

    public int FailAfterChars { get; init; } = -1;

    /// <summary>How many calls arrived through StreamAsync (the rest of Recorded came through GenerateAsync).</summary>
    public int StreamCalls { get; private set; }

    public async Task<LlmResponse> GenerateAsync(LlmRequest request, CancellationToken ct = default)
    {
        Recorded.Add(request);
        if (Throw is not null && (ThrowOnCall < 0 || ThrowOnCall == Recorded.Count - 1))
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
        Recorded.Add(request);
        StreamCalls++;
        if (Throw is not null && (ThrowOnCall < 0 || ThrowOnCall == Recorded.Count - 1))
        {
            throw Throw;
        }

        var text = _answers.Count > 0 ? _answers.Dequeue() : string.Empty;
        if (FirstTokenDelay > TimeSpan.Zero)
        {
            await Task.Delay(FirstTokenDelay, ct);
        }

        for (var at = 0; at < text.Length; at += ChunkChars)
        {
            ct.ThrowIfCancellationRequested();
            if (FailAfterChars >= 0 && at >= FailAfterChars)
            {
                throw new ProviderException(ProviderErrorKind.Transport, ProviderId, ModelId, "fake stream dropped");
            }

            var piece = text.Substring(at, Math.Min(ChunkChars, text.Length - at));
            if (CharsPerSecond > 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(piece.Length / CharsPerSecond), ct);
            }
            else
            {
                await Task.Yield();
            }

            yield return new LlmStreamChunk { TextDelta = piece };
        }

        yield return new LlmStreamChunk { IsDone = true, InputTokens = 100, OutputTokens = text.Length / 4 };
    }

    public void Dispose() => Disposed = true;
}
