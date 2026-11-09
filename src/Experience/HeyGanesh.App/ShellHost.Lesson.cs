using HeyGanesh.Abstractions;
using HeyGanesh.DrawStage;
using HeyGanesh.LessonPlanner;
using HeyGanesh.Notes;
using HeyGanesh.Providers.Core;
using HeyGanesh.Providers.OpenAiCompat;
using HeyGanesh.TurnRouter;
using HeyGanesh.TutorEngine;
using HeyGanesh.Voice;

namespace HeyGanesh.App;

// The lesson leg (DEC-027; on the drawing stage since FEATURE-011 step 7, DEC-074): with --lesson, or when the route
// chooses a lesson, a produced verdict is taught as a drawing the model writes as a program, drawn on the stage over
// the real screen while each line is spoken. Same seat in the turn as the Tutor leg: it runs only on non-Failed
// verdicts, after the overlay has reported, and it re-decides nothing.
//
// The drawing stays up after the lesson. The next question asked over it (Ctrl+Alt, same monitor) is a follow-up, and
// so is a click on a part of a 3D figure; the model keeps the drawing (keep()) or draws afresh, or says NONE and the
// Tutor answers. The close button ends the drawing. Nothing waits for a click, so Ctrl+Alt is free once a lesson ends.
//
// The shell keeps its two jobs (DEC-015/016): it maps the verdict to plain data (the screenshot's size, the grounded
// box in canvas units, the screen text) and hands Voice's SpeakAsync in as the speech clock. The turn itself is
// DrawOnStageAsync (ShellHost.Drawing.cs), which TraySmoke --assert-drawing-leg drives with fakes; this assembly
// carries no fakes (DEC-012). The old VisualPlanner -> LessonTurn -> cards path was deleted in FEATURE-011 step 10 (DEC-077).

internal static partial class ShellHost
{
    /// <summary>The composed lesson seam. Present whenever a lesson can be chosen; <see cref="Planner"/> is null without a key.</summary>
    private sealed record LessonLeg(DrawingPlanner? Planner, DrawingStage Stage);

    private const string LessonKeyEnvVar = "DEEPSEEK_API_KEY";

    /// <summary>
    /// How long a drawing may take to finish once the model's program is complete (voice and page both done). Far
    /// above a long spoken lesson; it only keeps a stage that never reports from holding the turn forever.
    /// </summary>
    private static readonly TimeSpan DrawingFinishWait = TimeSpan.FromMinutes(5);

    /// <summary>The longest first spoken line kept as the lesson's title in the notes.</summary>
    private const int MaxTitleChars = 120;

    /// <summary>
    /// DEC-027: the lesson model is built from the key ring, not held behind a code flip like the Tutor's (DEC-017).
    /// With no key the leg reports an honest skip; with one, the first lesson works with no code change. DeepSeek
    /// through the OpenAI-compatible adapter (deepseek-flash, streamed, DEC-042), the provider the repo already commits
    /// to. It has no Vision, so DrawingPlanner sends no image and says so (DEC-072).
    /// </summary>
    /// <remarks>
    /// Ownership: the shell owns the client (one per process) and RunAsync's using disposes it. The key is read only by
    /// KeyRingSecretStore, per request, and never printed: the header reports presence only.
    /// </remarks>
    private static ILlmClient? CreateLessonClient()
    {
        if (!HasKey("deepseek"))
        {
            return null;
        }

        return new OpenAiCompatLlmClient(new OpenAiCompatOptions
        {
            ProviderId = "deepseek",
            BaseUrl = "https://api.deepseek.com",
            ModelId = "deepseek-flash",
            // Streaming (DEC-042): the drawing starts while the rest of the program is still being written.
            Capabilities = ProviderCapability.Text | ProviderCapability.Streaming,
            SecretStore = new KeyRingSecretStore(Keys, "deepseek"),
            CredentialProviderId = "deepseek",
        });
    }

    /// <summary>The real stage window over a monitor, at that monitor's scale (DEC-069); the shell's DrawingStage opener.</summary>
    private static async Task<IDrawStageSurface> OpenStageAsync(MonitorInfo monitor) =>
        await DrawStageWindow.OpenAsync(monitor.Bounds, NativeFns.MonitorScale(monitor.Handle)).ConfigureAwait(false);

    /// <summary>
    /// The one dispatch between the teaching legs, so both call sites stay a single line. A question asked over a
    /// drawing on the stage is a follow-up and goes to the lesson leg without waiting for the route (DEC-074): the
    /// follow-up prompt decides keep, redraw, or NONE (then the Tutor). Otherwise the leg is TurnLegs.Choose's answer
    /// (DEC-036): with no router it is the old dispatch, the lesson on --lesson and otherwise the Tutor.
    /// </summary>
    private static async Task<int> TeachAsync(
        TutorService? tutor,
        LessonLeg? lesson,
        Task<TurnRoute?>? routing,
        VoiceService voice,
        ShellOptions options,
        GuidanceResult guidance,
        TurnRecord record,
        CancellationToken ct)
    {
        if (lesson?.Planner is not null && guidance.Captures.Count > 0 && lesson.Stage.FollowUpOn(guidance.Captures[0].Monitor) is not null)
        {
            Console.WriteLine("route       : skipped — a follow-up: the question was asked over the drawing on screen");
            record.Route = "a follow-up over the drawing on screen";
            record.Leg = NoteLeg.Lesson;
            return await TeachWithLessonAsync(tutor, lesson, voice, options, guidance, record, ct).ConfigureAwait(false);
        }

        var leg = await ChooseLegAsync(routing, options, ct).ConfigureAwait(false);
        if (leg is null)
        {
            Console.WriteLine("CANCELLED: the token aborted the turn while it was being routed; the verdict above was already reported.");
            return 13;
        }

        record.Route = leg.Why;
        switch (leg.Leg)
        {
            case TurnLeg.Highlight:
                Console.WriteLine("teach       : skipped — the route says point: the highlight was the answer");
                record.Leg = NoteLeg.Highlight;
                record.Outcome = "the highlight was the answer";
                return 0;
            case TurnLeg.Lesson when lesson is not null:
                record.Leg = NoteLeg.Lesson;
                return await TeachWithLessonAsync(tutor, lesson, voice, options, guidance, record, ct).ConfigureAwait(false);
            default:
                record.Leg = NoteLeg.Tutor;
                return await TeachAndSpeakAsync(tutor, voice, options, guidance, record, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The verdict as drawing context: the canvas is the screenshot the verdict was resolved against (Captures[0], the
    /// cursor's monitor), the grounded box goes in canvas units with its description, and the screen's text goes in
    /// as read (all three fenced as untrusted by the planner). An honest no-match carries the screenshot only.
    /// Precondition: !Failed (call sites).
    /// </summary>
    private static async Task<int> TeachWithLessonAsync(
        TutorService? tutor,
        LessonLeg lesson,
        VoiceService voice,
        ShellOptions options,
        GuidanceResult guidance,
        TurnRecord record,
        CancellationToken ct)
    {
        if (lesson.Planner is null)
        {
            record.Outcome = "skipped: no lesson model";
            Console.WriteLine($"lesson      : skipped — a lesson was chosen but no lesson model is configured ({LessonKeyEnvVar}");
            Console.WriteLine("                MISSING, DEC-027). The verdict above stands as-is.");
            return 0;
        }

        if (guidance.Captures.Count == 0)
        {
            // The drawing is sized and placed by the screenshot; without one (--no-retain-captures) the Tutor answers.
            Console.WriteLine("lesson      : no captured frame to size the drawing on (--no-retain-captures); the Tutor answers instead");
            record.Leg = NoteLeg.Tutor;
            return await TeachAndSpeakAsync(tutor, voice, options, guidance, record, ct).ConfigureAwait(false);
        }

        var frame = guidance.Captures[0];
        var request = new DrawingRequest
        {
            Question = options.Question,
            CanvasWidth = frame.OutputWidth,
            CanvasHeight = frame.OutputHeight,
            ScreenJpeg = frame.Jpeg,
            PointedAtBox = guidance.Found ? ToCanvas(guidance.BoxOnScreen, frame.Monitor.Bounds, frame.OutputWidth, frame.OutputHeight) : null,
            PointedAtDescription = guidance.Found ? guidance.Description : null,
            ScreenText = frame.TextLines.Count > 0 ? string.Join("\n", frame.TextLines.Select(l => l.Text)) : null,
            EarlierTurns = EarlierTurnsFor(options),
        };

        return await DrawAndReportAsync(tutor, lesson.Planner, lesson.Stage, voice, options, frame.Monitor, request, guidance, record, ct).ConfigureAwait(false);
    }

    /// <summary>
    /// A click on a part of the drawing, asked as a follow-up (DEC-074). There is no verdict for a click: the canvas is
    /// the drawing's own, no screenshot or screen text goes with it, and a NONE answer goes to the Tutor with an empty
    /// context. Kept in this run's memory (DEC-061) but not saved as a note: a note records a verdict, and there is
    /// none (FEATURE-011 step 9 reworks notes).
    /// </summary>
    private static async Task<int> TeachPartAsync(TutorService? tutor, LessonLeg lesson, VoiceService voice, ShellOptions options, string question, CancellationToken ct)
    {
        if (lesson.Planner is null || lesson.Stage.Drawing is not { } drawing)
        {
            Console.WriteLine("follow-up   : the drawing is gone, so there is nothing to ask about");
            return 0;
        }

        var asked = options with { Question = question };
        var record = new TurnRecord { Leg = NoteLeg.Lesson, Route = "a follow-up: a part of the drawing was clicked" };
        var request = new DrawingRequest
        {
            Question = question,
            CanvasWidth = drawing.Width,
            CanvasHeight = drawing.Height,
            EarlierTurns = EarlierTurnsFor(options),
        };

        var code = await DrawAndReportAsync(tutor, lesson.Planner, lesson.Stage, voice, asked, drawing.Monitor, request, NoVerdict, record, ct).ConfigureAwait(false);
        if (!options.NoContext)
        {
            Session.Add(TurnNotes.From(question, NotesSession, DateTimeOffset.Now, NoVerdict, record, code));
        }

        return code;
    }

    /// <summary>A verdict that says nothing (not found, not failed): a click's Tutor fallback teaches the concept alone.</summary>
    private static readonly GuidanceResult NoVerdict = new();

    /// <summary>DEC-061: this run's earlier turns, so a follow-up question can be understood.</summary>
    private static IReadOnlyList<EarlierTurn> EarlierTurnsFor(ShellOptions options) =>
        options.NoContext ? Array.Empty<EarlierTurn>() : Session.Snapshot().Select(t => new EarlierTurn(t.Question, t.Taught)).ToArray();

    /// <summary>
    /// Draws one lesson and reports it. Never throws: the TeachAndSpeakAsync contract and exit-code alphabet. 0 drawn
    /// (or closed by the person); NONE or no program hands the turn to the Tutor, whose code it returns; 12 a key error
    /// (the lesson model or the speech); 3 a provider or speech fault; 13 cancelled; 4 a stage or shell fault.
    /// </summary>
    private static async Task<int> DrawAndReportAsync(
        TutorService? tutor,
        DrawingPlanner planner,
        DrawingStage stage,
        VoiceService voice,
        ShellOptions options,
        MonitorInfo monitor,
        DrawingRequest request,
        GuidanceResult guidance,
        TurnRecord record,
        CancellationToken ct)
    {
        // Speech is the drawing's clock (pace speech). --no-speak keeps a clock but not the voice: the page paces each
        // beat by its own estimate of the line's length.
        Func<string, CancellationToken, Task>? speak = options.NoSpeak
            ? null
            : (text, token) => voice.SpeakAsync(new SpeakRequest { Text = text }, token);

        DrawingTurn turn;
        try
        {
            turn = await DrawOnStageAsync(planner, stage, monitor, request, speak, DrawingFinishWait, ct).ConfigureAwait(false);
        }
        catch (ProviderException ex) when (ex.Kind == ProviderErrorKind.AuthFailed)
        {
            // ProviderException.ToString is the provider layer's own secret-free form.
            Console.WriteLine($"KEY ERROR (lesson model): {ex}");
            return 12;
        }
        catch (ProviderException ex)
        {
            Console.WriteLine($"LESSON MODEL ERROR (propagated not swallowed): {ex}");
            return 3;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("CANCELLED: the token aborted the lesson; the verdict above was already reported.");
            return 13;
        }
        catch (TimeoutException ex)
        {
            Console.WriteLine($"LESSON STAGE FAULT: the drawing did not finish ({ex.Message})");
            return 4;
        }
        catch (Exception ex)
        {
            // The stage could not open (no WebView2 runtime, the page not deployed) or another defect.
            Console.WriteLine($"FAULT in the lesson leg: {ex.GetType().Name}: {ex.Message}");
            return 4;
        }

        PrintDrawing(turn, options.NoSpeak);
        if (turn.Result.Outcome != DrawingOutcome.Program)
        {
            Console.WriteLine(turn.Result.Outcome == DrawingOutcome.None
                ? "lesson      : the model says a picture does not answer this (NONE); the Tutor answers instead"
                : "lesson      : no drawing program came back; the Tutor answers instead");
            record.Leg = NoteLeg.Tutor;
            return await TeachAndSpeakAsync(tutor, voice, options, guidance, record, ct).ConfigureAwait(false);
        }

        if (turn.Drawn is { Length: > 0 } drawn)
        {
            record.Drawing = StoredDrawing.Store(request.CanvasWidth, request.CanvasHeight, drawn);
        }

        if (turn.FirstLine is { Length: > 0 } title)
        {
            record.LessonTitle = title.Length > MaxTitleChars ? title[..MaxTitleChars].TrimEnd() + "…" : title;
        }

        var how = turn.FollowUp ? (turn.Kept == true ? "a follow-up drawn after the drawing" : "a follow-up drawn afresh") : "drawn";
        record.Outcome = turn.Closed ? $"{how}, closed by the person" : turn.SpeechError is null ? how : $"{how}, the voice cut short";
        return turn.SpeechError switch
        {
            null => 0,
            VoiceAuthenticationException ex => Report($"KEY ERROR (speech): {ex.Message}", 12),
            VoiceClientException ex => Report($"SPEECH ERROR (propagated not swallowed): {ex.Message}", 3),
            var ex => Report($"FAULT in the lesson's speech: {ex.GetType().Name}: {ex.Message}", 4),
        };
    }

    private static int Report(string line, int code)
    {
        Console.WriteLine(line);
        return code;
    }

    private static void PrintDrawing(DrawingTurn turn, bool silent)
    {
        var result = turn.Result;
        var drawn = turn.Kept switch
        {
            null => "nothing drawn",
            true => "a follow-up, drawn after the drawing (keep)",
            false when turn.FollowUp => "a follow-up, drawn afresh",
            false => "drawn",
        };
        Console.WriteLine($"lesson      : {result.Outcome}, {drawn}{(silent && turn.Kept is not null ? " (--no-speak: paced, not voiced)" : string.Empty)}");
        Console.WriteLine($"  model       : {result.ModelId}  tokens={result.InputTokens}/{result.OutputTokens}  latency={result.Latency.TotalMilliseconds:F0} ms  " +
                          $"streamed={result.Streamed}  program={result.Program.Length} chars");
        Console.WriteLine($"  image       : {(result.UsedImage ? "sent" : $"not sent ({result.ImageNote ?? "none"})")}");
        if (result.FirstProgramLatency is { } first)
        {
            Console.WriteLine($"  first code  : {first.TotalMilliseconds:F0} ms after the lesson was asked for");
        }

        if (turn.Voice is { } voice)
        {
            Console.WriteLine($"  voice       : {voice.Spoken} line(s) spoken{(voice.CutShort ? ", cut short (the rest was drawn at once)" : string.Empty)}");
        }

        if (turn.Closed)
        {
            Console.WriteLine("  closed      : the person closed the drawing");
        }
        else if (turn.Kept is not null)
        {
            Console.WriteLine("  stage       : the drawing stays up; ask over it (Ctrl+Alt) or click a part for a follow-up, x to close");
        }
    }
}
