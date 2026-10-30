using System.Diagnostics;
using HeyGanesh.Providers.Core;
using HeyGanesh.VisualLessonEngine;

namespace HeyGanesh.LessonPlanner;

// The streaming half of the planner (DEC-041). The lesson is asked for exactly as PlanAsync asks for it,
// but read as it arrives: each time a beat's closing brace lands, "header + beats so far" goes through
// the same parser and validator, and a beat is released only once that prefix validates. Validation is
// never skipped — a released beat is the validator's own output — it just happens beat by beat.

public sealed partial class VisualPlanner
{
    /// <summary>
    /// Plans like <see cref="PlanAsync"/>, but streams when the client advertises
    /// <see cref="ProviderCapability.Streaming"/> and calls <paramref name="onBeat"/> for each beat as
    /// soon as it is safe to teach. Without streaming it is exactly <see cref="PlanAsync"/>.
    /// </summary>
    /// <remarks>
    /// <para>Rules: nothing is released until the header and the first beat validate. While nothing has
    /// been released, the finished answer goes through the usual repair loop. Once a beat is out there is
    /// no repair (the user has already seen it): the lesson continues, or stops after the last released
    /// beat. A provider fault mid-stream returns the released beats with <see cref="PlanResult.StreamNote"/>;
    /// before the first beat it propagates, as in PlanAsync. Cancellation always propagates.</para>
    /// <para><paramref name="onBeat"/> runs on the reading loop, so it must return quickly (6b hands the
    /// beat to the player through a queue).</para>
    /// </remarks>
    public async Task<PlanResult> PlanStreamingAsync(
        PlanRequest request,
        Action<StreamedBeat>? onBeat = null,
        CancellationToken cancellationToken = default)
    {
        var prepared = Prepare(request);
        if (!_client.Capabilities.HasFlag(ProviderCapability.Streaming))
        {
            return await RunAttemptsAsync(prepared, firstAnswer: null, cancellationToken).ConfigureAwait(false);
        }

        var scanner = new PlanStreamScanner();
        var clock = Stopwatch.StartNew();
        LessonValidationResult? lastPrefix = null;
        var released = 0;
        TimeSpan? firstBeat = null;
        int input = 0, output = 0;

        try
        {
            var stream = _client.StreamAsync(new LlmRequest
            {
                SystemPrompt = prepared.System,
                Messages = new[] { prepared.User },
                MaxOutputTokens = _options.MaxOutputTokens,
                Temperature = _options.Temperature,
            }, cancellationToken);

            await foreach (var chunk in stream.WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                input = chunk.InputTokens ?? input;
                output = chunk.OutputTokens ?? output;
                if (string.IsNullOrEmpty(chunk.TextDelta) || scanner.Append(chunk.TextDelta).Count == 0)
                {
                    continue;
                }

                // A prefix that does not validate whole, or that the validator shortened (a limit), is
                // simply not released yet: a later, longer prefix may be. Released beats never change.
                var prefix = _validator.Validate(VisualPlanParser.Parse(scanner.PrefixJson(scanner.Beats.Count)!), prepared.Context);
                if (prefix.Outcome == LessonOutcome.Rejected || prefix.Plan is null || prefix.Plan.Beats.Count != scanner.Beats.Count)
                {
                    continue;
                }

                lastPrefix = prefix;
                for (; released < prefix.Plan.Beats.Count; released++)
                {
                    firstBeat ??= clock.Elapsed;
                    onBeat?.Invoke(new StreamedBeat(released, prefix.Plan.Beats[released], prefix, clock.Elapsed));
                }
            }
        }
        catch (ProviderException ex) when (released > 0)
        {
            return new PlanResult(lastPrefix!, 1, prepared.Image is not null, prepared.ImageNote, _client.ModelId, input, output,
                clock.Elapsed, lastPrefix!.Issues, null, released, firstBeat,
                $"{ex.Kind}: the stream failed after {released} beat(s); the lesson stops there");
        }

        var answer = new LlmResponse
        {
            Text = scanner.Text,
            ModelId = _client.ModelId,
            InputTokens = input,
            OutputTokens = output,
            Latency = clock.Elapsed,
        };

        if (released == 0)
        {
            // Nothing was taught yet, so this is PlanAsync with the stream as its first answer: repair included.
            var whole = await RunAttemptsAsync(prepared, answer, cancellationToken).ConfigureAwait(false);
            return whole with { StreamNote = "no beat validated while streaming; planned as a whole" };
        }

        var final = _validator.Validate(VisualPlanParser.Parse(scanner.Text), prepared.Context);
        string? note = null;
        var agrees = final.Plan is { } plan && plan.Beats.Count >= released
            && Enumerable.Range(0, released).All(i => StreamedBeat.Fingerprint(plan.Beats[i]) == StreamedBeat.Fingerprint(lastPrefix!.Plan!.Beats[i]));
        if (!agrees)
        {
            // Never expected (the prefix rule is checked over every golden lesson, LP-11), but if the whole
            // plan disowns a beat the user already saw, the lesson stops after what was shown.
            note = "the finished plan disagrees with the beats already released; the lesson stops after them";
            final = lastPrefix!;
        }

        return new PlanResult(final, 1, prepared.Image is not null, prepared.ImageNote, _client.ModelId, input, output,
            clock.Elapsed, final.Issues, null, released, firstBeat, note);
    }
}
