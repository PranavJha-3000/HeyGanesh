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
        if (lesson.Outcome == LessonOutcome.Rejected)
        {
            // Nothing usable, not even speech: touch neither seam.
            return new PlaybackReport(PlaybackStatus.Rejected, lesson.Outcome, Array.Empty<BeatRecord>(), SpokeFallback: false);
        }

        return await PlayCoreAsync(lesson.Outcome, lesson.Plan is null, async (token, records) =>
        {
            if (lesson.Outcome == LessonOutcome.TextFallback || lesson.Plan is null)
            {
                // Too broken to draw: say so once on the canvas, then teach by voice alone (M7 S7.1).
                token.ThrowIfCancellationRequested();
                await _canvas.ShowNoticeAsync(FallbackNotice, token);
                token.ThrowIfCancellationRequested();
                await _speaker.SpeakAsync(lesson.FallbackText, token);
                return true;
            }

            await PlayBeatsAsync(lesson.Plan.Surfaces, Each(lesson.Plan.Beats), records, token);
            return false;
        }, cancellationToken);
    }

    /// <summary>
    /// Plays beats as they arrive (DEC-042): the cards are reset for <paramref name="surfaces"/>, then each
    /// beat from <paramref name="beats"/> is drawn and spoken exactly as <see cref="PlayAsync"/> would, the
    /// next one waiting for the sequence. Every beat must already be validated (the streaming planner
    /// releases only validated beats). <paramref name="outcome"/> is reported as-is; the caller knows the
    /// lesson's final outcome only once the sequence has ended.
    /// </summary>
    public Task<PlaybackReport> PlayStreamAsync(
        IReadOnlyList<LessonSurface> surfaces,
        IAsyncEnumerable<LessonBeat> beats,
        LessonOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(surfaces);
        ArgumentNullException.ThrowIfNull(beats);
        return PlayCoreAsync(outcome, spokenOnly: false, async (token, records) =>
        {
            await PlayBeatsAsync(surfaces, beats, records, token);
            return false;
        }, cancellationToken);
    }

    /// <summary>
    /// The one-play-at-a-time frame both entry points share: refuse a second play, own the play token
    /// for the controls, map a student's Stop, always return to Idle. <paramref name="body"/> returns
    /// whether it spoke the fallback text instead of drawing.
    /// </summary>
    private async Task<PlaybackReport> PlayCoreAsync(
        LessonOutcome outcome,
        bool spokenOnly,
        Func<CancellationToken, List<BeatRecord>, Task<bool>> body,
        CancellationToken cancellationToken)
    {
        var none = Array.Empty<BeatRecord>();
        if (Interlocked.CompareExchange(ref _state, (int)PlayerState.Playing, (int)PlayerState.Idle) != (int)PlayerState.Idle)
        {
            return new PlaybackReport(PlaybackStatus.Refused, outcome, none, SpokeFallback: false);
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
            // The token is checked before every seam call, not left to the seams: a canvas or speaker
            // that ignores its token must still not be driven after Stop or a caller cancel.
            var token = playCts.Token;
            var spokeFallback = await body(token, records);
            return spokeFallback
                ? new PlaybackReport(PlaybackStatus.Completed, outcome, none, SpokeFallback: true)
                : new PlaybackReport(PlaybackStatus.Completed, outcome, records, SpokeFallback: false);
        }
        catch (OperationCanceledException) when (StoppedByStudent(cancellationToken))
        {
            return new PlaybackReport(PlaybackStatus.Stopped, outcome, records, spokenOnly);
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
        CancellationTokenSource? beat;
        lock (_lock)
        {
            beat = _beatCts;
        }

        CancelWithoutRunningHere(beat);
    }

    /// <summary>Ends the lesson (also from a pause). <see cref="PlayAsync"/> returns <see cref="PlaybackStatus.Stopped"/>.</summary>
    public void Stop()
    {
        CancellationTokenSource? play;
        lock (_lock)
        {
            play = _playCts;
            if (play is not null)
            {
                _stopRequested = true;
            }
        }

        CancelWithoutRunningHere(play);
    }

    /// <summary>
    /// Controls arrive on the shell's threads — the hotkey worker has a ~300 ms budget. A plain
    /// Cancel() runs token callbacks, and through them the rest of the lesson (the next draw and
    /// sentence), on the calling thread. CancelAsync hands the callbacks to the thread pool, and
    /// doing it outside the lock means no seam ever runs while a control holds the lock.
    /// </summary>
    private static void CancelWithoutRunningHere(CancellationTokenSource? cts)
    {
        if (cts is null)
        {
            return;
        }

        try
        {
            _ = cts.CancelAsync();
        }
        catch (ObjectDisposedException)
        {
            // The beat or play finished between taking the reference and cancelling: nothing to stop.
        }
    }

    /// <summary>Reset the cards, then draw and speak each beat in turn — from a finished plan or a live stream.</summary>
    private async Task PlayBeatsAsync(IReadOnlyList<LessonSurface> surfaces, IAsyncEnumerable<LessonBeat> beats, List<BeatRecord> records, CancellationToken token)
    {
        token.ThrowIfCancellationRequested();
        await _canvas.ResetAsync(surfaces, token);
        var index = 0;
        await foreach (var beat in beats.WithCancellation(token))
        {
            await WaitWhilePausedAsync(token);
            token.ThrowIfCancellationRequested();
            records.Add(await PlayBeatAsync(beat, index++, token));
        }
    }

    private static async IAsyncEnumerable<LessonBeat> Each(IReadOnlyList<LessonBeat> beats)
    {
        foreach (var beat in beats)
        {
            yield return beat;
        }

        await Task.CompletedTask;
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
