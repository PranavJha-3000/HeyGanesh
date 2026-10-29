using HeyGanesh.Abstractions;
using HeyGanesh.ElementSelection;
using HeyGanesh.ScreenCapture;

namespace HeyGanesh.Tools.ElementSelectionSmoke;

// Screen text as candidates (DEC-039).
//   --assert-text-merge          ES-8: TextCandidates' rules from made-up lines; no window, CI-safe (G-20).
//   --list-only --read-text      ES-9: a real capture with OCR on, merged over the real window's UIA
//                                candidates; counts only, never the text (local-only).

internal static partial class ElementSelectionSmokeHost
{
    private static readonly PixelRect Window = new(100, 100, 900, 600);

    private static int RunAssertTextMerge()
    {
        Console.WriteLine("Element Selection verification host -- screen text merge rules (made-up lines, no window)");
        Console.WriteLine();
        var failures = 0;
        var save = new UiaCandidate("", "Save", "Button", 50000, new PixelRect(120, 120, 80, 30), "top-left");
        UiaCandidate[] controls = { save };

        static ScreenTextLine L(string text, int x, int y, int w = 120, int h = 20) => new(text, new PixelRect(x, y, w, h));

        var one = TextCandidates.FromScreenText(new[] { L("Chain rule", 400, 130) }, controls, Window);
        failures = Check(one.Count == 1 && one[0].ControlType == TextCandidates.ControlTypeName && one[0].Id == "" && one[0].ControlTypeId == 0,
            "a line inside the window and off every control is offered as 'Text (OCR)' with no id yet", failures);
        failures = Check(one.Count == 1 && one[0].PositionHint == "top-center" && one[0].Rect == new PixelRect(400, 130, 120, 20),
            $"its box is the line's box and its hint is computed like a control's ({(one.Count == 1 ? one[0].PositionHint : "-")})", failures);

        failures = Check(TextCandidates.FromScreenText(new[] { L("Elsewhere", 1200, 130) }, controls, Window).Count == 0,
            "a line outside the target window is dropped", failures);
        failures = Check(TextCandidates.FromScreenText(new[] { L("Save", 125, 125, 40, 16) }, controls, Window).Count == 0,
            "a line whose centre is on a UIA control is dropped (the control is offered already)", failures);

        var len80 = new string('a', 75) + " bcde";
        var len81 = len80 + "f";
        failures = Check(TextCandidates.FromScreenText(new[] { L(len80, 300, 300) }, controls, Window).Count == 1
                         && TextCandidates.FromScreenText(new[] { L(len81, 300, 300) }, controls, Window).Count == 0,
            "80 characters is kept, 81 is dropped", failures);

        string[] secrets =
        {
            "OPENAI sk-abc123def456ghi789jkl",
            "token ghp_A1b2C3d4E5f6G7h8I9j0K1l2",
            "AKIAIOSFODNN7EXAMPLE",
            "Bearer eyJhbGciOiJIUzI1NiJ9.eyJzdWIiOiIxMjM0NSJ9",
            "slack xoxb-1234567890-abcdefgh",
            "key Q2hhaW5SdWxlSXNHcmVhdDEyMzQ1Njc4",
        };
        foreach (var s in secrets)
        {
            failures = Check(TextCandidates.LooksSecret(s) && TextCandidates.FromScreenText(new[] { L(s, 300, 300) }, controls, Window).Count == 0,
                $"secret-looking line dropped ({s[..Math.Min(12, s.Length)]}...)", failures);
        }

        string[] ordinary = { "Chapter 12: the chain rule", "ISBN 978-0-13-468599-1", "Total 1,234.56", "Open settings", "x2 + 3x - 4 = 0" };
        failures = Check(ordinary.All(o => !TextCandidates.LooksSecret(o)), "ordinary text with numbers is not treated as a secret", failures);

        var spaced = TextCandidates.FromScreenText(new[] { L("  Open    settings ", 300, 300), L("Open settings", 300, 300) }, controls, Window);
        failures = Check(spaced.Count == 1 && spaced[0].Name == "Open settings", "spacing is normalised and the same line at the same box is offered once", failures);

        var many = Enumerable.Range(0, 50).Select(i => L($"line {i}", 300, 600 - (i * 9), 100, 8)).ToArray();
        var capped = TextCandidates.FromScreenText(many, controls, Window);
        failures = Check(capped.Count == TextCandidates.MaxLines, $"at most {TextCandidates.MaxLines} lines are offered ({capped.Count})", failures);
        failures = Check(capped.Zip(capped.Skip(1)).All(p => p.First.Rect.Y <= p.Second.Rect.Y), "lines are kept in reading order (top first)", failures);

        failures = Check(TextCandidates.FromScreenText(Array.Empty<ScreenTextLine>(), controls, Window).Count == 0
                         && TextCandidates.FromScreenText(new[] { L("   ", 300, 300) }, controls, Window).Count == 0,
            "no lines, or blank lines, offer nothing", failures);

        Console.WriteLine(failures == 0 ? "TEXT MERGE OK (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    private static async Task<int> RunReadTextAsync(HostOptions options)
    {
        Console.WriteLine("--list-only --read-text: real capture with OCR, merged over the real window (counts only, never the text)");
        var failures = 0;
        using var capture = new ScreenCaptureService(new ScreenCaptureOptions { ReadText = true });
        var frames = await capture.CaptureAllMonitorsAsync();
        var lines = frames[0].TextLines;
        Console.WriteLine($"  read        : {lines.Count} lines on {frames[0].Monitor.DeviceName} in {frames[0].TextDuration.TotalMilliseconds:F0} ms" +
                          (frames[0].TextNote is { } note ? $" (note: {note})" : string.Empty));

        var set = new CandidateEnumerator(new SelectionOptions { MaxCandidates = options.MaxCandidates })
            .Enumerate(options.Foreground ? SelectionTarget.ForegroundWindow : SelectionTarget.CursorWindow);
        var text = TextCandidates.FromScreenText(lines, set.Candidates, set.Target.WindowRect);
        var ranked = CandidateRanker.RankAndCap(set.Candidates.Concat(text).ToList(), options.Question, options.MaxCandidates);
        var inManifest = ranked.Count(c => c.ControlType == TextCandidates.ControlTypeName);
        Console.WriteLine($"  window      : {set.Target.WindowRect}, {set.Candidates.Count} UIA candidates");
        Console.WriteLine($"  text offered: {text.Count} of {lines.Count} lines; {inManifest} in the {ranked.Count}-candidate manifest after ranking");

        var w = set.Target.WindowRect;
        failures = Check(text.Count <= TextCandidates.MaxLines, $"at most {TextCandidates.MaxLines} text candidates", failures);
        failures = Check(text.All(c => c.Rect.X + (c.Rect.Width / 2) >= w.X && c.Rect.X + (c.Rect.Width / 2) < w.Right
                                       && c.Rect.Y + (c.Rect.Height / 2) >= w.Y && c.Rect.Y + (c.Rect.Height / 2) < w.Bottom),
            "every text candidate's centre is inside the target window", failures);
        failures = Check(text.All(c => !TextCandidates.LooksSecret(c.Name) && c.Name.Length <= TextCandidates.MaxChars),
            "no text candidate looks secret or is longer than 80 characters", failures);
        Console.WriteLine(failures == 0 ? "READ TEXT MERGE OK (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    private static int Check(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }
}
