namespace HeyGanesh.VisualLessonEngine.Runtime;

/// <summary>
/// Plays a validated lesson: for each beat it starts the drawing, then the sentence, and moves on
/// only when both are done — so what the student hears and what they see stay together, and the
/// length of the speech sets the pace. Holds no lesson state between plays, so playing the same
/// validated result again is a replay.
/// </summary>
/// <remarks>
/// One lesson at a time: a second <see cref="PlayAsync"/> while one plays is refused, never queued
/// (the same burst rule as the overlay and voice legs). Controls come from another thread (the
/// shell's hotkey or a card button), so they only flip flags and cancel tokens under a lock; the play
/// loop does all the work. A control pressed while idle does nothing and cannot leak into the next play.
/// </remarks>
public sealed class LessonPlayer
{
    public const string FallbackNotice = "I couldn't draw this one, so I'll just explain it.";

    private readonly ILessonCanvas _canvas;
    private readonly ILessonSpeaker _speaker;
    private readonly TimeProvider _time;
    private readonly object _lock = new();
    private int _state = (int)PlayerState.Idle;

    // Live only while a play runs; null otherwise. Guarded by _lock.
    private CancellationTokenSource? _playCts;
    private CancellationTokenSource? _beatCts;
    private bool _stopRequested;
    private bool _pauseRequested;
    private TaskCompletionSource? _resume;

    public LessonPlayer(ILessonCanvas canvas, ILessonSpeaker speaker, TimeProvider? time = null)
    {
        _canvas = canvas ?? throw new ArgumentNullException(nameof(canvas));
        _speaker = speaker ?? throw new ArgumentNullException(nameof(speaker));
        _time = time ?? TimeProvider.System;
    }

    public PlayerState State => (PlayerState)Volatile.Read(ref _state);

    public async Task<PlaybackReport> PlayAsync(LessonValidationResult lesson, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(lesson);
        var none = Array.Empty<BeatRecord>();
        if (lesson.Outcome == LessonOutcome.Rejected)
        {
            // Nothing usable, not even speech: touch neither seam.
            return new PlaybackReport(PlaybackStatus.Rejected, lesson.Outcome, none, SpokeFallback: false);
        }

        if (Interlocked.CompareExchange(ref _state, (int)PlayerState.Playing, (int)PlayerState.Idle) != (int)PlayerState.Idle)
        {
            return new PlaybackReport(PlaybackStatus.Refused, lesson.Outcome, none, SpokeFallback: false);
        }

        using var playCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        lock (_lock)
        {
            _playCts = playCts;
            _stopRequested = false;
            _pauseRequested = false;
        }

        var records = new List<BeatRecord>();
        try
        {
            if (lesson.Outcome == LessonOutcome.TextFallback || lesson.Plan is null)
            {
                // Too broken to draw: say so once on the canvas, then teach by voice alone (M7 S7.1).
                await _canvas.ShowNoticeAsync(FallbackNotice, playCts.Token);
                await _speaker.SpeakAsync(lesson.FallbackText, playCts.Token);
                return new PlaybackReport(PlaybackStatus.Completed, lesson.Outcome, none, SpokeFallback: true);
            }

            var plan = lesson.Plan;
            await _canvas.ClearAsync(playCts.Token);
            for (var index = 0; index < plan.Beats.Count; index++)
            {
                await WaitWhilePausedAsync(playCts.Token);
                records.Add(await PlayBeatAsync(plan.Beats[index], index, playCts.Token));
            }

            return new PlaybackReport(PlaybackStatus.Completed, lesson.Outcome, records, SpokeFallback: false);
        }
        catch (OperationCanceledException) when (StoppedByStudent(cancellationToken))
        {
            return new PlaybackReport(PlaybackStatus.Stopped, lesson.Outcome, records, lesson.Plan is null);
        }
        finally
        {
            lock (_lock)
            {
                _playCts = null;
                _beatCts = null;
                _pauseRequested = false;
                _resume?.TrySetResult();
                _resume = null;
            }

            Volatile.Write(ref _state, (int)PlayerState.Idle);
        }
    }

    /// <summary>Takes effect at the next beat boundary — the current sentence is never cut off.</summary>
    public void Pause()
    {
        lock (_lock)
        {
            if (_playCts is not null)
            {
                _pauseRequested = true;
            }
        }
    }

    public void Resume()
    {
        lock (_lock)
        {
            _pauseRequested = false;
            _resume?.TrySetResult();
        }
    }

    /// <summary>Cuts the current beat's drawing and speech short; the lesson carries on with the next beat.</summary>
    public void SkipBeat()
    {
        lock (_lock)
        {
            _beatCts?.Cancel();
        }
    }

    /// <summary>Ends the lesson (also from a pause). <see cref="PlayAsync"/> returns <see cref="PlaybackStatus.Stopped"/>.</summary>
    public void Stop()
    {
        lock (_lock)
        {
            if (_playCts is not null)
            {
                _stopRequested = true;
                _playCts.Cancel();
            }
        }
    }

    private async Task<BeatRecord> PlayBeatAsync(LessonBeat beat, int index, CancellationToken playToken)
    {
        using var beatCts = CancellationTokenSource.CreateLinkedTokenSource(playToken);
        lock (_lock)
        {
            _beatCts = beatCts;
        }

        var started = _time.GetUtcNow();
        var skipped = false;
        try
        {
            // Draw first so the picture lands as the sentence begins; a beat with no ops (degraded)
            // is spoken only — the canvas never sees an empty beat.
            var draw = beat.Ops.Count > 0 ? _canvas.ApplyBeatAsync(beat, beatCts.Token) : Task.CompletedTask;
            var say = _speaker.SpeakAsync(beat.Say, beatCts.Token);
            await Task.WhenAll(draw, say);
        }
        catch (OperationCanceledException) when (beatCts.IsCancellationRequested && !playToken.IsCancellationRequested)
        {
            skipped = true;
        }
        finally
        {
            lock (_lock)
            {
                _beatCts = null;
            }
        }

        return new BeatRecord(index, beat.Id, beat.Degraded, skipped, started, _time.GetUtcNow());
    }

    private async Task WaitWhilePausedAsync(CancellationToken playToken)
    {
        Task wait;
        lock (_lock)
        {
            if (!_pauseRequested)
            {
                return;
            }

            _resume = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            wait = _resume.Task;
        }

        Volatile.Write(ref _state, (int)PlayerState.Paused);
        await wait.WaitAsync(playToken);
        Volatile.Write(ref _state, (int)PlayerState.Playing);
    }

    // A cancellation is the student's Stop only when Stop asked for it and the caller's own token did
    // not fire — a shell cancel must keep propagating as OperationCanceledException (exit 13).
    private bool StoppedByStudent(CancellationToken callerToken)
    {
        lock (_lock)
        {
            return _stopRequested && !callerToken.IsCancellationRequested;
        }
    }
}
