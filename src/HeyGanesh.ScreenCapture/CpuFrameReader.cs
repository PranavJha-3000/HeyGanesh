using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;

namespace HeyGanesh.ScreenCapture;

/// <summary>
/// Turns a GPU capture surface into CPU-readable pixels.
/// </summary>
/// <remarks>
/// Uses the projected <see cref="SoftwareBitmap.CreateCopyFromSurfaceAsync"/> rather than a
/// hand-rolled staging-texture copy. That keeps this module free of vtable-level COM interop
/// (<c>ID3D11DeviceContext::CopyResource</c> and friends), which is where hand-written capture code
/// usually goes wrong — a wrong vtable slot is an access violation, not a compile error.
/// </remarks>
internal static class CpuFrameReader
{
    /// <summary>Pixels below this value in every channel count as "black" for blank-frame detection.</summary>
    private const int BlackThreshold = 8;

    /// <summary>Copies a capture surface into a CPU-readable BGRA bitmap.</summary>
    internal static async Task<SoftwareBitmap> ReadAsync(IDirect3DSurface surface, CancellationToken cancellationToken)
    {
        var bitmap = await SoftwareBitmap.CreateCopyFromSurfaceAsync(surface).AsTask(cancellationToken).ConfigureAwait(false);

        if (bitmap.BitmapPixelFormat != BitmapPixelFormat.Bgra8)
        {
            // Capture frames are requested as B8G8R8A8UIntNormalized, so this should never happen;
            // if it ever does, the maths below (4 bytes per pixel) would silently be wrong.
            bitmap.Dispose();
            throw new InvalidOperationException(
                $"Expected a Bgra8 capture bitmap but received {bitmap.BitmapPixelFormat}.");
        }

        return bitmap;
    }

    /// <summary>
    /// Fraction of pixels that are not black, in the range 0..1.
    /// </summary>
    /// <remarks>
    /// A fresh WGC session can hand back a black or stale frame on some drivers. Rather than
    /// trusting the image, every capture measures it: a ratio near zero means "this frame has no
    /// content" and the caller should retry rather than upload a blank screenshot to a vision model.
    /// </remarks>
    internal static double ComputeNonBlackRatio(SoftwareBitmap bitmap)
    {
        var byteCount = checked(bitmap.PixelWidth * bitmap.PixelHeight * 4);

        // CryptographicBuffer is used purely as a byte[] <-> IBuffer bridge: both directions are
        // projected APIs, so no classic COM interop (IMemoryBufferByteAccess) is needed.
        var buffer = CryptographicBuffer.CreateFromByteArray(new byte[byteCount]);
        bitmap.CopyToBuffer(buffer);
        CryptographicBuffer.CopyToByteArray(buffer, out var pixels);

        var nonBlack = 0;

        for (var i = 0; i + 3 < pixels.Length; i += 4)
        {
            // BGRA8: any channel above the threshold means there is something to look at.
            if (pixels[i] > BlackThreshold || pixels[i + 1] > BlackThreshold || pixels[i + 2] > BlackThreshold)
            {
                nonBlack++;
            }
        }

        var totalPixels = bitmap.PixelWidth * bitmap.PixelHeight;
        return totalPixels == 0 ? 0 : (double)nonBlack / totalPixels;
    }
}
