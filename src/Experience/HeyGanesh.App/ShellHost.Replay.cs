using HeyGanesh.Abstractions;
using HeyGanesh.DrawStage;
using HeyGanesh.Notes;
using HeyGanesh.Voice;

namespace HeyGanesh.App;

// Replay (DEC-060, reworked for the drawing stage by DEC-076): a stored drawing (TurnNote.LessonPlan in StoredDrawing's
// form, the program exactly as drawn) is drawn again on a stage window over the cursor's monitor, at the canvas size it
// was written for. No model is called. The program runs in the stage's sandbox and its marks are checked as a live
// drawing's are (DEC-070). Speech goes through the running shell's voice when a Fish key is set; otherwise the page
// paces each line by its own estimate. A row from before the drawing stage holds VisualPlan JSON and is refused with
// a message. The window closes a few seconds after the drawing ends, or at once on its close button.

internal static partial class ShellHost
{
    /// <summary>The running shell's voice, kept for replays from the tray. Null before RunAsync composes it.</summary>
    private static VoiceService? ReplayVoice;

    /// <summary>Where a replay is drawn: the cursor's monitor, from the running shell's capture service. Null before RunAsync.</summary>
    private static Func<MonitorInfo?>? ReplayMonitor;

    /// <summary>How long a replayed drawing stays up after its last line, so it can be looked at.</summary>
    internal static readonly TimeSpan ReplayHold = TimeSpan.FromSeconds(4);

    internal const string OlderLessonRefusal = "this lesson was made by an older version of Hey Ganesh (lesson cards), so it cannot be replayed";

    /// <summary>The stored drawing, ready to draw, or why it cannot be. Pure: nothing is drawn or said.</summary>
    internal static (StoredReplay? Drawing, string? Refusal) PrepareReplay(string? stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
        {
            return (null, "this turn has no stored lesson to replay");
        }

        if (!stored.StartsWith(StoredDrawing.Marker, StringComparison.Ordinal))
        {
            return (null, OlderLessonRefusal);
        }

        return StoredDrawing.TryRead(stored, out var width, out var height, out var program)
            ? (new StoredReplay(width, height, program), null)
            : (null, "the stored drawing is damaged (no canvas size or no program), so it cannot be replayed");
    }

    /// <summary>Replays on a real stage window with the shell's voice (or the page's own pacing). What the tray calls.</summary>
    internal static Task<ReplayResult> ReplayAsync(string? stored, CancellationToken ct) =>
        ReplayMonitor?.Invoke() is { } monitor
            ? ReplayAsync(stored, OpenStageAsync, monitor, ReplaySpeaker(), DrawingFinishWait, ReplayHold, ct)
            : Task.FromResult(new ReplayResult(false, "the app is not running, so there is no screen to draw on", 0));

    /// <summary>
    /// Draws a stored drawing on a stage from <paramref name="open"/>; nothing is opened for one that cannot be drawn.
    /// <paramref name="speak"/> null is silent (pace estimate). The window is closed when the replay ends, however it
    /// ends. A page that never finishes within <paramref name="finishWait"/> ends the replay as not played.
    /// </summary>
    internal static async Task<ReplayResult> ReplayAsync(
        string? stored,
        Func<MonitorInfo, Task<IDrawStageSurface>> open,
        MonitorInfo monitor,
        Func<string, CancellationToken, Task>? speak,
        TimeSpan finishWait,
        TimeSpan hold,
        CancellationToken ct)
    {
        var (drawing, refusal) = PrepareReplay(stored);
        if (drawing is null)
        {
            return new ReplayResult(false, refusal!, 0);
        }

        var surface = await open(monitor).ConfigureAwait(false);
        try
        {
            var controller = new DrawStageController(surface);
            var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var closed = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var lines = 0;
            controller.Finished += () => finished.TrySetResult();
            controller.Closed += () =>
            {
                closed.TrySetResult();
                finished.TrySetResult();
            };
            controller.LineAdded += (_, _) => Interlocked.Increment(ref lines);

            // The voice listens before the stage starts, so no line is missed (DrawStageVoice's contract).
            var voice = speak is null ? null : DrawStageVoice.RunAsync(controller, speak, ct);
            await controller.BeginAsync(drawing.Width, drawing.Height, speak is null ? DrawStagePace.Estimate : DrawStagePace.Speech).ConfigureAwait(false);
            await controller.FeedAsync(drawing.Program, done: true).ConfigureAwait(false);
            try
            {
                await Task.WhenAll(voice ?? Task.CompletedTask, finished.Task).WaitAsync(finishWait, ct).ConfigureAwait(false);
            }
            catch (TimeoutException)
            {
                return new ReplayResult(false, "the drawing did not finish, so the replay was stopped", Volatile.Read(ref lines));
            }

            if (closed.Task.IsCompleted)
            {
                return new ReplayResult(true, $"replayed {Volatile.Read(ref lines)} line(s); closed by you", Volatile.Read(ref lines));
            }

            await Task.WhenAny(closed.Task, Task.Delay(hold, ct)).ConfigureAwait(false);
            ct.ThrowIfCancellationRequested();
            return new ReplayResult(true, $"replayed {Volatile.Read(ref lines)} line(s), drawn and spoken again with no model call", Volatile.Read(ref lines));
        }
        finally
        {
            (surface as IDisposable)?.Dispose();
        }
    }

    /// <summary>
    /// The shell's voice when there is a Fish key; null (the page's own pacing) otherwise. A line the voice cannot say
    /// (a live turn is speaking, or the voice failed) is paced instead, so the replay goes on.
    /// </summary>
    private static Func<string, CancellationToken, Task>? ReplaySpeaker()
    {
        if (ReplayVoice is not { } voice || !HasKey("fish"))
        {
            return null;
        }

        return async (text, token) =>
        {
            try
            {
                await voice.SpeakAsync(new SpeakRequest { Text = text }, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                await Task.Delay(Math.Clamp(text.Length * 60, 600, 8000), token).ConfigureAwait(false);
            }
        };
    }
}

/// <summary>A stored drawing read back: the canvas it was written for and the program.</summary>
internal sealed record StoredReplay(int Width, int Height, string Program);

/// <summary>How a replay went, in a sentence the tray shows. <see cref="Lines"/> is how many lines the page drew.</summary>
internal sealed record ReplayResult(bool Played, string Said, int Lines);
