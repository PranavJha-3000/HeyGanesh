using System.Text;
using System.Threading.Channels;
using HeyGanesh.Abstractions;
using HeyGanesh.DrawStage;
using HeyGanesh.LessonPlanner;

namespace HeyGanesh.App;

// The drawing leg's moving parts (FEATURE-011 step 7, DEC-074): the stage that outlives a turn (DrawingStage) and one
// drawing turn on it (ShellHost.DrawOnStageAsync). Both take the stage's surface through IDrawStageSurface, so
// tools/TraySmoke --assert-drawing-leg (SH-29) drives them offline with a fake model and a fake page; the shell opens
// a real DrawStageWindow (ShellHost.Lesson.cs). This file reads no key and prints nothing.

/// <summary>
/// How one drawing turn went. <see cref="Kept"/> is null when nothing was drawn, true when a follow-up kept the drawing
/// and drew after it, false when the stage was drawn afresh. <see cref="Voice"/> is null when silent (pace estimate) or
/// nothing was drawn. <see cref="SpeechError"/> is the speaker's fault, if the voice was cut short by one.
/// <see cref="Drawn"/> is the whole program now drawn (an earlier one first, when keep() drew after it), for the note.
/// </summary>
internal sealed record DrawingTurn(
    DrawingResult Result,
    bool FollowUp,
    bool? Kept,
    DrawStageVoiceResult? Voice,
    Exception? SpeechError,
    bool Closed,
    string? FirstLine,
    string? Drawn = null);

/// <summary>
/// The drawing stage between turns (DEC-074): one window over one monitor, opened on the first drawing and kept, plus
/// what is drawn on it (the questions it answered and its program), so the next question can be asked over it as a
/// follow-up. The close button forgets the drawing; a drawing on another monitor replaces the window. Disposed by the
/// shell at shutdown.
/// </summary>
internal sealed class DrawingStage : IDisposable
{
    private readonly Func<MonitorInfo, Task<IDrawStageSurface>> _open;
    private readonly object _gate = new();
    private readonly List<string> _questions = new();
    private IDrawStageSurface? _surface;
    private DrawStageController? _controller;
    private MonitorInfo? _monitor;
    private int _width;
    private int _height;
    private string? _program;

    public DrawingStage(Func<MonitorInfo, Task<IDrawStageSurface>> open) => _open = open;

    /// <summary>A part of a 3D figure was clicked (its name, untrusted page text). Raised on the stage's thread.</summary>
    public event Action<string>? PartClicked;

    /// <summary>A line the page or the controller logged. Raised on the stage's thread.</summary>
    public event Action<string>? Logged;

    /// <summary>The drawing on the stage over <paramref name="monitor"/>, for a follow-up; null when there is none.</summary>
    public DrawingFollowUp? FollowUpOn(MonitorInfo monitor)
    {
        lock (_gate)
        {
            return _program is not null && SameMonitor(monitor) ? new DrawingFollowUp(_questions.ToArray(), _program) : null;
        }
    }

    /// <summary>Where the drawing is (its monitor and canvas), for a click's follow-up; null when nothing is drawn.</summary>
    public (MonitorInfo Monitor, int Width, int Height)? Drawing
    {
        get
        {
            lock (_gate)
            {
                return _program is not null && _monitor is { } monitor ? (monitor, _width, _height) : null;
            }
        }
    }

    /// <summary>The controller of a stage over <paramref name="monitor"/>; opens one (and closes any other) when needed.</summary>
    internal async Task<DrawStageController> ControllerOnAsync(MonitorInfo monitor)
    {
        lock (_gate)
        {
            if (_controller is { } current && SameMonitor(monitor))
            {
                return current;
            }
        }

        CloseWindow(); // another monitor, or none yet: the old window and its drawing go
        var surface = await _open(monitor).ConfigureAwait(false);
        var controller = new DrawStageController(surface);
        controller.FigurePartClicked += part => PartClicked?.Invoke(part);
        controller.Closed += Forget;
        controller.Logged += line => Logged?.Invoke(line);
        lock (_gate)
        {
            _surface = surface;
            _controller = controller;
            _monitor = monitor;
        }

        return controller;
    }

    /// <summary>A program finished drawing: kept drawings add to the one on the stage, others replace it.</summary>
    public void Remember(string question, string program, bool kept, int width, int height)
    {
        lock (_gate)
        {
            if (!kept || _program is null)
            {
                _questions.Clear();
                _program = null;
            }

            _questions.Add(question);
            _program = _program is null ? program : _program + "\n" + program;
            _width = width;
            _height = height;
        }
    }

    /// <summary>Nothing is drawn any more (closed or cleared): the next question is not a follow-up.</summary>
    public void Forget()
    {
        lock (_gate)
        {
            _questions.Clear();
            _program = null;
        }
    }

    /// <summary>Forgets the drawing and wipes the stage (best effort: a stage that is gone has nothing to wipe).</summary>
    public async Task ClearAsync(string reason)
    {
        Forget();
        DrawStageController? controller;
        lock (_gate)
        {
            controller = _controller;
        }

        if (controller is null)
        {
            return;
        }

        try
        {
            await controller.ClearAsync(reason).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            Logged?.Invoke($"clear: the stage was already gone ({ex.GetType().Name})");
        }
    }

    public void Log(string line) => Logged?.Invoke(line);

    public void Dispose() => CloseWindow();

    private void CloseWindow()
    {
        IDrawStageSurface? surface;
        lock (_gate)
        {
            surface = _surface;
            _surface = null;
            _controller = null;
            _monitor = null;
            _questions.Clear();
            _program = null;
        }

        (surface as IDisposable)?.Dispose();
    }

    private bool SameMonitor(MonitorInfo monitor) =>
        _monitor is { } current && current.Bounds == monitor.Bounds && current.DeviceName == monitor.DeviceName;
}

internal static partial class ShellHost
{
    /// <summary>The longest a click's follow-up question may name a part (the page's text is untrusted).</summary>
    internal const int MaxPartChars = 60;

    /// <summary>
    /// One drawing turn on the stage (DEC-074). The planner is asked with the drawing on the stage over
    /// <paramref name="monitor"/> as its follow-up (if any); the program is fed to the stage as it streams; the turn
    /// ends when both the voice and the page are done (the page can say finished about 0.25 s before a slow voice
    /// ends, DEC-071). A follow-up is drawn after the drawing (keep()) or afresh once its first line tells which.
    /// NONE or no program draws nothing, and a follow-up's drawing is then cleared (the question moved on).
    /// <paramref name="speak"/> null is silent: the page paces by its own estimate. Provider faults, cancellation and a
    /// page that never finishes within <paramref name="finishWait"/> (TimeoutException) propagate, with the stage
    /// cleared.
    /// </summary>
    internal static async Task<DrawingTurn> DrawOnStageAsync(
        DrawingPlanner planner,
        DrawingStage stage,
        MonitorInfo monitor,
        DrawingRequest request,
        Func<string, CancellationToken, Task>? speak,
        TimeSpan finishWait,
        CancellationToken ct)
    {
        var followUp = stage.FollowUpOn(monitor);
        request = request with { FollowUp = followUp };

        // Opened (or reused) while the model writes, so the first statement is not held back by the window.
        var opening = stage.ControllerOnAsync(monitor);
        using var turn = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var finished = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var closed = false;
        string? firstLine = null;
        Exception? speechError = null;
        bool? kept = null;
        Task<DrawStageVoiceResult>? voice = null;
        DrawStageController? controller = null;

        // Controller events come on the stage thread: these only record.
        void OnFinished() => finished.TrySetResult();
        void OnClosed()
        {
            Volatile.Write(ref closed, true);
            finished.TrySetResult();
        }

        void OnLine(int k, string text) => Interlocked.CompareExchange(ref firstLine, text, null);

        async Task Speak(string text, CancellationToken token)
        {
            try
            {
                await speak!(text, token).ConfigureAwait(false);
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                speechError ??= ex; // DrawStageVoice draws the rest at once; the shell reports the fault
                throw;
            }
        }

        // onProgram runs on the planner's reading loop and must return at once: it only queues, and this one reader
        // posts to the stage, skipping to the newest text (each feed is the whole program so far).
        var programs = Channel.CreateUnbounded<(string Program, bool Done)>(new UnboundedChannelOptions { SingleReader = true });
        async Task FeedAsync()
        {
            controller = await opening.ConfigureAwait(false);
            controller.Finished += OnFinished;
            controller.Closed += OnClosed;
            controller.LineAdded += OnLine;
            var pace = speak is null ? DrawStagePace.Estimate : DrawStagePace.Speech;
            await foreach (var next in programs.Reader.ReadAllAsync(turn.Token).ConfigureAwait(false))
            {
                var (program, done) = next;
                while (programs.Reader.TryRead(out var newer))
                {
                    (program, done) = newer;
                }

                if (kept is null)
                {
                    kept = followUp is null ? false : DrawingPlanner.KeepsTheDrawing(program, done);
                    if (kept is null)
                    {
                        continue; // too little has come to tell keep() from a fresh drawing
                    }

                    // The voice listens before the stage starts, so no line is missed (DrawStageVoice's contract).
                    voice = speak is null ? null : DrawStageVoice.RunAsync(controller, Speak, turn.Token);
                    if (kept == true)
                    {
                        await controller.ContinueAsync(pace).ConfigureAwait(false);
                    }
                    else
                    {
                        await controller.BeginAsync(request.CanvasWidth, request.CanvasHeight, pace).ConfigureAwait(false);
                    }
                }

                await controller.FeedAsync(program, done).ConfigureAwait(false);
            }
        }

        var feeding = FeedAsync();
        try
        {
            var result = await planner.DrawAsync(request, (program, done) => programs.Writer.TryWrite((program, done)), turn.Token).ConfigureAwait(false);
            programs.Writer.TryComplete();
            if (result.Outcome != DrawingOutcome.Program)
            {
                // Nothing was fed. The window may still be opening, or have failed to: neither matters to a Tutor answer.
                try
                {
                    await feeding.ConfigureAwait(false);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    stage.Log($"the stage did not open ({ex.GetType().Name}); nothing was to be drawn");
                }

                if (followUp is not null)
                {
                    await stage.ClearAsync("the question is not about the drawing").ConfigureAwait(false);
                }

                return new DrawingTurn(result, followUp is not null, null, null, null, false, null);
            }

            await feeding.ConfigureAwait(false);
            await Task.WhenAll(voice ?? Task.CompletedTask, finished.Task).WaitAsync(finishWait, turn.Token).ConfigureAwait(false);
            var spoken = voice is null ? null : await voice.ConfigureAwait(false);
            var wasClosed = Volatile.Read(ref closed) || spoken?.Closed == true;
            if (!wasClosed)
            {
                stage.Remember(request.Question, result.Program, kept == true, request.CanvasWidth, request.CanvasHeight);
            }

            // keep() is a no-op in the worker, so the earlier program and this one, joined, replay as one drawing.
            var drawn = kept == true && followUp is not null ? followUp.Program + "\n" + result.Program : result.Program;
            return new DrawingTurn(result, followUp is not null, kept, spoken, speechError, wasClosed, Volatile.Read(ref firstLine), drawn);
        }
        catch
        {
            programs.Writer.TryComplete();
            turn.Cancel();
            try
            {
                await feeding.ConfigureAwait(false);
            }
            catch (Exception)
            {
                // the turn's own fault is the one rethrown below
            }

            await stage.ClearAsync("the drawing turn did not finish").ConfigureAwait(false);
            throw;
        }
        finally
        {
            if (controller is not null)
            {
                controller.Finished -= OnFinished;
                controller.Closed -= OnClosed;
                controller.LineAdded -= OnLine;
            }
        }
    }

    /// <summary>
    /// A click on a figure's part as a follow-up question (DEC-074). The part's name comes from the page, so it is
    /// untrusted: only letters, digits, spaces, hyphens and apostrophes are kept, at most <see cref="MaxPartChars"/>.
    /// Null when nothing is left.
    /// </summary>
    internal static string? PartQuestion(string part)
    {
        var clean = new StringBuilder();
        foreach (var c in part)
        {
            var keep = char.IsLetterOrDigit(c) || c is '-' or '\'' ? c : ' ';
            if (keep != ' ' || (clean.Length > 0 && clean[^1] != ' '))
            {
                clean.Append(keep);
            }
        }

        var name = clean.ToString().Trim();
        if (name.Length > MaxPartChars)
        {
            name = name[..MaxPartChars].TrimEnd();
        }

        return name.Length == 0 ? null : $"Tell me more about the {name}.";
    }

    /// <summary>A screen box in canvas units: the screenshot is the monitor scaled to the canvas, so the box scales too.</summary>
    internal static PixelRect? ToCanvas(PixelRect? box, PixelRect monitor, int canvasWidth, int canvasHeight)
    {
        if (box is not { } b || monitor.Width <= 0 || monitor.Height <= 0)
        {
            return null;
        }

        double sx = (double)canvasWidth / monitor.Width, sy = (double)canvasHeight / monitor.Height;
        return new PixelRect(
            (int)Math.Round((b.X - monitor.X) * sx),
            (int)Math.Round((b.Y - monitor.Y) * sy),
            (int)Math.Round(b.Width * sx),
            (int)Math.Round(b.Height * sy));
    }
}
