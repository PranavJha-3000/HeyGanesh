using Windows.Graphics.Imaging;
using Windows.Security.Cryptography;
using Windows.Storage.Streams;

namespace HeyGanesh.Tools.ElementSelectionSmoke;

/// <summary>
/// Host-only helper: draws a red box + yellow crosshair onto a captured JPEG so the visual sanity
/// check can confirm the chosen candidate's box lands on the right control. The box is given in
/// capture-local physical pixels (screen px minus the monitor origin) and mapped onto the downscaled
/// JPEG by the same uniform scale the capture used. Deliberately not in the module — annotation is
/// verification, not product surface.
/// </summary>
internal static class Annotator
{
    public static async Task<byte[]> AnnotateAsync(
        byte[] jpeg, int boxX, int boxY, int boxW, int boxH, int sourceWidth, int sourceHeight)
    {
        using var input = new InMemoryRandomAccessStream();
        await input.WriteAsync(CryptographicBuffer.CreateFromByteArray(jpeg)).AsTask();
        input.Seek(0);

        var decoder = await BitmapDecoder.CreateAsync(input).AsTask();
        using var software = await decoder
            .GetSoftwareBitmapAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied)
            .AsTask();

        var w = (int)software.PixelWidth;
        var h = (int)software.PixelHeight;

        // Read pixels out (same bridge CpuFrameReader uses: IBuffer <-> byte[] via CryptographicBuffer).
        var buffer = CryptographicBuffer.CreateFromByteArray(new byte[w * h * 4]);
        software.CopyToBuffer(buffer);
        CryptographicBuffer.CopyToByteArray(buffer, out var pixels);

        // Map the capture-local box onto the JPEG's downscaled pixel grid.
        double sx = (double)w / sourceWidth;
        double sy = (double)h / sourceHeight;
        int x1 = (int)(boxX * sx), y1 = (int)(boxY * sy);
        int x2 = (int)((boxX + boxW) * sx), y2 = (int)((boxY + boxH) * sy);
        int cx = Math.Clamp((x1 + x2) / 2, 0, w - 1);
        int cy = Math.Clamp((y1 + y2) / 2, 0, h - 1);

        DrawBox(pixels, w, h, x1, y1, x2, y2, b: 0, g: 0, r: 255, a: 255);       // red outline (BGRA)
        Crosshair(pixels, w, h, cx, cy, arm: 25, b: 0, g: 255, r: 255, a: 255);  // yellow

        using var output = new InMemoryRandomAccessStream();
        var encoder = await BitmapEncoder.CreateAsync(BitmapEncoder.JpegEncoderId, output).AsTask();
        encoder.SetPixelData(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Premultiplied, (uint)w, (uint)h, 96, 96, pixels);
        await encoder.FlushAsync().AsTask();
        return await ReadStreamAsync(output);
    }

    private static async Task<byte[]> ReadStreamAsync(InMemoryRandomAccessStream stream)
    {
        stream.Seek(0);
        using var reader = new DataReader(stream.GetInputStreamAt(0));
        await reader.LoadAsync((uint)stream.Size).AsTask();
        var data = new byte[(int)stream.Size];
        reader.ReadBytes(data);
        return data;
    }

    private static void DrawBox(byte[] px, int w, int h, int x1, int y1, int x2, int y2, byte b, byte g, byte r, byte a)
    {
        if (x1 > x2) { (x1, x2) = (x2, x1); }
        if (y1 > y2) { (y1, y2) = (y2, y1); }
        VLine(px, w, h, x1, y1, y2, b, g, r, a);
        VLine(px, w, h, x2, y1, y2, b, g, r, a);
        HLine(px, w, h, y1, x1, x2, b, g, r, a);
        HLine(px, w, h, y2, x1, x2, b, g, r, a);
    }

    private static void VLine(byte[] px, int w, int h, int x, int y1, int y2, byte b, byte g, byte r, byte a)
    {
        for (int dx = 0; dx <= 2; dx++)
        {
            var xx = x + dx;
            if ((uint)xx >= w) continue;
            for (int y = Math.Max(0, y1); y <= Math.Min(h - 1, y2); y++)
            {
                SetPixel(px, w, h, xx, y, b, g, r, a);
            }
        }
    }

    private static void HLine(byte[] px, int w, int h, int y, int x1, int x2, byte b, byte g, byte r, byte a)
    {
        for (int dy = 0; dy <= 2; dy++)
        {
            var yy = y + dy;
            if ((uint)yy >= h) continue;
            for (int x = Math.Max(0, x1); x <= Math.Min(w - 1, x2); x++)
            {
                SetPixel(px, w, h, x, yy, b, g, r, a);
            }
        }
    }

    private static void Crosshair(byte[] px, int w, int h, int cx, int cy, int arm, byte b, byte g, byte r, byte a)
    {
        for (int d = -arm; d <= arm; d++)
        {
            SetPixel(px, w, h, cx + d, cy, b, g, r, a);
            SetPixel(px, w, h, cx, cy + d, b, g, r, a);
        }
    }

    private static void SetPixel(byte[] px, int w, int h, int x, int y, byte b, byte g, byte r, byte a)
    {
        if ((uint)x >= w || (uint)y >= h) return;
        var i = (y * w + x) * 4;
        px[i] = b; px[i + 1] = g; px[i + 2] = r; px[i + 3] = a;
    }
}
