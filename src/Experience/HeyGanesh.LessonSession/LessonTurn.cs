using HeyGanesh.LessonPlanner;
using HeyGanesh.Providers.Core;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.LessonSession;

/// <summary>How one lesson turn ended, in the terms the shell maps to its exit-code alphabet.</summary>
public enum LessonTurnOutcome
{
    /// <summary>The plan was drawn and spoken beat by beat (some beats may have been degraded).</summary>
    Played = 0,

    /// <summary>Too broken to draw: the fallback notice was shown and the lesson spoken as text.</summary>
    PlayedFallback = 1,

    /// <summary>The student stopped it part-way.</summary>
    Stopped = 2,

    /// <summary>Nothing usable came back, even after the repair turn; no card was opened.</summary>
    Rejected = 3,

    /// <summary>The model provider refused the key.</summary>
    AuthError = 4,

    /// <summary>Any other provider fault (rate limit, timeout, transport, server error).</summary>
    ProviderError = 5,

    /// <summary>The caller's token was cancelled while planning or playing.</summary>
    Cancelled = 6,
}

/// <summary>
/// What one turn did. <see cref="Plan"/> is null when planning itself failed; <see cref="Playback"/>
/// is null when nothing was played. <see cref="Error"/> is <see cref="ProviderException.ToString"/>,
/// the provider layer's own secret-free form, never the raw message.
/// </summary>
public sealed record LessonTurnReport(LessonTurnOutcome Outcome, PlanResult? Plan, PlaybackReport? Playback, string? Error);

/// <summary>A canvas for one turn, plus whatever must be disposed when the turn ends.</summary>
public sealed record LessonCanvasLease(ILessonCanvas Canvas, IAsyncDisposable Owner);

/// <summary>
/// Opens the cards for one turn. Called only once a plan exists that has something to show, so a
/// rejected plan or a provider fault costs no window and no WebView2 start-up.
/// </summary>
public interface ILessonCanvasHost
{
    Task<LessonCanvasLease> OpenAsync(CancellationToken cancellationToken);
}

/// <summary>
/// One lesson turn: plan → open cards → play → close. The App Shell calls it after a produced verdict
/// with <c>--lesson</c>; tools/LessonTurnSmoke calls it with a fake model.
/// </summary>
/// <remarks>
/// Documented faults become outcomes so the shell never needs to know a provider type: provider
/// faults while planning and cancellation anywhere. Everything else propagates on purpose — a speaker
/// fault (the shell maps Voice's exceptions itself), a card that never acknowledged (TimeoutException)
/// or a missing WebView2 runtime are not verdicts and must not be dressed up as one. The cards are
/// closed on every path once they were opened.
/// </remarks>
public static class LessonTurn
{
    public static async Task<LessonTurnReport> RunAsync(
        VisualPlanner planner,
        ILessonCanvasHost canvases,
        ILessonSpeaker speaker,
        PlanRequest request,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(canvases);
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(request);

        PlanResult plan;
        try
        {
            plan = await planner.PlanAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new LessonTurnReport(LessonTurnOutcome.Cancelled, null, null, null);
        }
        catch (ProviderException ex)
        {
            var outcome = ex.Kind == ProviderErrorKind.AuthFailed ? LessonTurnOutcome.AuthError : LessonTurnOutcome.ProviderError;
            return new LessonTurnReport(outcome, null, null, ex.ToString());
        }

        if (plan.Lesson.Outcome == LessonOutcome.Rejected)
        {
            // Nothing to draw and nothing to say: no card is opened for a lesson that cannot exist.
            return new LessonTurnReport(LessonTurnOutcome.Rejected, plan, null, null);
        }

        var lease = await canvases.OpenAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var playback = await new LessonPlayer(lease.Canvas, speaker).PlayAsync(plan.Lesson, cancellationToken).ConfigureAwait(false);
            var outcome = playback.Status switch
            {
                PlaybackStatus.Completed when playback.SpokeFallback => LessonTurnOutcome.PlayedFallback,
                PlaybackStatus.Completed => LessonTurnOutcome.Played,
                PlaybackStatus.Stopped => LessonTurnOutcome.Stopped,

                // A fresh player cannot refuse, and Rejected was returned above: reaching here is a defect.
                _ => throw new InvalidOperationException($"unexpected playback status {playback.Status} on a fresh player"),
            };
            return new LessonTurnReport(outcome, plan, playback, null);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return new LessonTurnReport(LessonTurnOutcome.Cancelled, plan, null, null);
        }
        finally
        {
            await lease.Owner.DisposeAsync().ConfigureAwait(false);
        }
    }
}
