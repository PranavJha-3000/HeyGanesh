namespace HeyGanesh.DrawStage;

/// <summary>What the controller needs from the window: <see cref="DrawStageWindow"/>, or a fake in a test.</summary>
public interface IDrawStageSurface
{
    /// <summary>Every message the page sends, as JSON text (raised on the window's thread).</summary>
    event Action<string>? MessageReceived;

    Task PostAsync(string json);

    Task ShowAsync();

    Task HideAsync();
}

/// <summary>
/// The drawing stage's conductor (FEATURE-011 step 4, DEC-071), ported from openclicky's DrawStageController (MIT): it
/// decides what the page is told and when, and turns the page's messages into events. The window
/// (<see cref="DrawStageWindow"/>) is opened by the caller and has already heard the page's "ready", so nothing waits
/// for it here.
/// <para>
/// Trimmed from upstream: no photos, pieces, saving, recording or measuring (not offered, DEC-067 / DEC-070). The
/// page's close button is handled here: the drawing is cleared, the window hidden, and <see cref="Closed"/> raised so
/// the voice and the turn can stop. A "save" message cannot come (stage.css hides Picture and Video), and is logged
/// if it does.
/// </para>
/// <para>
/// Events are raised on the window's thread: handlers must be quick and must not wait on the stage.
/// </para>
/// </summary>
public sealed class DrawStageController
{
    private readonly IDrawStageSurface _surface;
    private volatile bool _showing;

    public DrawStageController(IDrawStageSurface surface)
    {
        _surface = surface;
        _surface.MessageReceived += Receive;
    }

    /// <summary>The program said line <c>k</c> (a say()) as soon as its statement ran: a voice can start on it.</summary>
    public event Action<int, string>? LineAdded;

    /// <summary>Every statement has run (or the program was stopped), so every line it says has been added: the count.</summary>
    public event Action<int>? ProgramRan;

    /// <summary>The page is ready for line <c>k</c> to be spoken (pace speech).</summary>
    public event Action<int, string>? LineDue;

    /// <summary>Everything the program drew is on screen and the program is done.</summary>
    public event Action? Finished;

    /// <summary>A part of a 3D figure was clicked: its name.</summary>
    public event Action<string>? FigurePartClicked;

    /// <summary>The person closed the drawing with its close button; the stage is already cleared and hidden.</summary>
    public event Action? Closed;

    /// <summary>A line the page logged; its errors come prefixed "error: ".</summary>
    public event Action<string>? Logged;

    /// <summary>A program is on the stage (from <see cref="BeginAsync"/> until it is cleared).</summary>
    public bool IsShowing => _showing;

    /// <summary>A new program on a canvas of that size; anything drawn before is cleared, and the window shows.</summary>
    public async Task BeginAsync(int canvasWidth, int canvasHeight, DrawStagePace pace)
    {
        _showing = true;
        await _surface.PostAsync(DrawStageMessages.BeginStream(canvasWidth, canvasHeight, pace)).ConfigureAwait(false);
        await _surface.ShowAsync().ConfigureAwait(false);
    }

    /// <summary>A follow-up's program, drawn after the one on the stage (which stays, with its names).</summary>
    public async Task ContinueAsync(DrawStagePace pace)
    {
        _showing = true;
        await _surface.PostAsync(DrawStageMessages.ContinueStream(pace)).ConfigureAwait(false);
        await _surface.ShowAsync().ConfigureAwait(false);
    }

    public Task FeedAsync(string programSoFar, bool done) => _surface.PostAsync(DrawStageMessages.Feed(programSoFar, done));

    public Task LineStartedAsync(int k) => _surface.PostAsync(DrawStageMessages.Line("lineStarted", k));

    public Task LineEndedAsync(int k) => _surface.PostAsync(DrawStageMessages.Line("lineEnded", k));

    /// <summary>Draws everything still waiting right away (the voice was cut short or skipped).</summary>
    public Task FinishNowAsync() => _surface.PostAsync(DrawStageMessages.Simple("finishNow"));

    /// <summary>Wipes the drawing and hides the window.</summary>
    public async Task ClearAsync(string reason)
    {
        Logged?.Invoke($"stage cleared ({reason})");
        _showing = false;
        await _surface.PostAsync(DrawStageMessages.Simple("clear")).ConfigureAwait(false);
        await _surface.HideAsync().ConfigureAwait(false);
    }

    private void Receive(string json)
    {
        if (DrawStageMessages.ParseEvent(json) is not { } message)
        {
            return;
        }

        switch (message.Type)
        {
            case "line" when message.K is { } k:
                LineAdded?.Invoke(k, message.Text ?? "");
                break;
            case "lineDue" when message.K is { } k:
                LineDue?.Invoke(k, message.Text ?? "");
                break;
            case "programRan":
                ProgramRan?.Invoke(message.K ?? 0);
                break;
            case "finished":
                Finished?.Invoke();
                break;
            case "figurePart" when message.Text is { Length: > 0 } part:
                FigurePartClicked?.Invoke(part);
                break;
            case "close":
                _ = CloseAsync();
                break;
            case "error":
                Logged?.Invoke("error: " + message.Text);
                break;
            case "log":
                Logged?.Invoke(message.Text ?? "");
                break;
            case "ready" or "figures":
                break; // the window handles these
            default:
                Logged?.Invoke($"ignored page message '{message.Type}'"); // save, photoWanted, pieceWanted: not offered
                break;
        }
    }

    private async Task CloseAsync()
    {
        try
        {
            await ClearAsync("close button").ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is ObjectDisposedException or InvalidOperationException)
        {
            Logged?.Invoke($"close: the stage was already gone ({ex.GetType().Name})");
        }

        Closed?.Invoke();
    }
}
