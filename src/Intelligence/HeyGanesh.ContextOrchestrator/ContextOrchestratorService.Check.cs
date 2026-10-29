using HeyGanesh.Abstractions;
using HeyGanesh.ElementSelection;
using HeyGanesh.ScreenCapture;
using HeyGanesh.Vision;

namespace HeyGanesh.ContextOrchestrator;

// The Low-confidence check (DEC-037). When Element Selection found an element but its own blended
// score put it in the Low tier, Vision looks at the same frame as a second opinion. Element Selection
// stays primary: its answer is replaced only when Vision is at least Medium sure AND points somewhere
// else. A check that cannot help — Vision unsure, not found, failed, or erroring — never loses the
// answer we already had. High and Medium choices, the honest no-match and genuine failures are not
// checked: failures still take the DEC-010 fallback, unchanged.

/// <summary>What the check decided, in words the shell prints as-is.</summary>
public sealed record CheckDecision(bool UseVision, string Note);

/// <summary>
/// The check's rule as a pure function, so every case is verified offline (CO-12) without a window,
/// a key or a model.
/// </summary>
public static class LowConfidenceCheck
{
    /// <summary>Found, not failed, and Low: the only verdict the check looks at.</summary>
    public static bool Applies(SelectionResult selection) =>
        !selection.Failed && selection.Found && selection.Confidence == ElementSelection.ConfidenceLevel.Low;

    /// <param name="selection">Element Selection's Low-confidence verdict.</param>
    /// <param name="vision">Vision's answer on the same frame, or null when it threw.</param>
    /// <param name="visionError">Why Vision threw, when <paramref name="vision"/> is null.</param>
    public static CheckDecision Decide(SelectionResult selection, VisionResult? vision, string? visionError)
    {
        const string Kept = "kept Element Selection's Low choice";

        if (vision is null)
        {
            return new(false, $"Vision could not check ({visionError ?? "no answer"}); {Kept}");
        }

        if (vision.Failed)
        {
            return new(false, $"Vision's check failed ({vision.FailureReason}); {Kept}");
        }

        if (!vision.Found || vision.CenterOnScreen is not { } center)
        {
            return new(false, $"Vision did not find it; {Kept}");
        }

        // Agreement: Vision's centre lands on the control Element Selection chose. The UIA box is the
        // exact control, so it is the one kept.
        if (selection.BoxOnScreen is { } box
            && center.X >= box.X && center.X < box.Right
            && center.Y >= box.Y && center.Y < box.Bottom)
        {
            return new(false, "Vision agrees (its centre is inside Element Selection's box); kept Element Selection's box");
        }

        if (vision.Confidence != Vision.ConfidenceLevel.Low)
        {
            return new(true, $"Vision found it elsewhere at {vision.Confidence} confidence; using Vision's box");
        }

        return new(false, $"Vision found it elsewhere but only at Low confidence; {Kept}");
    }
}

public sealed partial class ContextOrchestratorService
{
    /// <summary>
    /// Runs the check for a Low-confidence primary verdict and returns the verdict it decides on, with
    /// <see cref="GuidanceResult.ConfidenceCheck"/> saying what happened. Vision's provider errors are
    /// caught here and only here — a second opinion that fails must not lose the first answer.
    /// Cancellation still propagates.
    /// </summary>
    private async Task<GuidanceResult> CheckLowConfidenceAsync(
        string question,
        SelectionResult selection,
        IReadOnlyList<MonitorCapture> captures,
        long started,
        CancellationToken token)
    {
        _phase = OrchestratorPhase.CheckingConfidence;

        VisionResult? vision = null;
        string? error = null;
        try
        {
            // captures[0] is the cursor's display, as for the fallback.
            vision = await _vision.LocateAsync(captures[0], question, token).ConfigureAwait(false);
        }
        catch (VisionClientException ex)
        {
            // Auth (no DEEPSEEK_API_KEY), rate limit and HTTP faults alike; their messages carry no key.
            error = $"{ex.GetType().Name}: {ex.Message}";
        }

        var decision = LowConfidenceCheck.Decide(selection, vision, error);
        var elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started);
        var verdict = decision.UseVision
            ? FromVision(vision!, selection, captures, elapsed)
            : FromSelection(selection, captures, elapsed);
        return verdict with { ConfidenceCheck = decision.Note };
    }
}
