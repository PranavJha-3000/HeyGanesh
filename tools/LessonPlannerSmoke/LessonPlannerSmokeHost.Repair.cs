using HeyGanesh.LessonPlanner;
using HeyGanesh.Providers.Core;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.Tools.LessonPlannerSmoke;

/// <summary>LP-5..LP-7: one repair call with the issues, faults pass through, follow-up context.</summary>
internal static partial class LessonPlannerSmokeHost
{
    private static async Task<int> RunAssertRepairAsync()
    {
        var failures = 0;
        var good = Fixture("chain-rule.json");
        var bad = good.Replace("\"target\": \"w4\"", "\"target\": \"w9\"", StringComparison.Ordinal);

        var fixedOnRetry = new FakeLlmClient(TextOnly, bad, good);
        var repaired = await new VisualPlanner(fixedOnRetry).PlanAsync(Request("chain rule", new ScreenGrounding("slide", Target, null, 0, 0)));
        var second = fixedOnRetry.Recorded.Count == 2 ? fixedOnRetry.Recorded[1] : null;
        failures = Check(repaired.Lesson.Outcome == LessonOutcome.Valid && repaired.Attempts == 2 && repaired.FirstAttemptIssues.Any(i => i.Code == LessonIssueCodes.RefTarget),
            "invalid first answer -> one repair call -> Valid, 2 attempts, first issues kept", failures);
        failures = Check(second is not null
                && second.Messages.Any(m => m.Role == "assistant" && m.Text == bad)
                && UserText(second).Contains(LessonIssueCodes.RefTarget, StringComparison.Ordinal)
                && FirstUserText(second) == FirstUserText(fixedOnRetry.Recorded[0]),
            "repair call carries the original question, the first answer, and the issue codes", failures);

        var stubborn = new FakeLlmClient(TextOnly, bad, bad, good);
        var gaveUp = await new VisualPlanner(stubborn).PlanAsync(Request("chain rule", new ScreenGrounding("slide", Target, null, 0, 0)));
        failures = Check(stubborn.Recorded.Count == 2 && gaveUp.Attempts == 2 && gaveUp.Lesson.Outcome == LessonOutcome.PartiallyDegraded,
            "two bad answers -> exactly 2 calls, the validated (degraded) plan is returned, no throw", failures);

        var prose = new FakeLlmClient(TextOnly, "I think the chain rule is neat.", "Still no JSON, sorry.");
        var noJson = await new VisualPlanner(prose).PlanAsync(Request("chain rule"));
        failures = Check(prose.Recorded.Count == 2 && noJson.Lesson.Outcome == LessonOutcome.Rejected,
            "prose with no JSON twice -> Rejected after one repair, no exception", failures);

        var worse = new FakeLlmClient(TextOnly, bad, "not json");
        var kept = await new VisualPlanner(worse).PlanAsync(Request("chain rule", new ScreenGrounding("slide", Target, null, 0, 0)));
        failures = Check(kept.Lesson.Outcome == LessonOutcome.PartiallyDegraded, "a repair answer worse than the first -> the better first plan is kept", failures);

        var empty = new FakeLlmClient(TextOnly, "   ", good);
        var blank = await new VisualPlanner(empty).PlanAsync(Request("chain rule", new ScreenGrounding("slide", Target, null, 0, 0)));
        failures = Check(empty.Recorded.Count == 2 && blank.Lesson.Outcome == LessonOutcome.Valid, "whitespace answer -> malformed -> repaired, never an empty lesson", failures);
        return Summarize(failures, "REPAIR OK");
    }

    private static async Task<int> RunAssertErrorsAsync()
    {
        var failures = 0;
        var auth = new FakeLlmClient(TextOnly) { Throw = new ProviderException(ProviderErrorKind.AuthFailed, "fake", "fake-model", "no key") };
        ProviderException? caught = null;
        try { await new VisualPlanner(auth).PlanAsync(Request("q")); }
        catch (ProviderException ex) { caught = ex; }
        failures = Check(caught is { Kind: ProviderErrorKind.AuthFailed }, "ProviderException (AuthFailed) passes through unchanged", failures);

        var hang = new FakeLlmClient(TextOnly) { Hang = true };
        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));
        var cancelled = false;
        try { await new VisualPlanner(hang).PlanAsync(Request("q"), cts.Token); }
        catch (OperationCanceledException) { cancelled = true; }
        failures = Check(cancelled, "cancellation propagates as OperationCanceledException", failures);

        var threw = false;
        try { await new VisualPlanner(new FakeLlmClient(TextOnly)).PlanAsync(Request("   ")); }
        catch (ArgumentException) { threw = true; }
        failures = Check(threw, "an empty question is a caller bug -> ArgumentException", failures);
        return Summarize(failures, "ERRORS OK");
    }

    private static async Task<int> RunAssertFollowupAsync()
    {
        var failures = 0;
        var previous = VisualPlanParser.Parse(Fixture("chain-rule.json")).Plan!;
        var client = new FakeLlmClient(TextOnly, Fixture("chain-rule.json"));
        await new VisualPlanner(client).PlanAsync(Request("can you explain it simpler?", previous: previous));
        var user = UserText(client.Recorded[0]);
        var block = user.IndexOf("PREVIOUS LESSON (UNTRUSTED", StringComparison.Ordinal);
        failures = Check(block > 0 && user.IndexOf("QUESTION", StringComparison.Ordinal) < block, "follow-up -> a PREVIOUS LESSON block, labelled untrusted, after the question", failures);
        failures = Check(user.Contains(previous.Title, StringComparison.Ordinal) && previous.Beats.All(b => user.Contains(b.Say, StringComparison.Ordinal)),
            "the block carries the previous title and every spoken sentence", failures);
        return Summarize(failures, "FOLLOWUP OK");
    }
}
