using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.LessonSmoke;

/// <summary>One ordered record of every call either fake seam saw — the playback rows assert on it.</summary>
internal sealed class EventLog
{
    private readonly List<string> _events = new();

    public void Add(string e)
    {
        lock (_events) _events.Add(e);
    }

    public IReadOnlyList<string> Snapshot()
    {
        lock (_events) return _events.ToList();
    }
}

/// <summary>Stands in for the Phase 3 card surface: records what it was asked to draw.</summary>
internal sealed class RecordingCanvas : ILessonCanvas
{
    private readonly EventLog _log;

    public RecordingCanvas(EventLog log) => _log = log;

    public Task ApplyBeatAsync(LessonBeat beat, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _log.Add($"draw:{beat.Id}");
        return Task.CompletedTask;
    }

    public Task ShowNoticeAsync(string notice, CancellationToken cancellationToken)
    {
        _log.Add($"notice:{notice}");
        return Task.CompletedTask;
    }

    public Task ClearAsync(CancellationToken cancellationToken)
    {
        _log.Add("clear");
        return Task.CompletedTask;
    }
}

/// <summary>
/// Stands in for Voice TTS. In gated mode each sentence "plays" until the row releases it, so a row
/// can act mid-speech (pause, skip, stop, cancel) and prove the next beat waits for speech to end.
/// </summary>
internal sealed class RecordingSpeaker : ILessonSpeaker
{
    private readonly EventLog _log;
    private readonly bool _gated;
    private readonly SemaphoreSlim _gate = new(0);
    private readonly SemaphoreSlim _started = new(0);
    private int _calls;

    public RecordingSpeaker(EventLog log, bool gated)
    {
        _log = log;
        _gated = gated;
    }

    /// <summary>Zero-based call index at which SpeakAsync throws, simulating a TTS outage; -1 = never.</summary>
    public int ThrowAtCall { get; set; } = -1;

    public async Task SpeakAsync(string text, CancellationToken cancellationToken)
    {
        var call = Interlocked.Increment(ref _calls) - 1;
        _log.Add($"say:{text}");
        _started.Release();
        if (call == ThrowAtCall)
        {
            throw new InvalidOperationException("tts down");
        }

        if (_gated)
        {
            try
            {
                await _gate.WaitAsync(cancellationToken);
            }
            catch (OperationCanceledException)
            {
                Interlocked.Increment(ref _cancelled);
                throw;
            }
        }
    }

    private int _cancelled;

    /// <summary>How many sentences were cut short through their token (proves a skip reached the speaker).</summary>
    public int CancelledCalls => Volatile.Read(ref _cancelled);

    /// <summary>Waits (bounded) until the next sentence has started playing.</summary>
    public Task<bool> WaitStartedAsync() => _started.WaitAsync(TimeSpan.FromSeconds(5));

    /// <summary>Lets the sentence currently playing finish.</summary>
    public void ReleaseOne() => _gate.Release();
}
