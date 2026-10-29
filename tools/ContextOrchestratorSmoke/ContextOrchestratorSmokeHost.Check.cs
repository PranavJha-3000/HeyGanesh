using HeyGanesh.Abstractions;
using HeyGanesh.ContextOrchestrator;
using HeyGanesh.ElementSelection;
using HeyGanesh.Vision;

namespace HeyGanesh.Tools.ContextOrchestratorSmoke;

// CO-12: the Low-confidence check's rule (DEC-037), from made-up results. No window, capture, key or
// model is touched, so this is the one orchestrator row CI can run.

internal static partial class ContextOrchestratorSmokeHost
{
    private static int RunAssertCheck()
    {
        Console.WriteLine("Context Orchestrator verification host -- Low-confidence check rule (made-up results, no window)");
        Console.WriteLine();
        var failures = 0;
        var box = new PixelRect(100, 100, 80, 30);
        var low = new SelectionResult { Found = true, BoxOnScreen = box, Confidence = ElementSelection.ConfidenceLevel.Low, Choice = "c003" };

        failures = Check(LowConfidenceCheck.Applies(low), "found + Low -> the check applies", failures);
        failures = Check(!LowConfidenceCheck.Applies(low with { Confidence = ElementSelection.ConfidenceLevel.Medium }), "Medium is never checked", failures);
        failures = Check(!LowConfidenceCheck.Applies(low with { Confidence = ElementSelection.ConfidenceLevel.High }), "High is never checked", failures);
        failures = Check(!LowConfidenceCheck.Applies(low with { Found = false, BoxOnScreen = null, Choice = "none" }), "the honest no-match is never checked", failures);
        failures = Check(!LowConfidenceCheck.Applies(low with { FailureReason = "Jev chose c999" }), "a failed primary is never checked (it takes the DEC-010 fallback)", failures);

        var agree = LowConfidenceCheck.Decide(low, Seen(140, 115, Vision.ConfidenceLevel.Medium), null);
        failures = Check(!agree.UseVision && agree.Note.Contains("agrees", StringComparison.Ordinal), $"Vision's centre inside the box -> keep Element Selection ({agree.Note})", failures);

        var agreeLow = LowConfidenceCheck.Decide(low, Seen(101, 101, Vision.ConfidenceLevel.Low), null);
        failures = Check(!agreeLow.UseVision && agreeLow.Note.Contains("agrees", StringComparison.Ordinal), "agreement counts whatever Vision's own tier", failures);

        var edge = LowConfidenceCheck.Decide(low, Seen(180, 115, Vision.ConfidenceLevel.High), null);
        failures = Check(edge.UseVision, "the box's right edge is outside (x = Right is elsewhere)", failures);

        var elsewhere = LowConfidenceCheck.Decide(low, Seen(900, 600, Vision.ConfidenceLevel.Medium), null);
        failures = Check(elsewhere.UseVision && elsewhere.Note.Contains("elsewhere", StringComparison.Ordinal), $"elsewhere at Medium -> use Vision ({elsewhere.Note})", failures);

        var unsure = LowConfidenceCheck.Decide(low, Seen(900, 600, Vision.ConfidenceLevel.Low), null);
        failures = Check(!unsure.UseVision, $"elsewhere at Low -> keep Element Selection ({unsure.Note})", failures);

        var missing = LowConfidenceCheck.Decide(low, new VisionResult { Found = false }, null);
        failures = Check(!missing.UseVision && missing.Note.Contains("did not find", StringComparison.Ordinal), "Vision not found -> keep Element Selection", failures);

        var failed = LowConfidenceCheck.Decide(low, new VisionResult { Found = false, FailureReason = "blank frame" }, null);
        failures = Check(!failed.UseVision && failed.Note.Contains("blank frame", StringComparison.Ordinal), "Vision failed -> keep Element Selection, with Vision's reason", failures);

        var threw = LowConfidenceCheck.Decide(low, null, "VisionAuthenticationException: DeepSeek API key not found");
        failures = Check(!threw.UseVision && threw.Note.Contains("could not check", StringComparison.Ordinal) && threw.Note.Contains("key not found", StringComparison.Ordinal),
            "Vision threw (e.g. no DEEPSEEK_API_KEY) -> keep Element Selection, with the error", failures);

        Console.WriteLine(failures == 0 ? "CHECK RULE OK (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    private static VisionResult Seen(int x, int y, Vision.ConfidenceLevel confidence) => new()
    {
        Found = true,
        BoxOnScreen = new PixelRect(x - 10, y - 10, 20, 20),
        CenterOnScreen = new Vision.PointI(x, y),
        Confidence = confidence,
    };

    private static int Check(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }
}
