using System.Diagnostics;
using HeyGanesh.Abstractions;
using HeyGanesh.App;
using HeyGanesh.Notes;

namespace HeyGanesh.Tools.TraySmoke;

internal static partial class TraySmokeHost
{
    // NB-1 (DEC-059): the App Shell's turn-to-note mapping, every case, and a round trip through a real store. Offline.
    private static int RunAssertNotesMap()
    {
        var failures = 0;
        var at = new DateTimeOffset(2026, 10, 30, 21, 0, 0, TimeSpan.FromHours(5.5));
        var found = new GuidanceResult { Found = true, Description = "Save button", Confidence = GuidanceConfidence.High, Source = GuidanceSource.ElementSelection };
        var noMatch = new GuidanceResult { Found = false, Description = "nothing like it", Confidence = GuidanceConfidence.Low, Source = GuidanceSource.ElementSelection };
        var failed = noMatch with { Source = GuidanceSource.Vision, FailureReason = "the model inspected the image but could not locate the requested element." };

        var tutor = new TurnRecord { Leg = NoteLeg.Tutor, Route = "routed: explain", Answer = "Saving writes the file to disk.", Outcome = "explained" };
        var note = TurnNotes.From("  where is Save and what does it do?  ", "s-1", at, found, tutor, 0);
        failures = Check(note is { Question: "where is Save and what does it do?", Session: "s-1", Leg: NoteLeg.Tutor, Route: "routed: explain", VerdictSource: "ElementSelection", Found: true,
                Element: "Save button", Confidence: "High", Answer: "Saving writes the file to disk.", LessonPlan: null, Outcome: "explained" } && note.At == at,
            "a Tutor turn: the question trimmed, the verdict in words, the answer, the outcome", failures);

        var miss = TurnNotes.From("explain entropy", "s-1", at, noMatch, new TurnRecord(), 0);
        failures = Check(miss is { Found: false, Element: null, Outcome: "exit 0" }, "an honest no-match keeps no element; with no outcome said, the exit code stands in", failures);

        var lesson = new TurnRecord { Leg = NoteLeg.Lesson, LessonTitle = "How a four-stroke engine works", PlanJson = "{\"schema\":\"heyganesh.visualplan/1.1\"}", Outcome = "Played, 1 follow-up(s), ended NoPick" };
        lesson.Picks.Add("engine.connecting-rod");
        var taught = TurnNotes.From("how does an engine work?", "s-1", at, found, lesson, 0);
        failures = Check(taught is { Leg: NoteLeg.Lesson, LessonTitle: "How a four-stroke engine works", LessonPlan: "{\"schema\":\"heyganesh.visualplan/1.1\"}" } && taught.Picks.SequenceEqual(new[] { "engine.connecting-rod" }),
            "a lesson turn keeps its title, the exact plan text, and the parts clicked", failures);

        var none = new[]
        {
            TurnNotes.From("x", "s", at, found, tutor, 12),
            TurnNotes.From("x", "s", at, found, tutor, 13),
            TurnNotes.From("x", "s", at, failed, tutor, 1),
            TurnNotes.From("   ", "s", at, found, tutor, 0),
        };
        failures = Check(none.All(n => n is null), "no note for a key error (12), a cancelled turn (13), a Failed verdict, or no question", failures);
        failures = Check(TurnNotes.From("x", "s", at, found, tutor, 3) is { Outcome: "explained" } && TurnNotes.From("x", "s", at, found, new TurnRecord(), 4) is { Outcome: "exit 4" },
            "a provider fault (3) or a defect (4) is still a turn the student asked: noted, with its outcome", failures);

        var dir = TempFolder("notes-map");
        try
        {
            using var store = NotesStore.Open(Path.Combine(dir, "notes.db"));
            var id = store.Record(taught!);
            var back = store.Get(id);
            failures = Check(back is not null && back with { Id = 0, Picks = taught!.Picks } == taught && back.Picks.SequenceEqual(taught.Picks),
                "the mapped note goes through the real store and reads back unchanged", failures);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        return Summarize(failures, "NOTES MAP OK");
    }

    // NB-4 (DEC-061): the shell's memory of this run's turns: only taught turns, the newest 3, said in a line each.
    private static int RunAssertSession()
    {
        var failures = 0;
        var at = new DateTimeOffset(2026, 10, 31, 10, 0, 0, TimeSpan.Zero);
        TurnNote Turn(string q, NoteLeg leg, string? answer = null, string? title = null, string? element = null) =>
            new() { At = at, Session = "s", Question = q, Leg = leg, Answer = answer, LessonTitle = title, Element = element, Outcome = "x" };

        var session = new SessionTurns();
        failures = Check(session.Snapshot().Count == 0 && SessionTurns.Keep == 3, "a new run remembers nothing, and keeps at most 3", failures);

        session.Add(null);
        session.Add(Turn("skipped tutor", NoteLeg.Tutor));
        session.Add(Turn("skipped lesson", NoteLeg.Lesson));
        failures = Check(session.Snapshot().Count == 0, "no note (a key error, a cancelled turn) and a turn that taught nothing are not kept", failures);

        session.Add(Turn("what is entropy?", NoteLeg.Tutor, answer: "A count of arrangements."));
        session.Add(Turn("where is Save?", NoteLeg.Highlight, element: "Save button"));
        session.Add(Turn("how does an engine work?", NoteLeg.Lesson, title: "How a four-stroke engine works"));
        session.Add(Turn("and the exhaust stroke?", NoteLeg.Tutor, answer: "The piston pushes the burnt gas out."));
        var kept = session.Snapshot();
        failures = Check(kept.Select(k => k.Question).SequenceEqual(new[] { "where is Save?", "how does an engine work?", "and the exhaust stroke?" }),
            $"after four taught turns the newest 3 are kept, oldest first ({string.Join(" / ", kept.Select(k => k.Question))})", failures);
        failures = Check(kept[0].Taught == "showed the Save button on screen" && kept[1].Taught == "a drawn lesson: How a four-stroke engine works" && kept[2].Taught == "The piston pushes the burnt gas out.",
            "each says what it taught: the highlight, the lesson's title, the Tutor's answer", failures);
        failures = Check(SessionTurns.TaughtLine(Turn("where?", NoteLeg.Highlight)) == "showed something on screen", "a highlight with no element name still counts", failures);
        return Summarize(failures, "SESSION OK");
    }

    // NB-2 (DEC-059): the real HeyGanesh.App.exe with temporary data folders. Local: --once captures the real screen.
    // It never runs with a key stored, so it can never make a live (billed) call.
    private static int RunAssertNotesShell()
    {
        var failures = 0;
        var exe = Path.Combine(RepoRoot(), "src", "Experience", "HeyGanesh.App", "bin", "Debug", "net8.0-windows10.0.22621.0", "HeyGanesh.App.exe");
        var (_, keys) = App(exe, TempFolder("notes-keys"), "--keys");
        if (keys.Contains("present (", StringComparison.Ordinal))
        {
            Console.WriteLine("REFUSED: a provider key is stored or set, and this row must never make a live call. Run it with no keys (exit 2).");
            return 2;
        }

        var (help, usage) = App(exe, TempFolder("notes-help"), "--help");
        failures = Check(help == 0 && usage.Contains("--no-notes", StringComparison.Ordinal) && usage.Contains("--no-context", StringComparison.Ordinal), "--help lists --no-notes and --no-context", failures);

        var on = TempFolder("notes-on");
        var (onCode, onOut) = App(exe, on, "--once", "--question", "where is the Save button");
        var db = Path.Combine(on, "notes.db");
        failures = Check(onCode == 12 && onOut.Contains($"notes      : ON ({db})", StringComparison.Ordinal), $"the header says where notes go; with no keys the turn stops at KEY ERROR (exit {onCode})", failures);
        failures = Check(onOut.Contains("context    : ON: the last 3 taught turns of this run go with each question (memory only)", StringComparison.Ordinal),
            "the header says earlier turns go with each question (DEC-061)", failures);
        using (var store = NotesStore.Open(db))
        {
            failures = Check(store.Count() == 0 && !onOut.Contains("notes       : saved", StringComparison.Ordinal), "a key-error turn leaves no note (the store exists and is empty)", failures);
        }

        var off = TempFolder("notes-off");
        var (offCode, offOut) = App(exe, off, "--once", "--question", "where is the Save button", "--no-notes", "--no-context");
        failures = Check(offCode == 12 && offOut.Contains("notes      : off (--no-notes)", StringComparison.Ordinal) && !File.Exists(Path.Combine(off, "notes.db"))
            && offOut.Contains("context    : off (--no-context)", StringComparison.Ordinal),
            "--no-notes and --no-context: the header says both are off, and no notes.db is created", failures);

        var broken = TempFolder("notes-broken");
        File.WriteAllText(Path.Combine(broken, "notes.db"), new string('x', 4096));
        var (brokenCode, brokenOut) = App(exe, broken, "--once", "--question", "where is the Save button");
        var kept = Directory.GetFiles(broken, "notes.db.broken-*");
        failures = Check(brokenCode == 12 && brokenOut.Contains("an unreadable old file was kept as notes.db.broken-", StringComparison.Ordinal) && kept.Length == 1,
            "a broken notes.db is kept aside, the header says so, and the run goes on as before", failures);

        foreach (var dir in new[] { on, off, broken })
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }

        return Summarize(failures, "NOTES SHELL OK");
    }

    /// <summary>Runs the console exe with its data folder pointed at <paramref name="data"/>; returns the exit code and output.</summary>
    private static (int Code, string Output) App(string exe, string data, params string[] args)
    {
        var info = new ProcessStartInfo(exe) { UseShellExecute = false, RedirectStandardOutput = true, RedirectStandardError = true, CreateNoWindow = true };
        foreach (var a in args)
        {
            info.ArgumentList.Add(a);
        }

        info.Environment["HEYGANESH_DATA_DIR"] = data;
        using var process = Process.Start(info)!;
        var stderr = process.StandardError.ReadToEndAsync();
        var output = process.StandardOutput.ReadToEnd();
        if (!process.WaitForExit(60000))
        {
            process.Kill();
            return (-1, output);
        }

        return (process.ExitCode, output + stderr.Result);
    }
}
