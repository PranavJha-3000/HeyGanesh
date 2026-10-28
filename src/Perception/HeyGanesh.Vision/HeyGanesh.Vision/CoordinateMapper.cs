using HeyGanesh.ScreenCapture;

namespace HeyGanesh.Vision;

/// <summary>
/// Pure, allocation-light math that translates between the coordinate planes Vision exposes.
/// </summary>
/// <remarks>
/// <para>Layout of the planes (all linear, uniform scale):</para>
/// <list type="number">
///   <item>model grid: 0..1000 over the JPEG -- what the model emits;</item>
///   <item>JPEG image: OutputWidth x OutputHeight -- the pixels the model saw;</item>
///   <item>source capture: SourceWidth x SourceHeight -- the monitor's true physical resolution;</item>
///   <item>screen (global physical px): source px + Monitor.Bounds origin -- ready for SetCursorPos.</item>
/// </list>
/// The JPEG is a uniform downscale of the source, so a single scale factor links image and source;
/// that is the only conversion that must be exact, so it is centralised here rather than recomputed.
/// </remarks>
internal static class CoordinateMapper
{
    private const int GridMax = 1000;

    /// <summary>Converts a model grid coordinate (0..1000) to JPEG pixel space.</summary>
    public static PointI GridToImage(int gridX, int gridY, int imageWidth, int imageHeight) =>
        new(
            Clamp(gridX * imageWidth / GridMax, 0, imageWidth - 1),
            Clamp(gridY * imageHeight / GridMax, 0, imageHeight - 1)
        );

    /// <summary>Converts JPEG pixel space to the source capture's physical-pixel space (uniform scale).</summary>
    public static int ImageToSource(int imagePx, int imageDim, int sourceDim)
    {
        if (imageDim <= 0) return 0;
        return (int)Math.Round(imagePx * (double)sourceDim / imageDim);
    }

    /// <summary>Converts the whole grid box to source-capture physical pixels.</summary>
    public static CaptureBox GridToSourceBox(int x1, int y1, int x2, int y2, int imageWidth, int imageHeight, int sourceWidth, int sourceHeight)
    {
        var ix1 = GridToImage(x1, y1, imageWidth, imageHeight);
        var ix2 = GridToImage(x2, y2, imageWidth, imageHeight);
        var sx1 = ImageToSource(ix1.X, imageWidth, sourceWidth);
        var sy1 = ImageToSource(ix1.Y, imageHeight, sourceHeight);
        var sx2 = ImageToSource(ix2.X, imageWidth, sourceWidth);
        var sy2 = ImageToSource(ix2.Y, imageHeight, sourceHeight);

        // Normalise so width/height are non-negative in case the model swapped corners.
        var (left, right) = sx1 <= sx2 ? (sx1, sx2) : (sx2, sx1);
        var (top, bottom) = sy1 <= sy2 ? (sy1, sy2) : (sy2, sy1);

        return new CaptureBox(left, top, right - left, bottom - top);
    }

    /// <summary>
    /// Translates a source-space box into global physical-screen pixel space by applying the monitor
    /// origin, clamping to the monitor's bounds so an over-shooting model cannot point off-screen.
    /// </summary>
    public static PixelRect SourceToScreenBox(CaptureBox source, PixelRect monitorBounds)
    {
        var x = Clamp(source.X, 0, monitorBounds.Right);
        var y = Clamp(source.Y, 0, monitorBounds.Bottom);
        var w = Math.Max(0, Math.Min(source.Width, monitorBounds.Right - x));
        var h = Math.Max(0, Math.Min(source.Height, monitorBounds.Bottom - y));

        return new PixelRect(x + monitorBounds.X, y + monitorBounds.Y, w, h);
    }

    /// <summary>Center of a screen-space box, as integer global physical pixels.</summary>
    public static PointI BoxCenter(PixelRect box) =>
        new(box.X + box.Width / 2, box.Y + box.Height / 2);

    /// <summary>Converts a model grid point straight to global screen pixels (used for the center marker).</summary>
    public static PointI GridToScreen(int gridX, int gridY, int imageWidth, int imageHeight, int sourceWidth, int sourceHeight, PixelRect monitorBounds)
    {
        var img = GridToImage(gridX, gridY, imageWidth, imageHeight);
        var sx = ImageToSource(img.X, imageWidth, sourceWidth) + monitorBounds.X;
        var sy = ImageToSource(img.Y, imageHeight, sourceHeight) + monitorBounds.Y;
        return new PointI(sx, sy);
    }

    private static int Clamp(int value, int min, int max) => value < min ? min : value > max ? max : value;
}
