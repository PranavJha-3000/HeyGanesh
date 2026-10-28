using HeyGanesh.Abstractions;
using HeyGanesh.ElementSelection;
using HeyGanesh.ScreenCapture;
using HeyGanesh.Vision;

namespace HeyGanesh.ContextOrchestrator;

// Second half of ContextOrchestratorService: the normalization. SelectionResult -> GuidanceResult and
// VisionResult -> GuidanceResult live side by side in one file, because reading them together is the
// only way to see that they really do produce the same shape.
//
// These two methods are the reason this module exists in DEC-009's story: the legs stay separate types,
// and the single-consumer shape is produced here — not by either leg, and not by a shared abstraction
// assembly (which DEC-009 keeps forbidden).

public sealed partial class ContextOrchestratorService
{
    /// <summary>
    /// Maps the PRIMARY leg's verdict. Only ever called for a non-Failed result, so there is no failure
    /// to carry: the honest no-match (<c>Found</c> = false, <c>Choice</c> = "none") arrives here too,
    /// and it is a verdict, not a failure.
    /// </summary>
    private GuidanceResult FromSelection(
        SelectionResult selection,
        IReadOnlyList<MonitorCapture> captures,
        TimeSpan latency)
        => new()
        {
            Found = selection.Found,
            Description = selection.Description,
            BoxOnScreen = selection.BoxOnScreen,
            CenterOnScreen = selection.CenterOnScreen is { } center
                ? new GuidancePoint(center.X, center.Y)
                : null,
            Confidence = selection.Confidence switch
            {
                // The same 0.8/0.5 tiers on both sides: mapped member-by-member, never compared.
                ElementSelection.ConfidenceLevel.High => GuidanceConfidence.High,
                ElementSelection.ConfidenceLevel.Medium => GuidanceConfidence.Medium,
                _ => GuidanceConfidence.Low,
            },
            Source = GuidanceSource.ElementSelection,
            // Null by construction: a primary verdict is not a failure (see this method's summary).
            FailureReason = null,
            PrimaryFailureReason = null,
            Choice = selection.Choice,
            JevConfidence = selection.JevConfidence,
            WinnerProbability = selection.WinnerProbability,
            NoneProbability = selection.NoneProbability,
            PlausibleProbability = selection.PlausibleProbability,
            ModelId = selection.ModelId,
            Captures = Options.RetainCaptures ? captures : Array.Empty<MonitorCapture>(),
            Latency = latency,
            InputTokens = selection.InputTokens,
            OutputTokens = selection.OutputTokens,
        };

    /// <summary>
    /// Maps the FALLBACK leg's verdict, carrying the primary leg's failure reason and raw signals
    /// forward as diagnostics — the fallback result alone cannot tell you <i>why</i> the primary was
    /// skipped, which is what a bug report needs to know.
    /// </summary>
    private GuidanceResult FromVision(
        VisionResult vision,
        SelectionResult primary,
        IReadOnlyList<MonitorCapture> captures,
        TimeSpan latency)
        => new()
        {
            Found = vision.Found,
            Description = vision.Description,
            BoxOnScreen = vision.BoxOnScreen,
            CenterOnScreen = vision.CenterOnScreen is { } center
                ? new GuidancePoint(center.X, center.Y)
                : null,
            Confidence = vision.Confidence switch
            {
                Vision.ConfidenceLevel.High => GuidanceConfidence.High,
                Vision.ConfidenceLevel.Medium => GuidanceConfidence.Medium,
                _ => GuidanceConfidence.Low,
            },
            Source = GuidanceSource.Vision,
            // Vision's own failure (blank frame / provider refusal / parse exhausted). Null here means
            // the model answered honestly that the element is not on screen — still not a failure.
            FailureReason = vision.FailureReason,
            PrimaryFailureReason = primary.FailureReason,
            Choice = primary.Choice,
            JevConfidence = primary.JevConfidence,
            WinnerProbability = primary.WinnerProbability,
            NoneProbability = primary.NoneProbability,
            PlausibleProbability = primary.PlausibleProbability,
            RawConfidence = vision.RawConfidence,
            Consistency = vision.Consistency,
            ModelId = vision.ModelId,
            Captures = Options.RetainCaptures ? captures : Array.Empty<MonitorCapture>(),
            Latency = latency,
            // Both legs' spend is summed: a fallback session is the expensive case, and hiding the
            // primary leg's tokens would understate exactly the sessions worth watching.
            InputTokens = primary.InputTokens + vision.PromptTokens,
            OutputTokens = primary.OutputTokens + vision.CompletionTokens,
        };

}
