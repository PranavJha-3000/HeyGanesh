using HeyGanesh.Abstractions;
using Windows.Graphics.Imaging;
using Windows.Media.Ocr;

namespace HeyGanesh.ScreenCapture;

/// <summary>What one OCR pass read: the lines, or why there are none.</summary>
/// <param name="Lines">Each recognised line with its box in global physical pixels.</param>
/// <param name="Note">Null when OCR ran; otherwise why it did not (no language installed, or a failure).</param>
/// <param name="Language">The recogniser's language tag, when one exists.</param>
public sealed record ScreenTextRead(IReadOnlyList<ScreenTextLine> Lines, string? Note, string? Language, TimeSpan Duration);

/// <summary>
/// Reads the text on a captured frame with <c>Windows.Media.Ocr</c>, the recogniser built into Windows
/// (DEC-038). Local only: no network, no key, no NuGet — the same WinRT surface the capture already
/// uses. It runs on the full-size frame because the 1280-px upload frame loses about 40% of the lines
/// (spike, 2026-10-30: 110 vs 68 lines on a 2560×1600 screen).
/// </summary>
/// <remarks>
/// The recogniser comes from the user's profile languages, once per reader. When none is installed
/// the reader returns no lines and says so — never an exception — so a machine without an OCR
/// language captures exactly as before. Called by <see cref="ScreenCaptureService"/> under its capture
/// gate, so one reader is never used by two captures at once.
/// </remarks>
public sealed class ScreenTextReader
{
    private readonly OcrEngine? _engine;

    public ScreenTextReader()
    {
        _engine = OcrEngine.TryCreateFromUserProfileLanguages();
    }

    /// <summary>Whether an OCR language is installed for this user.</summary>
    public bool IsAvailable => _engine is not null;

    /// <summary>The recogniser's language tag ("en-US"), or null when none is installed.</summary>
    public string? Language => _engine?.RecognizerLanguage.LanguageTag;

    /// <summary>
    /// Reads <paramref name="bitmap"/> (Bgra8 or Gray8, at most <see cref="OcrEngine.MaxImageDimension"/>
    /// on each side) and places every line at <paramref name="origin"/>, the frame's top-left corner in
    /// global physical pixels (the monitor's bounds for a capture). Cancellation propagates; any other
    /// recogniser failure becomes a note.
    /// </summary>
    public async Task<ScreenTextRead> ReadAsync(SoftwareBitmap bitmap, PixelRect origin, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(bitmap);
        var started = System.Diagnostics.Stopwatch.GetTimestamp();

        if (_engine is null)
        {
            return new(Array.Empty<ScreenTextLine>(), "no OCR language is installed for this user", null, TimeSpan.Zero);
        }

        if (bitmap.PixelWidth > OcrEngine.MaxImageDimension || bitmap.PixelHeight > OcrEngine.MaxImageDimension)
        {
            return new(Array.Empty<ScreenTextLine>(),
                $"frame {bitmap.PixelWidth}x{bitmap.PixelHeight} is larger than OCR's {OcrEngine.MaxImageDimension} px limit",
                Language, TimeSpan.Zero);
        }

        OcrResult result;
        try
        {
            result = await _engine.RecognizeAsync(bitmap).AsTask(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The recogniser is a convenience on top of the capture: its failure must never fail the frame.
            return new(Array.Empty<ScreenTextLine>(), $"OCR failed: {ex.GetType().Name}: {ex.Message}", Language,
                System.Diagnostics.Stopwatch.GetElapsedTime(started));
        }

        var lines = new List<ScreenTextLine>(result.Lines.Count);
        foreach (var line in result.Lines)
        {
            if (line.Words.Count == 0 || string.IsNullOrWhiteSpace(line.Text))
            {
                continue;
            }

            // A line's box is the union of its words' boxes; OCR reports them in frame pixels.
            double left = double.MaxValue, top = double.MaxValue, right = double.MinValue, bottom = double.MinValue;
            foreach (var word in line.Words)
            {
                var r = word.BoundingRect;
                left = Math.Min(left, r.X);
                top = Math.Min(top, r.Y);
                right = Math.Max(right, r.X + r.Width);
                bottom = Math.Max(bottom, r.Y + r.Height);
            }

            var x = (int)Math.Floor(left);
            var y = (int)Math.Floor(top);
            lines.Add(new ScreenTextLine(
                line.Text,
                new PixelRect(origin.X + x, origin.Y + y, (int)Math.Ceiling(right) - x, (int)Math.Ceiling(bottom) - y)));
        }

        return new(lines, null, Language, System.Diagnostics.Stopwatch.GetElapsedTime(started));
    }
}
