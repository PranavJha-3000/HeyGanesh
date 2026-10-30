using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

// The screen-text rows (DEC-038). Both need an OCR language installed for this user, so both are
// local-only: CI's Windows image is not known to have one.
//   --assert-ocr  reads fixtures/ocr-words.png (three known lines, one per 100-px band from x=40) and
//                 checks the words, that every box is placed at the origin it was given, and the
//                 too-large refusal.
//   --read-text   one real capture with ReadText on; prints counts and timings only, never the text.
internal static class OcrRows
{
    private static readonly string[] Expected = { "Save draft", "Open settings", "Chain rule 42" };

    /// <summary>Drawn widths from the fixture generator (Segoe UI 32 px), for the box-size check.</summary>
    private static readonly int[] DrawnWidths = { 156, 213, 202 };

    public static async Task<int> AssertOcrAsync()
    {
        Console.WriteLine("=== Screen text: OCR fixture (local-only: needs an OCR language) ===");
        var failures = 0;
        var reader = new ScreenTextReader();
        failures = Check(reader.IsAvailable, $"an OCR language is installed ({reader.Language ?? "none"})", failures);
        if (!reader.IsAvailable)
        {
            Console.WriteLine("FAILED: no OCR language for this user; install one (Settings > Language) to run this row (exit 1)");
            return 1;
        }

        using var bitmap = await DecodeAsync(Path.Combine(AppContext.BaseDirectory, "fixtures", "ocr-words.png"));
        var origin = new PixelRect(1000, 2000, bitmap.PixelWidth, bitmap.PixelHeight);
        var read = await reader.ReadAsync(bitmap, origin, CancellationToken.None);
        Console.WriteLine($"  read {read.Lines.Count} line(s) in {read.Duration.TotalMilliseconds:F0} ms");

        failures = Check(read.Note is null, $"no note when OCR ran ({read.Note ?? "none"})", failures);
        failures = Check(read.Lines.Count == Expected.Length, $"three lines read ({read.Lines.Count})", failures);

        for (var i = 0; i < Expected.Length; i++)
        {
            var line = read.Lines.FirstOrDefault(l => string.Equals(Normalize(l.Text), Normalize(Expected[i]), StringComparison.OrdinalIgnoreCase));
            failures = Check(line is not null, $"'{Expected[i]}' read exactly (case and spacing aside)", failures);
            if (line is null)
            {
                continue;
            }

            var b = line.Box;
            var bandTop = origin.Y + (100 * i);
            failures = Check(b.Y >= bandTop && b.Bottom <= bandTop + 100, $"'{Expected[i]}' box {b} lies in its band y {bandTop}..{bandTop + 100}", failures);
            failures = Check(b.X >= origin.X + 30 && b.X <= origin.X + 60, $"'{Expected[i]}' box starts near x {origin.X + 40} (got {b.X})", failures);
            failures = Check(Math.Abs(b.Width - DrawnWidths[i]) <= DrawnWidths[i] * 0.25, $"'{Expected[i]}' box width {b.Width} within 25% of drawn {DrawnWidths[i]}", failures);
        }

        using var huge = new SoftwareBitmap(BitmapPixelFormat.Bgra8, 10_001, 8);
        var refused = await reader.ReadAsync(huge, new PixelRect(0, 0, 10_001, 8), CancellationToken.None);
        failures = Check(refused.Lines.Count == 0 && refused.Note?.Contains("larger than", StringComparison.Ordinal) == true,
            $"a frame over OCR's size limit is refused with a note ({refused.Note})", failures);

        Console.WriteLine(failures == 0 ? "OCR OK (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    public static async Task<int> ReadTextAsync()
    {
        Console.WriteLine("=== Screen text: one real capture with ReadText on (counts only, never the text) ===");
        var failures = 0;

        using (var plain = new ScreenCaptureService())
        {
            var frames = await plain.CaptureAllMonitorsAsync();
            failures = Check(frames.All(f => f.TextLines.Count == 0 && f.TextNote is null && f.TextDuration == TimeSpan.Zero),
                "ReadText off (the default): no frame carries text or a note", failures);
        }

        using var service = new ScreenCaptureService(new ScreenCaptureOptions { ReadText = true });
        var captures = await service.CaptureAllMonitorsAsync();
        for (var i = 0; i < captures.Count; i++)
        {
            var c = captures[i];
            var words = c.TextLines.Sum(l => l.Text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length);
            Console.WriteLine($"  [{i + 1}] {c.Monitor}: {c.TextLines.Count} lines, {words} words, OCR {c.TextDuration.TotalMilliseconds:F0} ms, " +
                              $"capture {c.Duration.TotalMilliseconds:F0} ms{(c.TextNote is null ? string.Empty : $", note: {c.TextNote}")}");
        }

        var first = captures[0];
        failures = Check(first.TextLines.Count > 0 || first.TextNote is not null, "the cursor's monitor was read (lines, or a note saying why not)", failures);
        failures = Check(captures.Skip(1).All(c => c.TextLines.Count == 0 && c.TextNote is null), "other monitors are not read", failures);
        var b = first.Monitor.Bounds;
        failures = Check(first.TextLines.All(l => l.Box.X >= b.X && l.Box.Y >= b.Y && l.Box.Right <= b.Right && l.Box.Bottom <= b.Bottom),
            $"every line box lies inside its monitor {b} (global physical pixels)", failures);

        Console.WriteLine(failures == 0 ? "READ TEXT OK (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    private static string Normalize(string text) => string.Join(' ', text.Split(' ', StringSplitOptions.RemoveEmptyEntries));

    private static int Check(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }

    private static async Task<SoftwareBitmap> DecodeAsync(string path)
    {
        using var stream = new InMemoryRandomAccessStream();
        var writer = new DataWriter(stream);
        writer.WriteBytes(await File.ReadAllBytesAsync(path));
        await writer.StoreAsync();
        await writer.FlushAsync();
        _ = writer.DetachStream();
        stream.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(stream);
        return await decoder.GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied);
    }
}
