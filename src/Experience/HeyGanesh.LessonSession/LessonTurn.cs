using System.Threading.Channels;
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
/// <remarks>
/// <see cref="FirstBeatShown"/> is from the start of the turn to the first beat starting on the cards
/// (DEC-042): the time a student waits to see the lesson begin. <see cref="Error"/> also carries the
/// planner's StreamNote when a streamed lesson stopped early.
/// </remarks>
public sealed record LessonTurnReport(LessonTurnOutcome Outcome, PlanResult? Plan, PlaybackReport? Playback, string? Error, TimeSpan? FirstBeatShown = null);

/// <summary>Why <see cref="LessonTurn.RunWithFollowUpsAsync"/> stopped asking for follow-ups.</summary>
public enum FollowUpEnd
{
    /// <summary>The last turn did not play to the end (rejected, stopped, fallback, provider fault, cancelled).</summary>
    TurnEnded = 0,

    /// <summary>The lesson drew no 3D scene, so there was nothing to click.</summary>
    NoScene = 1,

    /// <summary>The canvas cannot take clicks (it is not an <see cref="IPartPicker"/>).</summary>
    NotPickable = 2,

    /// <summary>No click: the student closed the lesson, the idle timeout passed, or the scene had nothing drawn.</summary>
    NoPick = 3,

    /// <summary><see cref="FollowUpOptions.MaxFollowUps"/> follow-ups were played.</summary>
    MaxFollowUps = 4,

    /// <summary>A pick named a part the Part Library does not have. A canvas defect; never asked about.</summary>
    UnknownPart = 5,

    /// <summary>The caller's token was cancelled while waiting for a click.</summary>
    Cancelled = 6,
}

/// <summary>
/// Every turn of one lesson and its follow-ups, in order (the first is the student's own question),
/// the parts that were clicked between them, and why it stopped.
/// </summary>
public sealed record LessonConversationReport(IReadOnlyList<LessonTurnReport> Turns, IReadOnlyList<PartPick> Picks, FollowUpEnd End);

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
/// with <c>--lesson</c>; tools/LessonTurnSmoke calls it with a fake model. <see cref="RunWithFollowUpsAsync"/>
/// adds click-a-part follow-ups on the same cards (DEC-031); the shell's --lesson leg calls it (DEC-033).
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

        var (report, lease) = await PlayTurnAsync(planner, canvases, null, speaker, request, cancellationToken).ConfigureAwait(false);
        if (lease is not null)
        {
            await lease.Owner.DisposeAsync().ConfigureAwait(false);
        }

        return report;
    }

    /// <summary>
    /// One lesson, then click-to-ask follow-ups (DEC-031): once a lesson that drew a 3D scene has played
    /// to the end, the canvas (if it is an <see cref="IPartPicker"/>) waits for the student to click a
    /// part; the click becomes a question written here from the Part Library (<see cref="PartQuestion"/>),
    /// planned with the lesson just shown as <see cref="PlanRequest.PreviousPlan"/>, and played on the
    /// SAME canvas. Stops at the first turn that does not play to the end, on no click, or after
    /// <see cref="FollowUpOptions.MaxFollowUps"/>. The canvas is opened at most once and closed on every path.
    /// </summary>
    /// <remarks>
    /// Faults behave as in <see cref="RunAsync"/>. A follow-up has no screen grounding: it is about a
    /// part on the lesson's own scene, so it may not mark the student's screen.
    /// </remarks>
    public static async Task<LessonConversationReport> RunWithFollowUpsAsync(
        VisualPlanner planner,
        ILessonCanvasHost canvases,
        ILessonSpeaker speaker,
        PlanRequest request,
        FollowUpOptions? options = null,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(planner);
        ArgumentNullException.ThrowIfNull(canvases);
        ArgumentNullException.ThrowIfNull(speaker);
        ArgumentNullException.ThrowIfNull(request);
        options ??= FollowUpOptions.Default;

        var turns = new List<LessonTurnReport>();
        var picks = new List<PartPick>();
        LessonCanvasLease? lease = null;
        try
        {
            var current = request;
            while (true)
            {
                LessonTurnReport report;
                (report, lease) = await PlayTurnAsync(planner, canvases, lease, speaker, current, cancellationToken).ConfigureAwait(false);
                turns.Add(report);
                if (report.Outcome != LessonTurnOutcome.Played)
                {
                    return new LessonConversationReport(turns, picks, FollowUpEnd.TurnEnded);
                }

                var shown = report.Plan!.Lesson.Plan!;
                if (!shown.Surfaces.Any(s => s.Kind == SurfaceKind.Scene3d))
                {
                    return new LessonConversationReport(turns, picks, FollowUpEnd.NoScene);
                }

                if (lease!.Canvas is not IPartPicker picker)
                {
                    return new LessonConversationReport(turns, picks, FollowUpEnd.NotPickable);
                }

                if (picks.Count >= options.MaxFollowUps)
                {
                    return new LessonConversationReport(turns, picks, FollowUpEnd.MaxFollowUps);
                }

                PartPick? pick;
                try
                {
                    pick = await picker.PickAsync(options.IdleTimeout, cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    return new LessonConversationReport(turns, picks, FollowUpEnd.Cancelled);
                }

                if (pick is null)
                {
                    return new LessonConversationReport(turns, picks, FollowUpEnd.NoPick);
                }

                var question = PartQuestion.For(pick, shown.Schematics);
                if (question is null)
                {
                    return new LessonConversationReport(turns, picks, FollowUpEnd.UnknownPart);
                }

                picks.Add(pick);
                current = current with { Question = question, PreviousPlan = shown, Screen = null };
            }
        }
        finally
        {
            if (lease is not null)
            {
                await lease.Owner.DisposeAsync().ConfigureAwait(false);
            }
        }
    }

    /// <summary>
    /// Plan, then play on <paramref name="lease"/> -- or on a canvas opened here when it is null and the
    /// plan has something to show. Returns the lease in use (null if none was ever opened) for the
    /// caller to close; a lease opened here is closed here only if an exception escapes.
    /// </summary>
    private static async Task<(LessonTurnReport Report, LessonCanvasLease? Lease)> PlayTurnAsync(
        VisualPlanner planner,
        ILessonCanvasHost canvases,
        LessonCanvasLease? lease,
        ILessonSpeaker speaker,
        PlanRequest request,
        CancellationToken cancellationToken)
    {
        // DEC-042: plan through the streaming path. With a client that cannot stream it is exactly
        // PlanAsync and no beat is released early, so the whole-plan path below runs as before.
        var startedAt = DateTimeOffset.UtcNow;
        using var planCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var beats = Channel.CreateUnbounded<LessonBeat>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = true });
        var firstReleased = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        IReadOnlyList<HeyGanesh.VisualLessonEngine.LessonSurface>? surfaces = null;
        IReadOnlyList<SchematicModel>? schematics = null;
        var released = 0;
        var planning = planner.PlanStreamingAsync(request, beat =>
        {
            // Runs on the planner's reading loop: hand the beat over and return at once.
            surfaces ??= beat.Prefix.Plan!.Surfaces;
            schematics ??= beat.Prefix.Plan!.Schematics;
            released++;
            beats.Writer.TryWrite(beat.Beat);
            firstReleased.TrySetResult();
        }, planCts.Token);

        await Task.WhenAny(planning, firstReleased.Task).ConfigureAwait(false);
        if (firstReleased.Task.IsCompleted)
        {
            return await PlayStreamedTurnAsync(planning, beats, surfaces!, schematics!, () => released, planCts, canvases, lease, speaker, startedAt, cancellationToken).ConfigureAwait(false);
        }

        PlanResult plan;
        try
        {
            plan = await planning.ConfigureAwait(false);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (new LessonTurnReport(LessonTurnOutcome.Cancelled, null, null, null), lease);
        }
        catch (ProviderException ex)
        {
            var outcome = ex.Kind == ProviderErrorKind.AuthFailed ? LessonTurnOutcome.AuthError : LessonTurnOutcome.ProviderError;
            return (new LessonTurnReport(outcome, null, null, ex.ToString()), lease);
        }

        if (plan.Lesson.Outcome == LessonOutcome.Rejected)
        {
            // Nothing to draw and nothing to say: no card is opened for a lesson that cannot exist.
            return (new LessonTurnReport(LessonTurnOutcome.Rejected, plan, null, null), lease);
        }

        var openedHere = lease is null;
        lease ??= await canvases.OpenAsync(cancellationToken).ConfigureAwait(false);
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
            return (new LessonTurnReport(outcome, plan, playback, null, FirstShown(playback, startedAt)), lease);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            return (new LessonTurnReport(LessonTurnOutcome.Cancelled, plan, null, null), lease);
        }
        catch when (openedHere)
        {
            await lease.Owner.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    /// <summary>
    /// The streamed half of a turn (DEC-042): a beat was released, so the cards open now and play each
    /// beat as the planner releases it, while the rest of the lesson is still being written. When planning
    /// ends, any beats the finished plan holds beyond those released are queued too, and the queue closes.
    /// A planner StreamNote (a dropped stream, or a finished plan that disowns a shown beat) ends the turn
    /// Stopped after the beats already shown. A student Stop or a fault stops the planning as well.
    /// </summary>
    private static async Task<(LessonTurnReport Report, LessonCanvasLease? Lease)> PlayStreamedTurnAsync(
        Task<PlanResult> planning,
        Channel<LessonBeat> beats,
        IReadOnlyList<HeyGanesh.VisualLessonEngine.LessonSurface> surfaces,
        IReadOnlyList<SchematicModel> schematics,
        Func<int> released,
        CancellationTokenSource planCts,
        ILessonCanvasHost canvases,
        LessonCanvasLease? lease,
        ILessonSpeaker speaker,
        DateTimeOffset startedAt,
        CancellationToken cancellationToken)
    {
        var feeding = FinishFeedingAsync(planning, beats.Writer, released);
        var openedHere = lease is null;
        try
        {
            lease ??= await canvases.OpenAsync(cancellationToken).ConfigureAwait(false);
            var playback = await new LessonPlayer(lease.Canvas, speaker)
                .PlayStreamAsync(surfaces, beats.Reader.ReadAllAsync(cancellationToken), LessonOutcome.Valid, cancellationToken, schematics)
                .ConfigureAwait(false);

            if (playback.Status == PlaybackStatus.Stopped && !planning.IsCompleted)
            {
                // The student stopped the lesson: stop writing the rest of it too.
                planCts.Cancel();
            }

            PlanResult? plan = null;
            try
            {
                plan = await planning.ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
            {
                // Planning was cancelled by the Stop above; the beats shown are the lesson.
            }

            await feeding.ConfigureAwait(false);
            var outcome = playback.Status switch
            {
                PlaybackStatus.Stopped => LessonTurnOutcome.Stopped,
                PlaybackStatus.Completed when plan?.StreamNote is not null => LessonTurnOutcome.Stopped,
                PlaybackStatus.Completed => LessonTurnOutcome.Played,
                _ => throw new InvalidOperationException($"unexpected playback status {playback.Status} on a fresh player"),
            };
            var report = playback with { Outcome = plan?.Lesson.Outcome ?? playback.Outcome };
            return (new LessonTurnReport(outcome, plan, report, plan?.StreamNote, FirstShown(report, startedAt)), lease);
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            await QuietlyAsync(planning).ConfigureAwait(false);
            return (new LessonTurnReport(LessonTurnOutcome.Cancelled, null, null, null), lease);
        }
        catch
        {
            planCts.Cancel();
            await QuietlyAsync(planning).ConfigureAwait(false);
            if (openedHere && lease is not null)
            {
                await lease.Owner.DisposeAsync().ConfigureAwait(false);
            }

            throw;
        }
    }

    /// <summary>When planning ends, queue the finished plan's beats that were not released, then close the queue.</summary>
    private static async Task FinishFeedingAsync(Task<PlanResult> planning, ChannelWriter<LessonBeat> writer, Func<int> released)
    {
        try
        {
            var plan = await planning.ConfigureAwait(false);
            if (plan.StreamNote is null && plan.Lesson.Plan is { } whole)
            {
                for (var i = released(); i < whole.Beats.Count; i++)
                {
                    writer.TryWrite(whole.Beats[i]);
                }
            }
        }
        catch
        {
            // Planning's own outcome is read by the turn; the queue only needs to close.
        }
        finally
        {
            writer.TryComplete();
        }
    }

    private static async Task QuietlyAsync(Task task)
    {
        try
        {
            await task.ConfigureAwait(false);
        }
        catch
        {
            // Already being reported (cancellation or the fault being rethrown).
        }
    }

    private static TimeSpan? FirstShown(PlaybackReport playback, DateTimeOffset startedAt) =>
        playback.Beats.Count > 0 ? playback.Beats[0].StartedAt - startedAt : null;
}
