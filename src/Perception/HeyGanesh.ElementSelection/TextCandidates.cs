using System.Text.RegularExpressions;
using HeyGanesh.Abstractions;

namespace HeyGanesh.ElementSelection;

/// <summary>
/// Turns the text the capture read off the screen (DEC-038) into extra candidates for Jev (DEC-039):
/// words UI Automation cannot see — text in canvases, PDFs, slides, some web content, or whole apps
/// with no UIA tree. Pure, so every rule is checked offline (ES-8).
/// </summary>
/// <remarks>
/// <para>This is the one place screen text is allowed into a Jev request, which goes to TypeSafe, so
/// it is filtered hard first. A line is kept only when:</para>
/// <list type="bullet">
/// <item>its centre is inside the target window (the user is asking about that window);</item>
/// <item>its centre is not on a UIA control already offered (a button's label is not offered twice);</item>
/// <item>it is at most <see cref="MaxChars"/> characters (targets are labels, not paragraphs);</item>
/// <item>it does not look like a secret (an API key, token or long random string).</item>
/// </list>
/// <para>At most <see cref="MaxLines"/> lines are kept, in reading order, so the text can never crowd
/// the UIA candidates out of the manifest cap.</para>
/// </remarks>
public static class TextCandidates
{
    public const int MaxLines = 40;
    public const int MaxChars = 80;

    /// <summary>The control-type name the option descriptions show, so Jev knows this is read text.</summary>
    public const string ControlTypeName = "Text (OCR)";

    // Known key and token shapes, then a generic long letters-and-digits run. Matching drops the
    // whole line: a partial redaction would still describe the secret's position and length.
    private static readonly Regex Secret = new(
        @"\b(sk|pk|rk)-[A-Za-z0-9_\-]{12,}|\bgh[pousr]_[A-Za-z0-9]{16,}|\bAKIA[0-9A-Z]{12,}|\bxox[abprs]-[A-Za-z0-9\-]{8,}" +
        @"|\beyJ[A-Za-z0-9_\-]{8,}\.[A-Za-z0-9_\-]{8,}|(?=[A-Za-z0-9+/_=\-]*\d)(?=[A-Za-z0-9+/_=\-]*[A-Za-z])[A-Za-z0-9+/_=\-]{24,}",
        RegexOptions.CultureInvariant);

    /// <summary>Whether a line looks like it carries a secret and must never be offered.</summary>
    public static bool LooksSecret(string text) => Secret.IsMatch(text);

    /// <summary>
    /// The lines worth offering, as candidates with empty ids (the service assigns ids after ranking,
    /// as for UIA candidates). <paramref name="controls"/> are the UIA candidates already found;
    /// <paramref name="windowRect"/> is the target window in global physical pixels.
    /// </summary>
    public static IReadOnlyList<UiaCandidate> FromScreenText(
        IReadOnlyList<ScreenTextLine> lines,
        IReadOnlyList<UiaCandidate> controls,
        PixelRect windowRect)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(controls);

        var kept = new List<UiaCandidate>();
        var seen = new HashSet<(string, PixelRect)>();
        foreach (var line in lines.OrderBy(l => l.Box.Y).ThenBy(l => l.Box.X))
        {
            if (kept.Count >= MaxLines)
            {
                break;
            }

            var text = string.Join(' ', line.Text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
            if (text.Length == 0 || text.Length > MaxChars || LooksSecret(text))
            {
                continue;
            }

            var cx = line.Box.X + (line.Box.Width / 2);
            var cy = line.Box.Y + (line.Box.Height / 2);
            if (!Inside(cx, cy, windowRect) || controls.Any(c => Inside(cx, cy, c.Rect)) || !seen.Add((text, line.Box)))
            {
                continue;
            }

            kept.Add(new UiaCandidate(
                Id: string.Empty,
                Name: text,
                ControlType: ControlTypeName,
                ControlTypeId: 0,
                Rect: line.Box,
                PositionHint: CandidateEnumerator.ComputePositionHint(line.Box, windowRect)));
        }

        return kept;
    }

    private static bool Inside(int x, int y, PixelRect r) => x >= r.X && x < r.Right && y >= r.Y && y < r.Bottom;
}
