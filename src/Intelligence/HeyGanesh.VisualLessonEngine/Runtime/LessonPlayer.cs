namespace HeyGanesh.VisualLessonEngine.Runtime;

/// <summary>
/// Plays a validated lesson: for each beat it starts the drawing, then the sentence, and moves on
/// only when both are done — so what the student hears and what they see stay together, and the
/// length of the speech sets the pace. Holds no lesson state between plays, so playing the same
/// validated result again is a replay.
/// </summary>
public sealed class LessonPlayer
{
    public const string FallbackNotice = "I couldn't draw this one, so I'll just explain it.";

    private readonly ILessonCanvas _canvas;
    private readonly ILessonSpeaker _speaker;
    private readonly TimeProvider _time;
    private int _state = (int)PlayerState.Idle;

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

        _state = (int)PlayerState.Playing;
        try
        {
            if (lesson.Outcome == LessonOutcome.TextFallback || lesson.Plan is null)
            {
                // Too broken to draw: say so once on the canvas, then teach by voice alone (M7 S7.1).
                await _canvas.ShowNoticeAsync(FallbackNotice, cancellationToken);
                await _speaker.SpeakAsync(lesson.FallbackText, cancellationToken);
                return new PlaybackReport(PlaybackStatus.Completed, lesson.Outcome, none, SpokeFallback: true);
            }

            var plan = lesson.Plan;
            var records = new List<BeatRecord>();
            await _canvas.ClearAsync(cancellationToken);
            for (var index = 0; index < plan.Beats.Count; index++)
            {
                var beat = plan.Beats[index];
                var started = _time.GetUtcNow();
                var draw = beat.Ops.Count > 0 ? _canvas.ApplyBeatAsync(beat, cancellationToken) : Task.CompletedTask;
                var say = _speaker.SpeakAsync(beat.Say, cancellationToken);
                await Task.WhenAll(draw, say);
                records.Add(new BeatRecord(index, beat.Id, beat.Degraded, false, started, _time.GetUtcNow()));
            }

            return new PlaybackReport(PlaybackStatus.Completed, lesson.Outcome, records, SpokeFallback: false);
        }
        finally
        {
            _state = (int)PlayerState.Idle;
        }
    }
}
