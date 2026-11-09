using HeyGanesh.Abstractions;
using HeyGanesh.App;
using HeyGanesh.DrawStage;
using HeyGanesh.Notes;
using HeyGanesh.Security;
using HeyGanesh.Tray;

namespace HeyGanesh.Tools.TraySmoke;

internal static partial class TraySmokeHost
{
    /// <summary>A drawn lesson as the shell stores it (DEC-076): two spoken lines on a 1280x720 canvas.</summary>
    private static readonly string EngineDrawing =
        StoredDrawing.Store(1280, 720, "step(1)\nsay('Fuel and air are drawn in.')\nline(100, 100, 400, 100)\nsay('The spark pushes the piston down.')");

    /// <summary>What a lesson row held before the drawing stage: VisualPlan JSON. Never replayed now.</summary>
    private const string OlderPlan = "{\"schema\":\"heyganesh.visualplan/1.1\",\"lessonId\":\"engine\",\"title\":\"How a four-stroke engine works\"}";

    /// <summary>A temp store holding a highlight, a Tutor turn, a drawn lesson, an unstored lesson and an older (cards) lesson, oldest first.</summary>
    private static NotesStore SeededStore(string dir, out long[] ids)
    {
        var store = NotesStore.Open(Path.Combine(dir, "notes.db"));
        var at = new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.FromHours(5.5));
        ids = new[]
        {
            store.Record(new TurnNote { At = at, Session = "s", Question = "where is Save?", Leg = NoteLeg.Highlight, VerdictSource = "ElementSelection", Found = true, Element = "Save button", Confidence = "High", Outcome = "the highlight was the answer" }),
            store.Record(new TurnNote { At = at.AddMinutes(1), Session = "s", Question = "what is entropy?", Leg = NoteLeg.Tutor, Found = false, Answer = "Entropy measures how many ways a system's parts can be arranged. " + new string('x', 80), Outcome = "explained" }),
            store.Record(new TurnNote { At = at.AddMinutes(2), Session = "s", Question = "how does an engine work?", Leg = NoteLeg.Lesson, Found = false, LessonTitle = "Fuel and air are drawn in.", LessonPlan = EngineDrawing, Picks = new[] { "piston" }, Outcome = "drawn" }),
            store.Record(new TurnNote { At = at.AddMinutes(3), Session = "s", Question = "draw the chain rule", Leg = NoteLeg.Lesson, Found = false, LessonTitle = "The chain rule", Outcome = "skipped: no lesson model" }),
            store.Record(new TurnNote { At = at.AddMinutes(4), Session = "s", Question = "explain the engine again", Leg = NoteLeg.Lesson, Found = false, LessonTitle = "How a four-stroke engine works", LessonPlan = OlderPlan, Outcome = "Played" }),
        };
        return store;
    }

    // RL-1 (DEC-060, DEC-076): what the Recent lessons window lists and says, offline.
    private static int RunAssertRecentModel()
    {
        var failures = 0;
        var dir = TempFolder("recent-model");
        try
        {
            using var model = new RecentLessonsModel(SeededStore(dir, out var ids));
            var rows = model.Rows();
            failures = Check(rows.Select(r => r.Id).SequenceEqual(ids.Reverse()), "the newest turn is listed first", failures);
            failures = Check(rows[4].Taught == "Shown: Save button" && rows[3].Taught.StartsWith("Explained: Entropy measures", StringComparison.Ordinal) && rows[3].Taught.EndsWith("…", StringComparison.Ordinal)
                && rows[3].Taught.Length <= 72 && rows[2].Taught == "Lesson: Fuel and air are drawn in." && rows[1].Taught == "Lesson: The chain rule" && rows[0].Taught == "Lesson: How a four-stroke engine works",
                $"each row says what was taught, a long answer cut short ({string.Join(" / ", rows.Select(r => r.Taught))})", failures);
            failures = Check(rows.Select(r => r.CanReplay).SequenceEqual(new[] { false, false, true, false, false }), "only a stored drawing can be replayed: not an unstored lesson, not an older (cards) one", failures);

            var lesson = model.Detail(ids[2]);
            var tutor = model.Detail(ids[1]);
            failures = Check(lesson.Contains("Asked: how does an engine work?", StringComparison.Ordinal) && lesson.Contains("Replay draws and speaks it again, with no model call.", StringComparison.Ordinal)
                && lesson.Contains("Parts you clicked: piston", StringComparison.Ordinal) && !lesson.Contains("say(", StringComparison.Ordinal) && !lesson.Contains(StoredDrawing.Marker, StringComparison.Ordinal),
                "a drawn lesson's details say what was asked, that it can be replayed, and the parts clicked, never the program", failures);
            failures = Check(tutor.Contains("Entropy measures how many ways", StringComparison.Ordinal) && tutor.Contains(new string('x', 80), StringComparison.Ordinal) && tutor.Contains("On screen: nothing matched", StringComparison.Ordinal),
                "a Tutor turn's details hold the whole answer", failures);
            var older = model.Detail(ids[4]);
            failures = Check(model.Detail(ids[3]).Contains("This lesson was not stored, so it cannot be replayed.", StringComparison.Ordinal)
                && older.Contains("made by an older version of Hey Ganesh (lesson cards), so it cannot be replayed", StringComparison.Ordinal) && !older.Contains("\"schema\"", StringComparison.Ordinal)
                && model.StoredLesson(ids[2]) == EngineDrawing,
                "an unstored lesson and an older (cards) one each say why they cannot be replayed; a drawing hands back exactly what was stored", failures);

            failures = Check(model.Delete(ids[1]) && !model.Delete(ids[1]) && model.Rows().Count == 4 && model.Detail(ids[1]) == string.Empty, "Delete removes one turn", failures);
            failures = Check(model.Clear() == 4 && model.Rows().Count == 0, "Clear all removes the rest", failures);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        return Summarize(failures, "RECENT MODEL OK");
    }

    // RL-2 (DEC-076): a stored lesson is read back before anything opens; offline, nothing is drawn.
    private static int RunAssertReplayPrepare()
    {
        var failures = 0;
        var engine = ShellHost.PrepareReplay(EngineDrawing);
        failures = Check(engine.Drawing is { Width: 1280, Height: 720 } d && d.Program.StartsWith("step(1)\nsay('Fuel", StringComparison.Ordinal) && d.Program.EndsWith("down.')", StringComparison.Ordinal) && engine.Refusal is null,
            "a stored drawing reads back with its canvas and the exact program", failures);

        var older = ShellHost.PrepareReplay(OlderPlan);
        failures = Check(older.Drawing is null && older.Refusal == ShellHost.OlderLessonRefusal, $"an older row's VisualPlan is refused with a clear message ({older.Refusal})", failures);

        var none = ShellHost.PrepareReplay(null);
        var damaged = new[] { "drawing:", "drawing:1280x720", "drawing:1280x720\n   ", "drawing:0x720\nsay('x')", "drawing:99999x720\nsay('x')", "drawing:-5x720\nsay('x')", "drawing:12x\nsay('x')" }
            .Select(ShellHost.PrepareReplay).ToArray();
        failures = Check(none.Drawing is null && none.Refusal == "this turn has no stored lesson to replay"
            && damaged.All(r => r.Drawing is null && r.Refusal!.Contains("damaged", StringComparison.Ordinal)),
            "no stored lesson, and a drawing with no program or a missing, zero, negative or oversized canvas, are refused with a reason", failures);

        var refused = false;
        try
        {
            StoredDrawing.Store(0, 720, "say('x')");
        }
        catch (ArgumentOutOfRangeException)
        {
            refused = true;
        }

        failures = Check(refused && StoredDrawing.Store(3840, 2160, "say('x')") == "drawing:3840x2160\nsay('x')", "storing writes the marker and canvas on the first line, and refuses a canvas that is not one", failures);
        return Summarize(failures, "REPLAY PREPARE OK");
    }

    // RL-3 (DEC-076): a stored drawing replays on a stage with no model, against the fake page SH-29 uses (offline).
    private static async Task<int> RunAssertReplayAsync()
    {
        var failures = 0;
        var monitor = new MonitorInfo(1, "\\\\.\\DISPLAY1", new PixelRect(0, 0, 1920, 1080), true);
        var opened = new List<FakePage>();
        Task<IDrawStageSurface> Open(MonitorInfo m)
        {
            var page = new FakePage();
            opened.Add(page);
            return Task.FromResult<IDrawStageSurface>(page);
        }

        var said = new List<string>();
        Task Speak(string text, CancellationToken token)
        {
            said.Add(text);
            return Task.Delay(5, token);
        }

        var wait = TimeSpan.FromSeconds(10);
        var result = await ShellHost.ReplayAsync(EngineDrawing, Open, monitor, Speak, wait, TimeSpan.Zero, CancellationToken.None);
        var page = opened.Single();
        failures = Check(result.Played && result.Lines == 2 && page.Begins.Single() == (1280, 720, "speech") && page.LastFeedDone && page.Types.Count(t => t == "feed") == 1,
            $"the stored drawing begins at its own canvas, paced by speech, and is fed whole ({result.Said})", failures);
        failures = Check(said.SequenceEqual(new[] { "Fuel and air are drawn in.", "The spark pushes the piston down." }) && page.Count("lineStarted") == 2 && page.Count("lineEnded") == 2,
            "every line is spoken again, in order, with lineStarted / lineEnded", failures);
        failures = Check(page.Disposed, "the stage window is closed when the replay ends", failures);

        var silent = await ShellHost.ReplayAsync(EngineDrawing, Open, monitor, null, wait, TimeSpan.Zero, CancellationToken.None);
        failures = Check(silent.Played && opened.Count == 2 && opened[1].Begins.Single().Pace == "estimate" && opened[1].Disposed, "with no voice the page paces by its own estimate", failures);

        var refusedOlder = await ShellHost.ReplayAsync(OlderPlan, Open, monitor, Speak, wait, TimeSpan.Zero, CancellationToken.None);
        var refusedNone = await ShellHost.ReplayAsync(null, Open, monitor, Speak, wait, TimeSpan.Zero, CancellationToken.None);
        failures = Check(!refusedOlder.Played && refusedOlder.Said == ShellHost.OlderLessonRefusal && !refusedNone.Played && opened.Count == 2,
            $"an older (cards) lesson or no lesson opens no window and says why ({refusedOlder.Said})", failures);

        // The close button during the hold ends the replay at once; a page that never finishes stops it, window closed.
        var holding = ShellHost.ReplayAsync(EngineDrawing, Open, monitor, null, wait, TimeSpan.FromMinutes(5), CancellationToken.None);
        await Task.Delay(100);
        opened[2].Send("{\"type\":\"close\"}");
        var closedEarly = await holding.WaitAsync(TimeSpan.FromSeconds(5));
        var stuck = ShellHost.ReplayAsync(EngineDrawing, m => { var p = new FakePage { NeverFinish = true }; opened.Add(p); return Task.FromResult<IDrawStageSurface>(p); },
            monitor, null, TimeSpan.FromMilliseconds(300), TimeSpan.Zero, CancellationToken.None);
        var stopped = await stuck.WaitAsync(TimeSpan.FromSeconds(5));
        failures = Check(closedEarly.Played && opened[2].Disposed && !stopped.Played && stopped.Said.Contains("did not finish", StringComparison.Ordinal) && opened[3].Disposed,
            $"the close button ends the hold at once; a drawing that never finishes stops the replay ({stopped.Said}); each window closed", failures);
        return Summarize(failures, "REPLAY OK");
    }

    // RL-4 (DEC-060): the real Recent lessons window from the tray, over a temp store, with a stand-in replay.
    private static int RunAssertRecentWindow()
    {
        var failures = 0;
        var dir = TempFolder("recent-window");
        var results = new List<(bool Ok, string Label)>();
        try
        {
            SeededStore(dir, out var ids).Dispose();
            using var keys = new KeyRing(new EmptyStore(), _ => "made-up-not-a-key");
            var replayed = new List<string>();
            var gate = new TaskCompletionSource();
            RunSta(() =>
            {
                using var tray = new TrayApp(async (stop, hookLive, _) => { hookLive(); try { await Task.Delay(Timeout.Infinite, stop); } catch (OperationCanceledException) { } return 0; },
                    keys, TempFolder("recent-log"), null,
                    recent: new RecentLessonsSource(() => NotesStore.Open(Path.Combine(dir, "notes.db")), async (plan, _) =>
                    {
                        replayed.Add(plan);
                        await gate.Task;
                        return "replayed (stand-in)";
                    }));
                results.Add((tray.MenuTexts.Contains("Recent lessons…"), "the tray menu has Recent lessons…"));

                tray.OpenRecent();
                var window = tray.OpenRecentLessons!;
                Application.DoEvents();
                results.Add((window.Visible && window.RowTexts.Count == 5 && window.RowTexts[2].Contains("how does an engine work? | Lesson: Fuel and air are drawn in.", StringComparison.Ordinal),
                    $"the window lists the 5 saved turns, newest first ({window.RowTexts.Count})"));
                tray.OpenRecent();
                results.Add((Application.OpenForms.OfType<RecentLessons>().Count() == 1, "opening it again brings the same window forward"));

                window.Select(0);
                Application.DoEvents();
                results.Add((!window.ReplayEnabled && window.DetailText.Contains("older version", StringComparison.Ordinal), "Replay is off for an older (cards) lesson, and its details say why"));
                window.Select(1);
                Application.DoEvents();
                results.Add((!window.ReplayEnabled, "Replay is off for a lesson that was not stored"));
                window.Select(2);
                Application.DoEvents();
                results.Add((window.ReplayEnabled && window.DetailText.Contains("Asked: how does an engine work?", StringComparison.Ordinal), "selecting the stored drawing shows its details and turns Replay on"));

                var playing = window.ReplaySelectedAsync();
                Application.DoEvents();
                results.Add((replayed.Count == 1 && replayed[0] == EngineDrawing && tray.TriggerPaused && !window.ReplayEnabled,
                    "Replay hands over the exact stored drawing; while it plays Ctrl+Alt is paused and Replay is off"));
                gate.SetResult();
                while (!playing.IsCompleted)
                {
                    Application.DoEvents();
                    Thread.Sleep(10);
                }

                Application.DoEvents();
                results.Add((!tray.TriggerPaused && window.StatusText == "replayed (stand-in)" && window.ReplayEnabled, $"after the replay: Ctrl+Alt asks again, and the window says how it went ({window.StatusText})"));

                window.Select(0);
                window.DeleteSelected();
                Application.DoEvents();
                results.Add((window.RowTexts.Count == 4 && window.StatusText.StartsWith("Deleted the turn", StringComparison.Ordinal), "Delete removes the selected turn from the list and the store"));
                window.ClearAll();
                Application.DoEvents();
                results.Add((window.RowTexts.Count == 0 && window.StatusText == "Cleared 4 saved turn(s).", $"Clear all empties it ({window.StatusText})"));
                window.Close();
                Application.DoEvents();
                results.Add((tray.OpenRecentLessons is null, "closing the window lets it be opened fresh next time"));
                tray.Quit();
            });

            using (var check = NotesStore.Open(Path.Combine(dir, "notes.db")))
            {
                results.Add((check.Count() == 0, "the store itself is empty after Clear all"));
            }
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        foreach (var (ok, label) in results)
        {
            failures = Check(ok, label, failures);
        }

        return Summarize(failures, "RECENT WINDOW OK");
    }
}
