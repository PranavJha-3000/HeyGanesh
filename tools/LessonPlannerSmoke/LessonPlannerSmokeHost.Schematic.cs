using HeyGanesh.LessonPlanner;
using HeyGanesh.PartLibrary;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonPlannerSmoke;

/// <summary>
/// LP-12 (DEC-049): schematics are offered exactly when they would be accepted — a catalogue AND the Part
/// Library's geometry check in the request. The SCHEMATICS block is read from the code the validator uses,
/// asks for the header before the beats and for a spoken "simplified", and the recorded F-35B lesson plans,
/// streams and validates through the real planner.
/// </summary>
internal static partial class LessonPlannerSmokeHost
{
    private static async Task<int> RunAssertPromptSchematicAsync()
    {
        var failures = 0;
        var answer = Fixture("f35b-schematic.json");
        var catalog = ModelLibrary.ToCatalog();
        PlanRequest JetRequest(bool check, bool scene = true) =>
            Request("can you draw the full jet in 3D and explain how the engines work?") with
            {
                Scene3d = scene ? catalog : null,
                SchematicGeometry = check ? Schematics.Check : null,
            };

        async Task<(PlanResult Result, string System)> PlanOnce(PlanRequest request, string recorded)
        {
            var client = new FakeLlmClient(TextOnly, recorded, recorded);
            var result = await new VisualPlanner(client).PlanAsync(request);
            return (result, client.Recorded[0].SystemPrompt ?? string.Empty);
        }

        // 1. Offered only with both: the 3D prompt is unchanged without the check, and so is the 2D one.
        var (_, without) = await PlanOnce(JetRequest(check: false), Fixture("engine-3d.json"));
        var (_, flat) = await PlanOnce(JetRequest(check: true, scene: false), Fixture("chain-rule.json"));
        var (planned, with) = await PlanOnce(JetRequest(check: true), answer);
        failures = Check(!without.Contains("SCHEMATICS", StringComparison.Ordinal) && without.Contains($"\"schema\": \"{VisualPlanValidator.Schema11}\"", StringComparison.Ordinal)
                         && !flat.Contains("SCHEMATICS", StringComparison.Ordinal) && !flat.Contains("scene3d", StringComparison.Ordinal),
            "no geometry check -> the 3D prompt as before (schema 1.1, no SCHEMATICS); no catalogue -> the 2D prompt", failures);
        failures = Check(with.StartsWith(without.Replace(VisualPlanValidator.Schema11, VisualPlanValidator.Schema12, StringComparison.Ordinal), StringComparison.Ordinal)
                         && with.Contains($"\"schema\": \"{VisualPlanValidator.Schema12}\"", StringComparison.Ordinal),
            $"catalogue + check -> the same prompt asking for schema 1.2, plus a SCHEMATICS block ({without.Length} -> {with.Length} chars)", failures);

        // 2. The block is drawn from the code: every shape, finish and limit the validator enforces.
        var limits = LessonLimits.Default;
        var block = with[with.IndexOf("SCHEMATICS (", StringComparison.Ordinal)..];
        var vocab = LessonVocabulary.SchematicShapes.Concat(LessonVocabulary.SchematicFinishes).Where(v => !block.Contains($"\"{v}\"", StringComparison.Ordinal)).ToList();
        var numbers = new[] { $"at most {limits.MaxSchematics} simplified", $"{limits.MaxSchematicParts} parts", $"{limits.MaxSchematicShapes} shapes", $"-{limits.SchematicExtent} to {limits.SchematicExtent}", $"{limits.MaxSchematicNameChars} characters" }
            .Where(n => !block.Contains(n, StringComparison.Ordinal)).ToList();
        failures = Check(vocab.Count == 0 && numbers.Count == 0,
            $"every shape and finish, and every schematic limit, is in the block (missing: {(vocab.Count + numbers.Count == 0 ? "none" : string.Join(", ", vocab.Concat(numbers)))})", failures);
        failures = Check(block.Contains("BEFORE \"beats\"", StringComparison.Ordinal) && block.Contains("simplified drawing, not to scale", StringComparison.Ordinal)
                         && block.Contains("a schematic only when none does", StringComparison.Ordinal) && block.Contains("no animations", StringComparison.Ordinal),
            "the block asks for the header before the beats, a spoken 'simplified drawing', catalogue first, and no animations", failures);

        // 3. The recorded answer plans: kept with the check, refused without it.
        failures = Check(planned.Lesson.Outcome == LessonOutcome.Valid && planned.Lesson.Plan!.Schematics.Single().Id == "f35b" && planned.Attempts == 1,
            $"the F-35B answer with the check -> Valid in one attempt, the schematic kept ({planned.Lesson.Outcome})", failures);
        var (refused, _) = await PlanOnce(JetRequest(check: false), answer);
        failures = Check(refused.FirstAttemptIssues.Any(i => i.Code == LessonIssueCodes.SchematicUnavailable) && (refused.Lesson.Plan is null || refused.Lesson.Plan.Schematics.Count == 0),
            "the same answer sent unasked to a canvas without the check -> schematic.unavailable, never accepted", failures);

        // 4. Streamed: the header (schematic included) comes first, so every beat is released as it validates.
        var streaming = new FakeLlmClient(Streaming, answer, answer) { ChunkChars = 11 };
        var streamed = await new VisualPlanner(streaming).PlanStreamingAsync(JetRequest(check: true));
        failures = Check(streamed.Lesson.Outcome == LessonOutcome.Valid && streamed.ReleasedBeats == 6 && streamed.StreamNote is null && streaming.StreamCalls == 1,
            $"streamed in 11-char chunks -> all 6 beats released while streaming, no stream note ({streamed.ReleasedBeats} released{(streamed.StreamNote is null ? string.Empty : $", {streamed.StreamNote}")})", failures);

        return Summarize(failures, "PROMPT SCHEMATIC OK");
    }
}
