using System.Diagnostics;
using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;

namespace HeyGanesh.Vision;

/// <summary>
/// Provider/HTTP-level failure from an <see cref="IVisionClient"/> that is not an auth problem
/// (see <see cref="VisionAuthenticationException"/>) or rate limiting (see
/// <see cref="VisionRateLimitException"/>). Carries the HTTP status when known.
/// </summary>
public class VisionClientException : Exception
{
    public int? StatusCode { get; }
    public VisionClientException(string message, int? statusCode = null) : base(message) => StatusCode = statusCode;
}

/// <summary>A bearer-key problem (missing key / HTTP 401 / 403) -- surfaced separately from model refusals.</summary>
public sealed class VisionAuthenticationException : VisionClientException
{
    public VisionAuthenticationException(string message) : base(message, null) { }
}

/// <summary>Provider rate limit (HTTP 429).</summary>
public sealed class VisionRateLimitException : VisionClientException
{
    public VisionRateLimitException(string message) : base(message, 429) { }
}

/// <summary>
/// Public API of the Vision module. Given a <see cref="MonitorCapture"/> and a question, it drives
/// <see cref="IVisionClient"/>, turns the model's fuzzy answer into a structured <see cref="VisionResult"/>
/// (with a confidence signal), and maps coordinates into the capture's physical pixel space.
/// </summary>
/// <remarks>
/// Policy that lives here: sampling/count for the consistency signal, parse-failure retry, the blank-frame
/// short-circuit, and coordinate back-mapping. The transport is entirely behind <see cref="IVisionClient"/>,
/// so swapping DeepSeek for a proxy or a fake is one constructor argument (see DEC-007). This file
/// deliberately carries <c>using HeyGanesh.Abstractions; using HeyGanesh.ScreenCapture;</c> so the composition link is grep-verifiable.
/// </remarks>
public sealed class VisionService : IDisposable
{
    /// <summary>
    /// Non-black fraction below which a capture is treated as blank. Chosen at the capture module's own
    /// exclusion-proof noise floor (0.6%) plus margin: a real desktop frame is orders of magnitude above
    /// this; Secure Desktop / elevated / RDP-empty frames sit at or below it.
    /// </summary>
    private const double BlankFrameThreshold = 0.005;

    private readonly IVisionClient _client;
    private readonly VisionOptions _options;
    private readonly bool _ownsClient;

    /// <summary>
    /// Creates a service. If <paramref name="client"/> is null a <see cref="DeepSeekVisionClient"/>
    /// (reading <c>DEEPSEEK_API_KEY</c> from the environment) is used; pass another
    /// <see cref="IVisionClient"/> to redirect to a proxy or a test double.
    /// </summary>
    public VisionService(IVisionClient? client = null, VisionOptions? options = null)
    {
        _options = options ?? VisionOptions.Default;
        if (client is null)
        {
            _client = new DeepSeekVisionClient(_options);
            _ownsClient = true;
        }
        else
        {
            _client = client;
        }
    }

    /// <summary>
    /// Locates <paramref name="question"/> in <paramref name="capture"/> and returns a structured
    /// <see cref="VisionResult"/> carrying coordinates in the capture's physical pixel space plus a
    /// confidence signal.
    /// </summary>
    public async Task<VisionResult> LocateAsync(
        MonitorCapture capture,
        string question,
        CancellationToken ct = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(question);
        if (capture.OutputWidth <= 0 || capture.OutputHeight <= 0)
            throw new ArgumentException("Capture must have positive OutputWidth/Height.", nameof(capture));
        if (capture.Jpeg is null || capture.Jpeg.Length == 0)
            throw new ArgumentException("Capture has no JPEG bytes.", nameof(capture));

        var started = Stopwatch.GetTimestamp();

        // Blank-frame guard: surface Secure Desktop / elevated / RDP empties as a distinct failure, NOT as
        // a low-confidence miss. NonBlackPixelRatio is NaN when ScreenCaptureOptions disabled the check.
        if (!double.IsNaN(capture.NonBlackPixelRatio) && capture.NonBlackPixelRatio < BlankFrameThreshold)
            return BlankFrameResult(capture, Stopwatch.GetElapsedTime(started));

        var request = new VisionRequest
        {
            Model = _options.ModelId,
            UserQuestion = question,
            Image = capture.Jpeg,
            ImageWidth = capture.OutputWidth,
            ImageHeight = capture.OutputHeight,
            ImageDetail = _options.ImageDetail,
            MaxTokens = _options.MaxTokens,
            Temperature = _options.Temperature,
            TopP = _options.TopP,
            UseJsonMode = true,
        };

        var samples = new List<VisionSample>(_options.MaxSamples);
        var promptTokens = 0;
        var completionTokens = 0;

        // Draw MaxSamples (each with an internal parse-failure retry). VisionService owns the retry-for-
        // consistency policy; the client is a dumb single-call HTTP seam.
        for (int i = 0; i < _options.MaxSamples; i++)
        {
            ct.ThrowIfCancellationRequested();
            var sample = await SendSampleAsync(request, ct).ConfigureAwait(false);
            samples.Add(sample);
            promptTokens += sample.PromptTokens;
            completionTokens += sample.CompletionTokens;
        }

        return Reconcile(capture, samples, promptTokens, completionTokens, _options, Stopwatch.GetElapsedTime(started));
    }

        private async Task<VisionSample> SendSampleAsync(VisionRequest request, CancellationToken ct)
    {
        // One sample may be re-issued up to ParseRetryCount times: on a parse failure (JSON-mode empty
        // content / malformed output) or on a transient transport error. Auth/rate-limit fail is fatal.
        for (int attempt = 0; attempt <= _options.ParseRetryCount; attempt++)
        {
            try
            {
                var sw = Stopwatch.GetTimestamp();
                var response = await _client.AskAsync(request, ct).ConfigureAwait(false);
                var latency = Stopwatch.GetElapsedTime(sw);

                var sample = VisionResponseParser.Parse(
                    response.Content, latency, response.PromptTokens, response.CompletionTokens);

                if (sample.Parsed || attempt == _options.ParseRetryCount) return sample;

                // JSON-mode empty/malformed: re-ask and reparse.
                await Task.Delay(TimeSpan.FromMilliseconds(150 * (attempt + 1)), ct).ConfigureAwait(false);
            }
            catch (VisionAuthenticationException) { throw; }
            catch (VisionRateLimitException) { throw; }
            catch (OperationCanceledException) { throw; }
            catch (Exception) when (attempt < _options.ParseRetryCount)
            {
                // Transient transport error (5xx, network, timeout): retry once after a short back-off.
                await Task.Delay(TimeSpan.FromMilliseconds(200 * (attempt + 1)), ct).ConfigureAwait(false);
            }
        }

        // Unreachable for normal control flow; the safety-net sample keeps the method total.
        return new VisionSample
        {
            Parsed = false,
            ParseError = "exhausted retries without a response",
            Latency = TimeSpan.Zero,
        };
    }

    private static VisionResult Reconcile(
        MonitorCapture capture,
        List<VisionSample> samples,
        int promptTokens,
        int completionTokens,
        VisionOptions options,
        TimeSpan totalLatency)
    {
        var parsedFound = samples.Where(s => s.Parsed && s.Found).ToList();
        var description = parsedFound.FirstOrDefault()?.Element;

        if (parsedFound.Count == 0)
        {
            // Distinguish "model saw the frame but found nothing" from "every sample was unparseable."
            var anyParsed = samples.Any(s => s.Parsed);
            var reason = anyParsed
                ? "the model inspected the image but could not locate the requested element."
                : $"failed to parse a structured answer from {samples.Count} sample(s).";

            return new VisionResult
            {
                Monitor = capture.Monitor,
                Found = false,
                Description = description,
                SampleCount = samples.Count,
                Samples = samples,
                ModelId = options.ModelId,
                ImageDetail = options.ImageDetail,
                Latency = totalLatency,
                PromptTokens = promptTokens,
                CompletionTokens = completionTokens,
                Confidence = ConfidenceLevel.Low,
                FailureReason = reason,
            };
        }

        var primary = parsedFound[0];
        var consistency = ComputeConsistency(parsedFound, out _);
        var score = BlendConfidence(primary.Confidence, consistency);

        // Coordinate mapping: 0..1000 grid -> JPEG px -> source (monitor) px -> screen px.
        var sourceBox = CoordinateMapper.GridToSourceBox(
            primary.X1!.Value, primary.Y1!.Value, primary.X2!.Value, primary.Y2!.Value,
            capture.OutputWidth, capture.OutputHeight, capture.SourceWidth, capture.SourceHeight);

        var boxOnScreen = CoordinateMapper.SourceToScreenBox(sourceBox, capture.Monitor.Bounds);
        var centerOnScreen = CoordinateMapper.BoxCenter(boxOnScreen);

        // Normalized box re-derived from the (corner-normalised, in-range) source box for consistency.
                var normalized = new NormalizedBox(
            Math.Clamp(sourceBox.X / (float)capture.SourceWidth, 0f, 1f),
            Math.Clamp(sourceBox.Y / (float)capture.SourceHeight, 0f, 1f),
            Math.Clamp(sourceBox.Width / (float)capture.SourceWidth, 0f, 1f),
            Math.Clamp(sourceBox.Height / (float)capture.SourceHeight, 0f, 1f));

        return new VisionResult
        {
            Monitor = capture.Monitor,
            Found = true,
            Description = primary.Element,
            Normalized = normalized,
            Rectangle = sourceBox,
            BoxOnScreen = boxOnScreen,
            CenterOnScreen = centerOnScreen,
            Confidence = score >= 0.8f ? ConfidenceLevel.High : score >= 0.5f ? ConfidenceLevel.Medium : ConfidenceLevel.Low,
            RawConfidence = primary.Confidence,
            Consistency = consistency,
            SampleCount = samples.Count,
            Samples = samples,
            ModelId = options.ModelId,
            ImageDetail = options.ImageDetail,
            Latency = totalLatency,
            PromptTokens = promptTokens,
            CompletionTokens = completionTokens,
        };
    }

        private static float? ComputeConsistency(IReadOnlyList<VisionSample> found, out int primaryIndex)
    {
        primaryIndex = 0;
        if (found.Count < 2) return null; // No signal available from a single answer.

        // Consistency = 1 - mean pairwise centre distance, normalised by the grid diagonal
        // (sqrt(1000^2 + 1000^2)). Identical answers -> 1.0; total disagreement -> 0.0.
        const double diag = 1414.2135623730951;
        var centers = found.Select(s =>
            ((s.X1!.Value + s.X2!.Value) / 2.0, (s.Y1!.Value + s.Y2!.Value) / 2.0)).ToArray();

        double sum = 0;
        int pairs = 0;
        for (int i = 0; i < centers.Length; i++)
        for (int j = i + 1; j < centers.Length; j++)
        {
            var dist = Math.Sqrt(Math.Pow(centers[i].Item1 - centers[j].Item1, 2) +
                                 Math.Pow(centers[i].Item2 - centers[j].Item2, 2));
            sum += dist / diag;
            pairs++;
        }

        return (float?)Math.Clamp(1.0 - sum / pairs, 0.0, 1.0);
    }

    private static float BlendConfidence(float? modelConfidence, float? consistency)
    {
        if (modelConfidence.HasValue && consistency.HasValue)
            return 0.5f * modelConfidence.Value + 0.5f * consistency.Value;
        // Fall back to whichever signal survived a single sample or a model that won't self-rate.
        return modelConfidence ?? consistency ?? 0.5f;
    }

    private static VisionResult BlankFrameResult(MonitorCapture capture, TimeSpan latency) => new()
    {
        Monitor = capture.Monitor,
        Found = false,
        SampleCount = 0,
        WasBlankFrame = true,
        FailureReason = "capture frame contained no detectable content (NonBlackPixelRatio below threshold; " +
                        "possible Secure Desktop / elevated / RDP empty capture).",
        Latency = latency,
    };

    public void Dispose()
    {
        if (_ownsClient && _client is IDisposable d) d.Dispose();
    }
}
