using HeyGanesh.ElementSelection;

namespace HeyGanesh.Tools.ElementSelectionSmoke;

// Helpers for the Element Selection smoke host, part 1: fake client, list-only mode,
// and the coordinate cross-check.

internal static partial class ElementSelectionSmokeHost
{
    private static int RunListOnly(HostOptions options)
    {
        var enumerator = new CandidateEnumerator(new SelectionOptions { MaxCandidates = options.MaxCandidates });
        CandidateSet set;
        try
        {
            set = enumerator.Enumerate(options.Foreground ? SelectionTarget.ForegroundWindow : SelectionTarget.CursorWindow);
        }
        catch (InvalidOperationException ex)
        {
            Console.WriteLine($"ENUM ERROR: {ex.Message}");
            return 1;
        }
        catch (System.ComponentModel.Win32Exception ex)
        {
            Console.WriteLine($"ENUM ERROR: {ex.Message}");
            return 1;
        }

        Console.WriteLine($"window      : '{set.Target.Title}' hwnd=0x{set.Target.Handle:X}");
        Console.WriteLine($"enumerated  : control-view elements={set.TotalEnumerated}  interactive after filter={set.InteractiveAfterFilter}");
        Console.WriteLine("candidates (first 15):");
        var shown = 0;
        foreach (var candidate in set.Candidates)
        {
            if (shown++ >= 15)
            {
                Console.WriteLine($"  ... (+{set.Candidates.Count - 15} more)");
                break;
            }
            Console.WriteLine($"  {candidate}");
        }

        CrossCheck(set.Target);
        return 0;
    }

    /// <summary>
    /// The "verify, don't recall" check for the coordinate contract: UIA's root BoundingRectangle
    /// and GetWindowRect must describe the same window in the same units. A mismatch means a DPI
    /// awareness or coordinate-space problem and invalidates every BoxOnScreen the module emits.
    /// </summary>
    private static void CrossCheck(TargetWindowInfo target)
    {
        var win = target.WindowRect;
        var root = target.UiaRootRect;
        int dx = Math.Abs(win.X - root.X);
        int dy = Math.Abs(win.Y - root.Y);
        int dw = Math.Abs(win.Width - root.Width);
        int dh = Math.Abs(win.Height - root.Height);
        // Window borders/decorations can differ by a pixel between the two APIs; 2px is the tolerance.
        bool match = dx <= 2 && dy <= 2 && dw <= 2 && dh <= 2;
        Console.WriteLine($"coordinate cross-check: GetWindowRect={win}  UIA root={root}  ->  " +
                          (match ? "MATCH (same physical-px space)" : $"MISMATCH delta=({dx},{dy},{dw},{dh}) -- flag this"));
    }
}
