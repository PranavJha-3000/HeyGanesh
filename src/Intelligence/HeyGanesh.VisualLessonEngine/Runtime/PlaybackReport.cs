namespace HeyGanesh.VisualLessonEngine.Runtime;

/// <summary>How a play ended.</summary>
public enum PlaybackStatus
{
    /// <summary>Every beat was played (skipped beats still count as played).</summary>
    Completed = 0,

    /// <summary>The student stopped it.</summary>
    Stopped = 1,

    /// <summary>Another lesson was already playing on this player; nothing happened (never queued).</summary>
    Refused = 2,

    /// <summary>The validator rejected the plan; there was nothing to draw or say.</summary>
    Rejected = 3,
}

/// <summary>What the player is doing right now.</summary>
public enum PlayerState
{
    Idle = 0,
    Playing = 1,

    /// <summary>Waiting at a beat boundary for Resume or Stop.</summary>
    Paused = 2,
}

/// <summary>One beat as it was actually played.</summary>
public sealed record BeatRecord(int Index, string BeatId, bool Degraded, bool Skipped, DateTimeOffset StartedAt, DateTimeOffset EndedAt);

/// <summary>
/// The replay log of one play: which beats ran, when, and how. Plain data — kept by the session
/// (DEC-022) and, later, by Notes for lesson history.
/// </summary>
public sealed record PlaybackReport(PlaybackStatus Status, LessonOutcome Outcome, IReadOnlyList<BeatRecord> Beats, bool SpokeFallback);
