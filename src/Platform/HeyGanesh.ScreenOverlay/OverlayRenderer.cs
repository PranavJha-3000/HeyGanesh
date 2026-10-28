using System.Runtime.InteropServices;
using HeyGanesh.Abstractions;
using HeyGanesh.ScreenCapture;

namespace HeyGanesh.ScreenOverlay;

/// <summary>
/// Draws one overlay window's highlight: a translucent accent band around the resolved rectangle,
/// a thin ink edge, and an optional target glyph at the resolved center.
/// </summary>
/// <remarks>
/// <para>
/// Plain GDI with a colour-key surface: the whole window is filled with
/// <see cref="OverlayNative.ColorKey"/> (transparent through the layered window) and shapes are
/// painted in opaque accent/ink, with <c>SetLayeredWindowAttributes</c>'s single global alpha
/// making the drawn parts translucent. No per-pixel alpha, no DIB, no dependency.
/// </para>
/// <para>
/// Called only from the owning window's <c>WM_PAINT</c> handler on the pump thread, so it is never
/// concurrent. Sizes come from <see cref="OverlayDraw.MonitorBounds"/> — the window is a borderless
/// popup exactly covering one monitor, so client size == monitor size.
/// </para>
/// </remarks>
internal static class OverlayRenderer
{
    /// <summary>HeyGanesh's accent (warm yellow), readable over both light and dark UIs.</summary>
    internal const uint AccentColor = 0x0033CCFF; // COLORREF 0x00BBGGRR -> R=255, G=204, B=51

    /// <summary>Near-opaque deep brown-black for the inner edge and the glyph.</summary>
    internal const uint InkColor = 0x00302010;

    internal static void Paint(IntPtr window, OverlayDraw draw, ScreenOverlayOptions options)
    {
        var paint = new OverlayPInvoke.PaintStruct();
        var hdc = OverlayPInvoke.BeginPaint(window, out paint);
        if (hdc == IntPtr.Zero)
        {
            return;
        }

        try
        {
            var width = draw.MonitorBounds.Width;
            var height = draw.MonitorBounds.Height;

            // Draw into a memory DC, then blit once: the screen is never touched directly and the
            // whole highlight appears in a single composition step.
            var mem = OverlayPInvoke.CreateCompatibleDC(hdc);
            if (mem == IntPtr.Zero)
            {
                return;
            }

            var bitmap = OverlayPInvoke.CreateCompatibleBitmap(hdc, width, height);
            if (bitmap == IntPtr.Zero)
            {
                OverlayPInvoke.DeleteDC(mem);
                return;
            }

            var originalBitmap = OverlayPInvoke.SelectObject(mem, bitmap);

            // Background first: the colour key, i.e. fully transparent once the window composites.
            var background = OverlayPInvoke.CreateSolidBrush(OverlayNative.ColorKey);
            var originalBrush = OverlayPInvoke.SelectObject(mem, background);
            var originalPen = OverlayPInvoke.SelectObject(mem, OverlayPInvoke.GetStockObject(OverlayNative.NULL_PEN));

            OverlayPInvoke.Rectangle(mem, 0, 0, width, height);

            IntPtr accentBrush = IntPtr.Zero, bandPen = IntPtr.Zero, edgePen = IntPtr.Zero, glyphPen = IntPtr.Zero;
            try
            {
                if (options.ShowBox && draw.OutlineLocal is { } outline)
                {
                    DrawOutline(mem, outline, options, ref accentBrush, ref bandPen, ref edgePen);
                }

                if (options.ShowPointer && draw.CenterLocal is { } center)
                {
                    DrawGlyph(mem, center, options, ref glyphPen);
                }

                OverlayPInvoke.BitBlt(hdc, 0, 0, width, height, mem, 0, 0, OverlayPInvoke.SRCCOPY);
            }
            finally
            {
                // Restore selections before deleting: GDI refuses to delete a selected object.
                OverlayPInvoke.SelectObject(mem, originalPen);
                OverlayPInvoke.SelectObject(mem, originalBrush);
                OverlayPInvoke.SelectObject(mem, originalBitmap);

                Delete(background);
                Delete(accentBrush);
                Delete(bandPen);
                Delete(edgePen);
                Delete(glyphPen);
                Delete(bitmap);
                OverlayPInvoke.DeleteDC(mem);
            }
        }
        finally
        {
            OverlayPInvoke.EndPaint(window, ref paint);
        }
    }

    /// <summary>
    /// Frame-only highlight: an accent band around the element, its interior punched back to the
    /// colour key so the UI underneath stays fully readable, and a thin ink edge on the element's own
    /// boundary. GDI's <c>Rectangle</c> excludes its right/bottom edge and fills with the selected
    /// brush, hence the three-step select/draw/restore dance.
    /// </summary>
    private static void DrawOutline(
        IntPtr mem,
        PixelRect outline,
        ScreenOverlayOptions options,
        ref IntPtr accentBrush,
        ref IntPtr bandPen,
        ref IntPtr edgePen)
    {
        var t = Math.Max(1, options.BoxThickness);

        // 1) The band: expanded rect so the highlight sits OUTSIDE the element rather than over it.
        accentBrush = OverlayPInvoke.CreateSolidBrush(AccentColor);
        bandPen = OverlayPInvoke.CreatePen(OverlayPInvoke.PS_SOLID, t, AccentColor);
        OverlayPInvoke.SelectObject(mem, accentBrush);
        OverlayPInvoke.SelectObject(mem, bandPen);
        OverlayPInvoke.Rectangle(mem, outline.X - t, outline.Y - t, outline.Right + t, outline.Bottom + t);

        // 2) Interior back to transparent + ink edge on the boundary. The brush must be the colour
        // key here or Rectangle's fill would re-tint the element it is supposed to reveal.
        edgePen = OverlayPInvoke.CreatePen(OverlayPInvoke.PS_SOLID, Math.Max(1, t / 2), InkColor);
        var clearBrush = OverlayPInvoke.CreateSolidBrush(OverlayNative.ColorKey);
        var previousPen = OverlayPInvoke.SelectObject(mem, edgePen);
        var previousBrush = OverlayPInvoke.SelectObject(mem, clearBrush);
        OverlayPInvoke.Rectangle(mem, outline.X, outline.Y, outline.Right, outline.Bottom);
        OverlayPInvoke.SelectObject(mem, previousPen);
        OverlayPInvoke.SelectObject(mem, previousBrush);
        Delete(clearBrush); // deselected above, so GDI will actually release it
    }

    /// <summary>
    /// Target glyph: a ring with crosshairs whose middle gap keeps the exact point it names visible.
    /// Lines only — no brush, so no fill dance — and no Arc import: 24 segments keep the P/Invoke
    /// surface at what we actually use.
    /// </summary>
    private static void DrawGlyph(
        IntPtr mem,
        PixelRect center,
        ScreenOverlayOptions options,
        ref IntPtr glyphPen)
    {
        var r = Math.Max(2, options.PointerRadius);
        var cx = center.X; // the local 1x1 rect's corner IS the resolved center pixel
        var cy = center.Y;

        glyphPen = OverlayPInvoke.CreatePen(OverlayPInvoke.PS_SOLID, Math.Max(2, r / 3), InkColor);
        OverlayPInvoke.SelectObject(mem, glyphPen);

        var gap = Math.Max(2, r / 2);
        OverlayPInvoke.MoveToEx(mem, cx - r, cy, IntPtr.Zero);
        OverlayPInvoke.LineTo(mem, cx - gap, cy);
        OverlayPInvoke.MoveToEx(mem, cx + gap, cy, IntPtr.Zero);
        OverlayPInvoke.LineTo(mem, cx + r, cy);
        OverlayPInvoke.MoveToEx(mem, cx, cy - r, IntPtr.Zero);
        OverlayPInvoke.LineTo(mem, cx, cy - gap);
        OverlayPInvoke.MoveToEx(mem, cx, cy + gap, IntPtr.Zero);
        OverlayPInvoke.LineTo(mem, cx, cy + r);

        const int segments = 24;
        var previousX = cx + r;
        var previousY = cy;
        for (var i = 1; i <= segments; i++)
        {
            var angle = (2 * Math.PI * i) / segments;
            var x = cx + (int)Math.Round(r * Math.Cos(angle));
            var y = cy + (int)Math.Round(r * Math.Sin(angle));
            OverlayPInvoke.MoveToEx(mem, previousX, previousY, IntPtr.Zero);
            OverlayPInvoke.LineTo(mem, x, y);
            previousX = x;
            previousY = y;
        }
    }

    private static void Delete(IntPtr gdiObject)
    {
        if (gdiObject != IntPtr.Zero)
        {
            OverlayPInvoke.DeleteObject(gdiObject);
        }
    }
}
