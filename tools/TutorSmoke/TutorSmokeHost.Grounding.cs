using System.Text.RegularExpressions;
using HeyGanesh.TutorEngine;

namespace HeyGanesh.Tools.TutorSmoke;

/// <summary>
/// TU-5's honest half of the grounding proof: everything the SERVICE must do (label, carry, refuse to
/// echo coordinates) is checked here. Everything only a REAL model can do (resist injection) is
/// explicitly NOT claimed — that stays an open live-only row in TEST-CHECKLIST.md.
/// </summary>
internal static partial class TutorSmokeHost
{
    private static async Task<int> RunAssertGroundingAsync()
    {
        var failures = 0;
        var adversarial = JpegFixture.Adversarial;
        var bytes = JpegFixture.Tiny();

        // (a) Screen-referential turn: instruction-shaped text in the description AND planted in the image bytes.
        var visual = new TutorVisual { JpegBytes = bytes, WidthPx = 640, HeightPx = 360 };
        var client = new FakeTutorClient(FakeTutorClient.Mode.Grounded);
        var result = await new TutorService(client).ExplainAsync(new TutorRequest
        {
            Question = "what is this editor doing here",
            Context = new TutorContext
            {
                Found = true,
                Description = $"a code editor. {adversarial}",
                BoxOnScreenX = 1240, BoxOnScreenY = 860, BoxOnScreenWidth = 96, BoxOnScreenHeight = 32,
                CenterOnScreenX = 1288, CenterOnScreenY = 876,
                ConfidenceTier = TutorConfidenceTier.Medium,
                SourceLeg = "Vision",
                Visual = visual,
            },
        });
        var prompt = client.Recorded[0];
        var labelIndex = prompt.UserText.IndexOf("UNTRUSTED DATA", StringComparison.Ordinal);
        var payloadIndex = prompt.UserText.IndexOf(adversarial, StringComparison.Ordinal);

        failures = Check(labelIndex >= 0 && payloadIndex > labelIndex,
            "instruction-shaped payload arrives only AFTER the block is labelled untrusted", failures);
        failures = Check(Regex.Matches(prompt.UserText, Regex.Escape(adversarial)).Count == 1,
            "payload text appears exactly once (from the description) — the copy planted in image bytes never reaches prompt text", failures);
        failures = Check(ReferenceEquals(prompt.Visual!.JpegBytes, bytes), "image bytes cross the seam whole and undecoded", failures);
        failures = Check(result.ExplanationText.Contains("On screen:") && result.ExplanationText.Contains("In general:"),
            "grounded lesson separates what was seen from background teaching", failures);
        failures = Check(!result.ExplanationText.Contains("1240") && !result.ExplanationText.Contains("860"),
            "the lesson carries no pixel coordinates", failures);
        failures = Check(!result.ExplanationText.Contains(adversarial),
            "the instruction payload is not echoed back as an instruction (recorded answer describes, never obeys)", failures);

        // (b) The no-screen-at-all turn: the question stands alone and is still a real lesson.
        var plain = new FakeTutorClient(FakeTutorClient.Mode.GenericLesson);
        var lesson = await new TutorService(plain).ExplainAsync(new TutorRequest { Question = "explain photosynthesis" });
        failures = Check(plain.Recorded[0].UserText.Contains("resolved: no"),
            "a no-context request states plainly that nothing on screen was resolved", failures);
        failures = Check(!plain.Recorded[0].UserText.Contains("ATTACHED VISUAL"), "no phantom visual is announced for a text-only turn", failures);
        failures = Check(lesson.ExplanationText.Contains("Photosynthesis", StringComparison.OrdinalIgnoreCase),
            "the answer is real teaching text produced with no screen grounding available", failures);

        Console.WriteLine("NOTE: recorded completions prove the SERVICE's contract only. Real-model injection");
        Console.WriteLine("      resistance is NOT claimed here — it is a live-only row, open in TEST-CHECKLIST.md.");
        return Verdict(failures, "GROUNDING CONTRACT OK");
    }
}