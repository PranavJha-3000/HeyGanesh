using HeyGanesh.Abstractions;
using HeyGanesh.App;
using HeyGanesh.LessonSession;
using HeyGanesh.LessonSurface;
using HeyGanesh.Notes;
using HeyGanesh.PartLibrary.Models;
using HeyGanesh.Security;
using HeyGanesh.Tray;
using HeyGanesh.VisualLessonEngine;
using HeyGanesh.VisualLessonEngine.Runtime;

namespace HeyGanesh.Tools.TraySmoke;

internal static partial class TraySmokeHost
{
    private static readonly PixelRect Desktop = new(0, 0, 1920, 1080);

    private static string LessonFixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "fixtures", name))
            .Replace("\"engine-test\"", $"\"{FourStrokeEngine.Id}\"", StringComparison.Ordinal);

    /// <summary>A temp store holding a highlight, a Tutor turn, a lesson with its plan and one without, oldest first.</summary>
    private static NotesStore SeededStore(string dir, out long[] ids)
    {
        var store = NotesStore.Open(Path.Combine(dir, "notes.db"));
        var at = new DateTimeOffset(2026, 10, 31, 9, 0, 0, TimeSpan.FromHours(5.5));
        ids = new[]
        {
            store.Record(new TurnNote { At = at, Session = "s", Question = "where is Save?", Leg = NoteLeg.Highlight, VerdictSource = "ElementSelection", Found = true, Element = "Save button", Confidence = "High", Outcome = "the highlight was the answer" }),
            store.Record(new TurnNote { At = at.AddMinutes(1), Session = "s", Question = "what is entropy?", Leg = NoteLeg.Tutor, Found = false, Answer = "Entropy measures how many ways a system's parts can be arranged. " + new string('x', 80), Outcome = "explained" }),
            store.Record(new TurnNote { At = at.AddMinutes(2), Session = "s", Question = "how does an engine work?", Leg = NoteLeg.Lesson, Found = false, LessonTitle = "How a four-stroke engine works", LessonPlan = LessonFixture("engine-3d.json"), Picks = new[] { "engine.piston" }, Outcome = "Played, 1 follow-up(s), ended NoPick" }),
            store.Record(new TurnNote { At = at.AddMinutes(3), Session = "s", Question = "draw the chain rule", Leg = NoteLeg.Lesson, Found = false, LessonTitle = "The chain rule", Outcome = "skipped: no lesson model" }),
        };
        return store;
    }

    // RL-1 (DEC-060): what the Recent lessons window lists and says, offline.
    private static int RunAssertRecentModel()
    {
        var failures = 0;
        var dir = TempFolder("recent-model");
        try
        {
            using var model = new RecentLessonsModel(SeededStore(dir, out var ids));
            var rows = model.Rows();
            failures = Check(rows.Select(r => r.Id).SequenceEqual(ids.Reverse()), "the newest turn is listed first", failures);
            failures = Check(rows[3].Taught == "Shown: Save button" && rows[2].Taught.StartsWith("Explained: Entropy measures", StringComparison.Ordinal) && rows[2].Taught.EndsWith("…", StringComparison.Ordinal)
                && rows[2].Taught.Length <= 72 && rows[1].Taught == "Lesson: How a four-stroke engine works" && rows[0].Taught == "Lesson: The chain rule",
                $"each row says what was taught, a long answer cut short ({string.Join(" / ", rows.Select(r => r.Taught))})", failures);
            failures = Check(rows.Select(r => r.CanReplay).SequenceEqual(new[] { false, true, false, false }), "only a lesson whose plan was stored can be replayed", failures);

            var lesson = model.Detail(ids[2]);
            var tutor = model.Detail(ids[1]);
            failures = Check(lesson.Contains("Asked: how does an engine work?", StringComparison.Ordinal) && lesson.Contains("Replay draws and speaks it again, with no model call.", StringComparison.Ordinal)
                && lesson.Contains("Parts you clicked: engine.piston", StringComparison.Ordinal) && !lesson.Contains("\"schema\"", StringComparison.Ordinal),
                "a lesson's details say what was asked, that it can be replayed, and the parts clicked, never the plan's JSON", failures);
            failures = Check(tutor.Contains("Entropy measures how many ways", StringComparison.Ordinal) && tutor.Contains(new string('x', 80), StringComparison.Ordinal) && tutor.Contains("On screen: nothing matched", StringComparison.Ordinal),
                "a Tutor turn's details hold the whole answer", failures);
            failures = Check(model.Detail(ids[3]).Contains("This lesson was not stored, so it cannot be replayed.", StringComparison.Ordinal) && model.PlanJson(ids[2]) == LessonFixture("engine-3d.json"),
                "an unstored lesson says it cannot be replayed; a stored one hands back its exact plan", failures);

            failures = Check(model.Delete(ids[1]) && !model.Delete(ids[1]) && model.Rows().Count == 3 && model.Detail(ids[1]) == string.Empty, "Delete removes one turn", failures);
            failures = Check(model.Clear() == 3 && model.Rows().Count == 0, "Clear all removes the rest", failures);
        }
        finally
        {
            Directory.Delete(dir, recursive: true);
        }

        return Summarize(failures, "RECENT MODEL OK");
    }

    // RL-2 (DEC-060): a stored plan is validated again before it may play; offline, nothing is drawn.
    private static int RunAssertReplayPrepare()
    {
        var failures = 0;
        var engine = ShellHost.PrepareReplay(LessonFixture("engine-3d.json"), Desktop);
        failures = Check(engine.Lesson is { Outcome: LessonOutcome.Valid } && engine.Lesson.Plan!.Surfaces.Any(s => s.Kind == SurfaceKind.Scene3d) && engine.Refusal is null,
            $"a stored 3D lesson validates again with the scene on ({engine.Lesson?.Outcome})", failures);
        var jet = ShellHost.PrepareReplay(LessonFixture("f35b-schematic.json"), Desktop);
        failures = Check(jet.Lesson is { Outcome: LessonOutcome.Valid } && jet.Lesson.Plan!.Schematics.Count == 1, "a stored lesson with its own schematic validates again, schematic kept", failures);
        var chain = ShellHost.PrepareReplay(LessonFixture("chain-rule.json"), Desktop);
        failures = Check(chain.Lesson is { Outcome: LessonOutcome.PartiallyDegraded } && chain.Lesson.Issues.Any(i => i.Code == LessonIssueCodes.MarkUngrounded),
            $"a lesson that marked the screen still plays; with no screen now, only its marks are dropped ({chain.Lesson?.Outcome})", failures);

        var none = ShellHost.PrepareReplay(null, Desktop);
        var garbage = ShellHost.PrepareReplay("not a lesson", Desktop);
        var newer = ShellHost.PrepareReplay(LessonFixture("chain-rule.json").Replace("heyganesh.visualplan/1", "heyganesh.visualplan/9", StringComparison.Ordinal), Desktop);
        failures = Check(none.Lesson is null && none.Refusal == "this turn has no stored lesson to replay"
            && garbage.Lesson is null && garbage.Refusal!.StartsWith("the stored lesson can no longer be played (", StringComparison.Ordinal)
            && newer.Lesson is null && newer.Refusal!.Contains("can no longer be played", StringComparison.Ordinal),
            $"no plan, text that is not a lesson, and an unknown schema are refused with a reason ({garbage.Refusal})", failures);
        return Summarize(failures, "REPLAY PREPARE OK");
    }

    // RL-3 (DEC-060): a stored lesson replays on the real cards and scene window, with no model; a bad one opens nothing.
    private static async Task<int> RunAssertReplayAsync()
    {
        var failures = 0;
        var speaker = new SilentRecorder();
        var host = new CountingHost(new CardSurfaceHost());
        var engine = LessonFixture("engine-3d.json");
        var result = await ShellHost.ReplayAsync(engine, host, speaker, HeyGanesh.App.NativeFns.VirtualDesktop(), CancellationToken.None);
        var says = VisualPlanParser.Parse(engine).Plan!.Beats.Select(b => b.Say).ToArray();
        failures = Check(result.Played && result.Report is { Status: PlaybackStatus.Completed } && result.Report.Beats.Count == says.Length,
            $"the stored engine lesson replays on the real cards to the end ({result.Said})", failures);
        failures = Check(speaker.Said.SequenceEqual(says), $"every beat is spoken again, in order ({speaker.Said.Count} of {says.Length})", failures);
        failures = Check(host.Opened == 1 && host.Closed == 1, "the cards are opened once and closed when the replay ends", failures);

        var refusedHost = new CountingHost(new CardSurfaceHost());
        var refused = await ShellHost.ReplayAsync("not a lesson", refusedHost, new SilentRecorder(), HeyGanesh.App.NativeFns.VirtualDesktop(), CancellationToken.None);
        failures = Check(!refused.Played && refused.Report is null && refusedHost.Opened == 0, $"a plan that cannot play opens no window ({refused.Said})", failures);
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
                results.Add((window.Visible && window.RowTexts.Count == 4 && window.RowTexts[1].Contains("how does an engine work? | Lesson: How a four-stroke engine works", StringComparison.Ordinal),
                    $"the window lists the 4 saved turns, newest first ({window.RowTexts.Count})"));
                tray.OpenRecent();
                results.Add((Application.OpenForms.OfType<RecentLessons>().Count() == 1, "opening it again brings the same window forward"));

                window.Select(0);
                Application.DoEvents();
                results.Add((!window.ReplayEnabled, "Replay is off for a lesson that was not stored"));
                window.Select(1);
                Application.DoEvents();
                results.Add((window.ReplayEnabled && window.DetailText.Contains("Asked: how does an engine work?", StringComparison.Ordinal), "selecting the stored lesson shows its details and turns Replay on"));

                var playing = window.ReplaySelectedAsync();
                Application.DoEvents();
                results.Add((replayed.Count == 1 && replayed[0] == LessonFixture("engine-3d.json") && tray.TriggerPaused && !window.ReplayEnabled,
                    "Replay hands over the exact stored plan; while it plays Ctrl+Alt is paused and Replay is off"));
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
                results.Add((window.RowTexts.Count == 3 && window.StatusText.StartsWith("Deleted the turn", StringComparison.Ordinal), "Delete removes the selected turn from the list and the store"));
                window.ClearAll();
                Application.DoEvents();
                results.Add((window.RowTexts.Count == 0 && window.StatusText == "Cleared 3 saved turn(s).", $"Clear all empties it ({window.StatusText})"));
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

    private sealed class SilentRecorder : ILessonSpeaker
    {
        public List<string> Said { get; } = new();

        public Task SpeakAsync(string text, CancellationToken cancellationToken)
        {
            Said.Add(text);
            return Task.Delay(150, cancellationToken);
        }
    }

    /// <summary>Counts how often the real cards are opened and closed around a replay.</summary>
    private sealed class CountingHost : ILessonCanvasHost
    {
        private readonly ILessonCanvasHost _inner;

        public CountingHost(ILessonCanvasHost inner) => _inner = inner;

        public int Opened { get; private set; }

        public int Closed { get; private set; }

        public async Task<LessonCanvasLease> OpenAsync(CancellationToken cancellationToken)
        {
            Opened++;
            var lease = await _inner.OpenAsync(cancellationToken);
            return lease with { Owner = new Counted(lease.Owner, () => Closed++) };
        }

        private sealed class Counted : IAsyncDisposable
        {
            private readonly IAsyncDisposable _inner;
            private readonly Action _closed;

            public Counted(IAsyncDisposable inner, Action closed)
            {
                _inner = inner;
                _closed = closed;
            }

            public async ValueTask DisposeAsync()
            {
                _closed();
                await _inner.DisposeAsync();
            }
        }
    }
}
