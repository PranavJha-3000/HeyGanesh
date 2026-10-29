using HeyGanesh.Abstractions;
using HeyGanesh.LessonPlanner;
using HeyGanesh.LessonSession;
using HeyGanesh.LessonSurface;
using HeyGanesh.Providers.Core;
using HeyGanesh.Providers.OpenAiCompat;
using HeyGanesh.ScreenOverlay;
using HeyGanesh.TurnRouter;
using HeyGanesh.TutorEngine;
using HeyGanesh.VisualLessonEngine.Runtime;
using HeyGanesh.Voice;

namespace HeyGanesh.App;

// The lesson leg (FEATURE-007 Phase 4.5, DEC-027): with --lesson, a produced verdict is taught as a
// drawn, spoken lesson instead of the Tutor's prose. Same seat in the turn as the Tutor leg: it runs
// only on non-Failed verdicts, after the overlay has reported, and it re-decides nothing.
//
// The shell keeps the two jobs DEC-015/016 give it and nothing more: it maps the verdict to plain
// grounding data (GuidanceResult never leaves this file), and it hands Voice's SpeakAsync in as the
// lesson's speaker. Planning, cards and playback are HeyGanesh.LessonSession's LessonTurn, which
// tools/LessonTurnSmoke drives with a fake model — this assembly carries no fakes (DEC-012).

internal static partial class ShellHost
{
    /// <summary>The composed lesson seam. Present whenever --lesson is on; <see cref="Planner"/> is null without a key.</summary>
    private sealed record LessonLeg(VisualPlanner? Planner, ScreenOverlayService Marks);

    private const string LessonKeyEnvVar = "DEEPSEEK_API_KEY";

    /// <summary>
    /// How long a finished 3D lesson waits for a click on a part (DEC-031/DEC-033). Shorter than the
    /// session default because Ctrl+Alt is refused while it waits: one turn at a time.
    /// </summary>
    private static readonly TimeSpan LessonPickIdle = TimeSpan.FromSeconds(30);

    /// <summary>For the header: how many 3D models a lesson may show (the cards' catalogue, DEC-033).</summary>
    private static int Scene3dModelCount => CardSurface.Scene3dCatalog.Models.Count;

    /// <summary>
    /// DEC-027: the lesson planner is built from the environment, not held behind a code flip like the
    /// Tutor's (DEC-017). With no key the leg reports an honest skip; with one, the first lesson works
    /// with no code change. DeepSeek through the OpenAI-compatible adapter is the provider the repo
    /// already commits to (DEC-017: deepseek-flash, DEEPSEEK_API_KEY). It has no Vision, so the planner
    /// sends no image and says so in the prompt (DEC-024) — a Vision-capable provider is a registry
    /// assignment away, not a planner change.
    /// </summary>
    /// <remarks>
    /// Ownership: the planner owns the client it resolves from the registry (one per process, not per
    /// question) and RunAsync's using disposes it. The key is read only by EnvironmentSecretStore, per
    /// request, and never printed — the header reports presence only.
    /// </remarks>
    private static VisualPlanner? CreateLessonPlanner()
    {
        if (string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(LessonKeyEnvVar)))
        {
            return null;
        }

        var registry = new ProviderRegistry();
        registry.RegisterLlmFactory("deepseek", model => new OpenAiCompatLlmClient(new OpenAiCompatOptions
        {
            ProviderId = "deepseek",
            BaseUrl = "https://api.deepseek.com",
            ModelId = model,
            Capabilities = ProviderCapability.Text,
            SecretStore = new EnvironmentSecretStore(LessonKeyEnvVar),
            CredentialProviderId = "deepseek",
        }));
        registry.Assign(new ProviderAssignment { Role = ProviderRole.Tutor, ProviderId = "deepseek", ModelId = "deepseek-flash" });
        return VisualPlanner.FromRegistry(registry);
    }

    /// <summary>
    /// The one dispatch between the teaching legs, so both call sites stay a single line. The leg is
    /// TurnLegs.Choose's answer (DEC-036): with no router it is exactly the old dispatch — the lesson
    /// on --lesson, otherwise the Tutor — and a route can also pick point (nothing more to teach) or a
    /// lesson on one model or in 2D.
    /// </summary>
    private static async Task<int> TeachAsync(
        TutorService? tutor,
        LessonLeg? lesson,
        Task<TurnRoute?>? routing,
        VoiceService voice,
        ShellOptions options,
        GuidanceResult guidance,
        CancellationToken ct)
    {
        var leg = await ChooseLegAsync(routing, options, ct).ConfigureAwait(false);
        if (leg is null)
        {
            Console.WriteLine("CANCELLED: the token aborted the turn while it was being routed; the verdict above was already reported.");
            return 13;
        }

        switch (leg.Leg)
        {
            case TurnLeg.Highlight:
                Console.WriteLine("teach       : skipped — the route says point: the highlight was the answer");
                return 0;
            case TurnLeg.Lesson when lesson is not null:
                return await TeachWithLessonAsync(lesson, CatalogFor(leg), voice, options, guidance, ct).ConfigureAwait(false);
            default:
                return await TeachAndSpeakAsync(tutor, voice, options, guidance, ct).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// The verdict as planner grounding — the lesson-side twin of MapGuidanceToContext. Found: the
    /// description, the resolved box (the only place a lesson may mark, LS-12) and the frame the verdict
    /// was resolved against, as already-encoded bytes. Honest no-match: description and box absent, so
    /// every screen mark is refused and the question stands alone. Precondition: !Failed (call sites).
    /// </summary>
    private static ScreenGrounding? MapGuidanceToGrounding(GuidanceResult guidance)
    {
        if (!guidance.Found)
        {
            return null;
        }

        var frame = guidance.Captures.Count > 0 ? guidance.Captures[0] : null;
        return new ScreenGrounding(
            guidance.Description,
            guidance.BoxOnScreen,
            frame?.Jpeg,
            frame?.OutputWidth ?? 0,
            frame?.OutputHeight ?? 0);
    }

    /// <summary>
    /// Plans, draws and speaks one lesson, then any click-to-ask follow-ups (DEC-033): the planner is
    /// offered <paramref name="scene3d"/> (null: 2D only), and a finished 3D lesson waits <see cref="LessonPickIdle"/> for
    /// the student to click a part. Never throws — the TeachAndSpeakAsync contract, the same exit-code
    /// alphabet: 0 played (or the honest no-key skip), 1 nothing usable came back (no lesson was
    /// produced, like "no transcript was produced"), 12 key error, 3 provider/speech fault,
    /// 13 cancelled, 4 a shell or card defect. With follow-ups the code is the LAST turn's: the loop
    /// stops at the first turn that did not play to the end, so that turn is the one that ended it.
    /// </summary>
    private static async Task<int> TeachWithLessonAsync(
        LessonLeg lesson,
        VisualLessonEngine.ModelCatalog? scene3d,
        VoiceService voice,
        ShellOptions options,
        GuidanceResult guidance,
        CancellationToken ct)
    {
        if (lesson.Planner is null)
        {
            Console.WriteLine($"lesson      : skipped — a lesson was chosen but no lesson model is configured ({LessonKeyEnvVar}");
            Console.WriteLine("                MISSING, DEC-027). The verdict above stands as-is.");
            return 0;
        }

        // Speech is the lesson's clock. --no-speak keeps the clock but not the voice: each sentence
        // holds its beat for roughly the time it would take to say, so the cards still pace.
        ILessonSpeaker speaker = options.NoSpeak
            ? new DelegateLessonSpeaker((text, token) => Task.Delay(Math.Clamp(text.Length * 60, 600, 8000), token))
            : new DelegateLessonSpeaker((text, token) => voice.SpeakAsync(new SpeakRequest { Text = text }, token));

        try
        {
            var conversation = await LessonTurn.RunWithFollowUpsAsync(
                lesson.Planner,
                new CardSurfaceHost(marks: lesson.Marks),
                speaker,
                new PlanRequest
                {
                    Question = options.Question,
                    Screen = MapGuidanceToGrounding(guidance),
                    VirtualDesktop = NativeFns.VirtualDesktop(),
                    // The whole catalogue on --lesson, one model or none when the route chose (DEC-036).
                    Scene3d = scene3d,
                },
                new FollowUpOptions { IdleTimeout = LessonPickIdle },
                ct).ConfigureAwait(false);

            for (var i = 0; i < conversation.Turns.Count; i++)
            {
                if (i > 0)
                {
                    var pick = conversation.Picks[i - 1];
                    Console.WriteLine($"follow-up {i}  : the student clicked '{pick.PartId}' on '{pick.ModelId}'");
                }

                PrintLesson(conversation.Turns[i], options.NoSpeak);
            }

            Console.WriteLine($"  follow-ups  : {conversation.Picks.Count}, ended {conversation.End}");
            if (conversation.End == FollowUpEnd.Cancelled)
            {
                // Cancelled while the finished lesson waited for a click: the lesson itself played, but the
                // run was aborted, and the exit code says so as it would mid-lesson.
                return 13;
            }

            var report = conversation.Turns[^1];
            return report.Outcome switch
            {
                LessonTurnOutcome.Played or LessonTurnOutcome.PlayedFallback or LessonTurnOutcome.Stopped => 0,
                LessonTurnOutcome.Rejected => 1,
                LessonTurnOutcome.AuthError => 12,
                LessonTurnOutcome.ProviderError => 3,
                LessonTurnOutcome.Cancelled => 13,
                _ => 4,
            };
        }
        catch (VoiceAuthenticationException ex)
        {
            Console.WriteLine($"KEY ERROR (speech): {ex.Message}");
            return 12;
        }
        catch (VoiceClientException ex)
        {
            Console.WriteLine($"SPEECH ERROR (propagated not swallowed): {ex.Message}");
            return 3;
        }
        catch (OperationCanceledException)
        {
            Console.WriteLine("CANCELLED: the token aborted the lesson; the verdict above was already reported.");
            return 13;
        }
        catch (TimeoutException ex)
        {
            // A card that never acknowledged a beat or a layout (LSU-2's budget). A surface defect, not a verdict.
            Console.WriteLine($"LESSON SURFACE FAULT: {ex.Message}");
            return 4;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAULT in the lesson leg: {ex.GetType().Name}: {ex.Message}");
            return 4;
        }
    }

    private static void PrintLesson(LessonTurnReport report, bool silent)
    {
        Console.WriteLine($"lesson      : {report.Outcome}{(silent ? " (--no-speak: paced, not voiced)" : string.Empty)}");
        if (report.Plan is { } plan)
        {
            Console.WriteLine($"  plan        : {plan.Lesson.Outcome}  attempts={plan.Attempts}  model={plan.ModelId}  " +
                              $"tokens={plan.InputTokens}/{plan.OutputTokens}  latency={plan.Latency.TotalMilliseconds:F0} ms");
            Console.WriteLine($"  image       : {(plan.UsedImage ? "sent" : $"not sent ({plan.ImageNote ?? "none in the grounding"})")}");
            if (plan.RepairError is { } repair)
            {
                Console.WriteLine($"  repair      : failed, first answer kept ({repair})");
            }
        }

        if (report.Playback is { } playback)
        {
            Console.WriteLine($"  playback    : {playback.Status}  beats={playback.Beats.Count}  degraded={playback.Beats.Count(b => b.Degraded)}  " +
                              $"fallback={playback.SpokeFallback}");
        }

        if (report.Error is { } error)
        {
            // ProviderException.ToString — the provider layer's own secret-free form.
            Console.WriteLine($"  error       : {error}");
        }
    }
}
