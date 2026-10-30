using HeyGanesh.Abstractions;
using HeyGanesh.TutorEngine;
using HeyGanesh.Voice;

namespace HeyGanesh.App;

// The teaching leg: the one place a guidance verdict becomes a TutorRequest, and the one place the
// Tutor's answer becomes speech. Both mappings are the shell's job by DEC-015 — the Tutor must never
// know GuidanceResult, MonitorCapture or any ContextOrchestrator type exists, and Voice must keep
// knowing nothing but text. Everything screen-derived crosses here as scalar data or opaque bytes.
//
// This half of the turn runs only on non-Failed verdicts (the short-circuit lives at the call sites
// in ShellHost.Session.cs), after the overlay has reported. It re-decides nothing: the CO-6 quirk
// (Vision's model-side not-found arrives Found=false WITH Failed=true) skips the Tutor exactly as the
// overlay already skips drawing. Teaching from "no verdict" is fabrication; teaching the general
// concept behind an honest no-match is the Empty-context path and is legitimate.

internal static partial class ShellHost
{
    /// <summary>
    /// The point DEC-015 left open, switched on by DEC-047 (the owner chose it; DEC-017 already chose the
    /// provider): the DeepSeek Tutor (<c>deepseek-flash</c>), reading its key per request through the key
    /// ring, whenever the ring has the deepseek key. Without one it returns <see langword="null"/> and the
    /// leg reports an honest skip, as the lesson leg does (DEC-027), rather than failing every turn on auth.
    /// </summary>
    /// <remarks>
    /// Ownership: whatever this returns is shell-owned and disposed by RunAsync's using.
    /// <see cref="TutorService"/> never disposes the seam it was handed (it owns nothing), so the
    /// composition root is the owner — the same arrangement the composed VoiceService keeps. Keep the
    /// header's tutor line (TutorHeader) in step with this gate.
    /// </remarks>
    private static ITutorClient? CreateTutorClient() =>
        HasKey("deepseek")
            ? TutorClientFactory.CreateDeepSeek(new DeepSeekTutorOptions { ApiKey = Keys.Reader("deepseek") })
            : null;

    /// <summary>The header's tutor line; keep in step with CreateTutorClient().</summary>
    private static string TutorHeader() =>
        HasKey("deepseek")
            ? $"ON: deepseek-flash explains in prose when no lesson is drawn (DEEPSEEK_API_KEY {Keys.Describe("deepseek")})"
            : "off: no DeepSeek key, so the teaching leg reports its skip (store one with --set-key deepseek)";

    /// <summary>
    /// The scalar-snapshot mapping, written exactly as TutorContext's remarks prescribe. Deliberately
    /// NOT mapped: Choice, the Jev probabilities, RawConfidence/Consistency, PrimaryFailureReason,
    /// Latency, token counts (raw leg signals and session accounting, not teaching metadata), and the
    /// Captures collection itself — only a derived visual's already-encoded bytes cross, never a
    /// capture object. Precondition: !Failed (enforced by the call sites, not re-checked here).
    /// </summary>
    private static TutorContext MapGuidanceToContext(GuidanceResult guidance)
    {
        if (!guidance.Found)
        {
            // An honest no-match carries no grounding: the question stands alone and the system
            // prompt's "resolved: no" path teaches the general concept (TU-5's shape). Attaching a
            // frame here would spend visual budget on a turn whose verdict says nothing was seen.
            return TutorContext.Empty;
        }

        return new TutorContext
        {
            Found = true,
            Description = guidance.Description,

            // PixelRect and GuidancePoint are whole or absent, so the Tutor's all-four-or-none box
            // and centre-requires-box rules hold by construction, not by re-validation here.
            BoxOnScreenX = guidance.BoxOnScreen?.X,
            BoxOnScreenY = guidance.BoxOnScreen?.Y,
            BoxOnScreenWidth = guidance.BoxOnScreen?.Width,
            BoxOnScreenHeight = guidance.BoxOnScreen?.Height,
            CenterOnScreenX = guidance.CenterOnScreen?.X,
            CenterOnScreenY = guidance.CenterOnScreen?.Y,

            ConfidenceTier = guidance.Confidence switch
            {
                GuidanceConfidence.High => TutorConfidenceTier.High,
                GuidanceConfidence.Medium => TutorConfidenceTier.Medium,
                _ => TutorConfidenceTier.Low,
            },

            SourceLeg = guidance.Source.ToString(),
            ModelId = guidance.ModelId is { Length: > 0 } model ? model : null,

            Visual = BuildVisual(guidance),
        };
    }

    /// <summary>
    /// The only visual derivation that exists without an image pipeline: <c>Captures[0]</c> — the
    /// cursor's display, the frame the verdict was resolved against — handed over as the
    /// already-encoded JPEG, by reference (TutorService never decodes or mutates bytes). Kind is
    /// DownscaledFrame and the source region stays null because these bytes ARE the whole display
    /// frame. Size caps are NOT pre-checked here: TutorService enforces them pre-seam (caller bug →
    /// exit 4), so the caps have exactly one enforcer and no provider call is risked by an oversized
    /// frame. Crop-around-box needs a JPEG decoder and is the filed follow-up (FEATURE-006).
    /// </summary>
    private static TutorVisual? BuildVisual(GuidanceResult guidance)
    {
        if (!guidance.Found || guidance.Captures.Count == 0)
        {
            return null;
        }

        var frame = guidance.Captures[0];
        return new TutorVisual
        {
            JpegBytes = frame.Jpeg,
            WidthPx = frame.OutputWidth,
            HeightPx = frame.OutputHeight,
            Kind = TutorVisualKind.DownscaledFrame,
        };
    }

    /// <summary>
    /// Teaches a produced verdict and speaks the lesson. Never throws — the same contract
    /// <c>RunSessionAsync</c> keeps: every documented failure maps to one line of output and an exit
    /// code. The verdict has always been reported (and drawn, when enabled) before this runs, so a
    /// leg failure below replaces the exit code without hiding anything.
    /// </summary>
    private static async Task<int> TeachAndSpeakAsync(
        TutorService? tutor,
        VoiceService voice,
        ShellOptions options,
        GuidanceResult guidance,
        CancellationToken ct)
    {
        if (tutor is null)
        {
            // No DeepSeek key (DEC-047): the leg is skipped honestly. A produced verdict stands on its own —
            // skipping the explanation is not a failure.
            Console.WriteLine("tutor       : skipped — no DeepSeek key (store one with --set-key deepseek, or set");
            Console.WriteLine("                DEEPSEEK_API_KEY). The verdict above stands as-is.");
            return 0;
        }

        try
        {
            var result = await tutor.ExplainAsync(
                new TutorRequest { Question = options.Question, Context = MapGuidanceToContext(guidance) },
                ct).ConfigureAwait(false);

            Console.WriteLine($"tutor       : {result}");
            Console.WriteLine($"  lesson      : {result.ExplanationText}");

            if (options.NoSpeak)
            {
                Console.WriteLine("speech      : skipped (--no-speak)");
                return 0;
            }

            // The explanation goes to the speaker VERBATIM — the Tutor owns the prose, and
            // MaxExplanationChars is already the contract with this consumer (DEC-015).
            var spoken = await voice.SpeakAsync(
                new SpeakRequest { Text = result.ExplanationText }, ct).ConfigureAwait(false);
            Console.WriteLine($"speech      : {spoken}");
            return 0;
        }
        catch (TutorAuthenticationException ex)
        {
            Console.WriteLine($"KEY ERROR (Tutor): {ex.Message}");
            return 12;
        }
        catch (TutorClientException ex)
        {
            // Covers TutorRateLimitException too. Safe to print: the service attaches no inner chain
            // to translated faults and TU-6 pins that no provider-echoed credential rides along.
            Console.WriteLine($"TUTOR ERROR (propagated not swallowed): {ex.Message}");
            return 3;
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
            Console.WriteLine("CANCELLED: the token aborted the teach/speak leg; the verdict above was already " +
                              "produced and reported, and no lesson is fabricated to fill the gap.");
            return 13;
        }
        catch (ArgumentException ex)
        {
            // TutorService draws its fault line here: an ArgumentException means the SHELL mapped
            // something malformed (half a box, an over-cap visual). That is a shell defect — exit 4 —
            // and dressing it up as a provider failure would erase the line this repo insists on.
            Console.WriteLine($"TUTOR MAPPING FAULT (shell bug): {ex.GetType().Name}: {ex.Message}");
            return 4;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"FAULT in the teaching leg: {ex.GetType().Name}: {ex.Message}");
            return 4;
        }
    }
}