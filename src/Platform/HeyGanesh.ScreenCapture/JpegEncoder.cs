using Windows.Foundation;
using Windows.Graphics.Imaging;
using Windows.Storage.Streams;

namespace HeyGanesh.ScreenCapture;

/// <summary>
/// Downscales and JPEG-encodes a capture for upload.
/// </summary>
/// <remarks>
/// Uses the projected <c>BitmapEncoder</c> (WIC underneath) rather than a NuGet image library:
/// the same dependency-free stance as the rest of the project, and it gives both controls this
/// module needs — <c>ImageQuality</c> for the JPEG quality factor and <c>BitmapTransform</c> for
/// the downscale, applied during encoding so no separate scaler pass is required.
/// </remarks>
internal static class JpegEncoder
{
    /// <summary>Keys the encoder's quality property. Must be <c>Single</c>; a double is rejected.</summary>
    private const string ImageQualityKey = "ImageQuality";

    /// <summary>
    /// Encodes <paramref name="source"/> as JPEG, scaled so its largest dimension is at most
    /// <paramref name="maxDimension"/>.
    /// </summary>
    internal static async Task<(byte[] Bytes, int Width, int Height)> EncodeAsync(
        SoftwareBitmap source,
        int maxDimension,
        float quality,
        CancellationToken cancellationToken)
    {
        var (width, height) = ScaleToFit(source.PixelWidth, source.PixelHeight, maxDimension);

        var properties = new BitmapPropertySet
        {
            { ImageQualityKey, new BitmapTypedValue(quality, PropertyType.Single) },
        };

        try
        {
            return await EncodeCoreAsync(source, properties, width, height, BitmapInterpolationMode.Fant, cancellationToken)
                .ConfigureAwait(false);
        }
        catch (Exception) when (width != source.PixelWidth)
        {
            // Fant is the highest-quality scaler but is not supported by every encoder
            // configuration. Fall back rather than failing the capture over interpolation quality.
            return await EncodeCoreAsync(source, properties, width, height, BitmapInterpolationMode.Linear, cancellationToken)
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// Largest-dimension cap, preserving aspect ratio, never upscaling.
    /// </summary>
    internal static (int Width, int Height) ScaleToFit(int width, int height, int maxDimension)
    {
        if (maxDimension <= 0 || (width <= maxDimension && height <= maxDimension))
        {
            return (width, height);
        }

        var scale = (double)maxDimension / Math.Max(width, height);

        return (
            Math.Max(1, (int)Math.Round(width * scale)),
            Math.Max(1, (int)Math.Round(height * scale)));
    }

    private static async Task<(byte[] Bytes, int Width, int Height)> EncodeCoreAsync(
        SoftwareBitmap source,
        BitmapPropertySet properties,
        int width,
        int height,
        BitmapInterpolationMode interpolationMode,
        CancellationToken cancellationToken)
    {
        using var stream = new InMemoryRandomAccessStream();

        var encoder = await BitmapEncoder
            .CreateAsync(BitmapEncoder.JpegEncoderId, stream, properties)
            .AsTask(cancellationToken)
            .ConfigureAwait(false);

        if (width != source.PixelWidth || height != source.PixelHeight)
        {
            encoder.BitmapTransform.ScaledWidth = (uint)width;
            encoder.BitmapTransform.ScaledHeight = (uint)height;
            encoder.BitmapTransform.InterpolationMode = interpolationMode;
        }

        encoder.SetSoftwareBitmap(source);

        await encoder.FlushAsync().AsTask(cancellationToken).ConfigureAwait(false);

        return (await ReadStreamAsync(stream, checked((uint)stream.Size), cancellationToken).ConfigureAwait(false), width, height);
    }

    private static async Task<byte[]> ReadStreamAsync(InMemoryRandomAccessStream stream, uint size, CancellationToken cancellationToken)
    {
        using var reader = new DataReader(stream.GetInputStreamAt(0));

        await reader.LoadAsync(size).AsTask(cancellationToken).ConfigureAwait(false);

        var bytes = new byte[size];
        reader.ReadBytes(bytes);

        return bytes;
    }
}
