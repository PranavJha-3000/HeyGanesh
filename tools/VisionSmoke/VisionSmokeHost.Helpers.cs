using HeyGanesh.ScreenCapture;
using HeyGanesh.Vision;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;
using HeyGanesh.Abstractions;

namespace HeyGanesh.Tools.VisionSmoke;

/// <summary>
/// Offline fake used when DEEPSEEK_API_KEY is absent: returns a fixed, well-formed box so the
/// parse -> map -> annotate path can be exercised without network access or a key.
/// </summary>
internal sealed class FakeSuccessVisionClient : IVisionClient
{
    public Task<VisionRawResponse> AskAsync(VisionRequest request, CancellationToken cancellationToken)
    {
        var json = """{"found": true, "element": "offline probe", "box": {"x1": 240, "y1": 120, "x2": 360, "y2": 220}, "confidence": 0.86, "note": "fake"}""";
        return Task.FromResult(new VisionRawResponse(json, "offline-fake", 10, 20, TimeSpan.FromMilliseconds(5)));
    }
}

internal static partial class VisionSmokeHost
{
    private static async Task<int> RunOfflineAsync(VisionService vision, HostOptions options)
    {
        var jpeg = await File.ReadAllBytesAsync(options.ImagePath!);
        var (w, h) = await JpegDimensionsAsync(jpeg);
        // In offline mode the JPEG is both the source and the downscaled frame: treat it as a
        // 1:1 capture so the coordinate mapper back-computes onto the same pixels.
        var monitor = new MonitorInfo(IntPtr.Zero, "file", new PixelRect(0, 0, w, h), IsPrimary: true);
        var VarMonitorCapture = new MonitorCapture(
            Monitor: monitor,
            SourceWidth: w,
            SourceHeight: h,
            OutputWidth: w,
            OutputHeight: h,
            Jpeg: jpeg,
            NonBlackPixelRatio: double.NaN,
            FramesObserved: 0,
            Duration: TimeSpan.Zero);
        IVisionClient client = options.Fake
            ? new FakeSuccessVisionClient()
            : null!;
        using VisionService offlineVision = client is null ? vision : new(client: client);
        // Explicit fake request still prints the missing-key state honestly: the header above says
        // MISSING, and the result below shows model=offline-fake. A live call never impersonates a fake.
        return await RunVisionAsync(offlineVision, VarMonitorCapture, options.Question, options.OutDir);
    }

    private static async Task<int> RunVisionAsync(VisionService vision, MonitorCapture capture, string question, string outDir)
    {
        VisionResult result;
        try
        {
            result = await vision.LocateAsync(capture, question);
        }
        catch (VisionAuthenticationException aex)
        {
            Console.WriteLine($"KEY ERROR: {aex.Message}");
            return 12;
        }
        catch (VisionClientException cex)
        {
            Console.WriteLine($"API ERROR: {cex.Message}");
            return 3;
        }

        PrintResult(result, question);

        // Visual hit-check artifact: overlay the reported box on the JPEG the model actually saw.
        if (result.Found && result.Rectangle is CaptureBox box)
        {
            var annotated = await Annotator.AnnotateAsync(capture.Jpeg, box, capture.SourceWidth, capture.SourceHeight);
            var annotatedPath = Path.Combine(outDir, "annotated.jpg");
            await File.WriteAllBytesAsync(annotatedPath, annotated);
            Console.WriteLine($"wrote {annotatedPath} (crosshair + box overlaid on the JPEG)");
        }
        else
        {
            Console.WriteLine("(no box to annotate -- nothing found in the image)");
        }

        return result.Failed ? 1 : 0;
    }

    private static void PrintResult(VisionResult r, string question)
    {
        Console.WriteLine("=== Vision result ===");
        Console.WriteLine($"  question    : {question}");
        if (r.Failed) Console.WriteLine($"  FAILURE     : {r.FailureReason}");
        if (r.WasBlankFrame) Console.WriteLine("  blank frame : true (Secure Desktop / elevated / RDP empty capture)");
        Console.WriteLine($"  found       : {r.Found}");
        if (r.Found)
        {
            Console.WriteLine($"  description : {r.Description}");
            Console.WriteLine($"  normalized  : {r.Normalized?.ToString() ?? "n/a"}");
            Console.WriteLine($"  rectangle   : {r.Rectangle?.ToString() ?? "n/a"} (source-capture px, monitor-local)");
            Console.WriteLine($"  boxOnScreen : {r.BoxOnScreen?.ToString() ?? "n/a"} (global physical px)");
            Console.WriteLine($"  center      : {r.CenterOnScreen?.ToString() ?? "n/a"} (global physical px)");
        }
        Console.WriteLine($"  confidence  : {r.Confidence}  (model={r.RawConfidence}, consistency={r.Consistency}, samples={r.SampleCount})");
        Console.WriteLine($"  model       : {r.ModelId}  detail={r.ImageDetail}");
        Console.WriteLine($"  timings     : latency={r.Latency.TotalMilliseconds:F0}ms  promptTok={r.PromptTokens} completionTok={r.CompletionTokens}");
        Console.WriteLine("  samples:");
        for (int i = 0; i < r.Samples.Count; i++)
        {
            var s = r.Samples[i];
            Console.WriteLine($"    [{i}] parsed={s.Parsed} found={s.Found} box=({s.X1},{s.Y1},{s.X2},{s.Y2}) conf={s.Confidence} err={s.ParseError}");
            var raw = s.RawText.Length > 160 ? s.RawText.AsSpan(0, 160).ToString() + "…" : s.RawText;
            Console.WriteLine($"        raw: {raw}");
        }
    }

    private static async Task<(int Width, int Height)> JpegDimensionsAsync(byte[] jpeg)
    {
        using var stream = new InMemoryRandomAccessStream();
        await stream.WriteAsync(CryptographicBuffer.CreateFromByteArray(jpeg)).AsTask();
        stream.Seek(0);
        var decoder = await BitmapDecoder.CreateAsync(stream).AsTask();
        return ((int)decoder.PixelWidth, (int)decoder.PixelHeight);
    }
}
