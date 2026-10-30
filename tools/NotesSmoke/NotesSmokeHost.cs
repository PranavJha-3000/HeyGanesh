using System.Text.RegularExpressions;
using HeyGanesh.Notes;

namespace HeyGanesh.Tools.NotesSmoke;

/// <summary>
/// Verification host for the notes store (DEC-058). Every row is offline and uses a database in a fresh temporary
/// folder, deleted afterwards. Exit codes: 0 pass, 1 failed check, 2 usage.
/// </summary>
internal static class NotesSmokeHost
{
    public static int Run(string[] args)
    {
        var mode = args.Length > 0 ? args[0] : "--help";
        Console.WriteLine("Notes store verification host (winsqlite3, temporary databases, no key)");
        Console.WriteLine();
        try
        {
            return mode switch
            {
                "--assert-source" => RunAssertSource(),
                "--assert-store" => RunAssertStore(),
                "--assert-migrate" => RunAssertMigrate(),
                "--assert-concurrent" => RunAssertConcurrent(),
                "--assert-corrupt" => RunAssertCorrupt(),
                "--assert-retention" => RunAssertRetention(),
                "--help" or "-h" or "/?" => PrintUsage(),
                _ => Usage(mode),
            };
        }
        catch (Exception ex)
        {
            Console.WriteLine($"UNEXPECTED: {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static int Usage(string bad)
    {
        Console.WriteLine($"unknown argument '{bad}' (exit 2)");
        return 2;
    }

    private static int PrintUsage()
    {
        Console.WriteLine("Usage: NotesSmoke <mode>");
        Console.WriteLine("  --assert-source      no package or project reference; winsqlite3 the only native library; no console, network or key use; exit 0");
        Console.WriteLine("  --assert-store       record, read back every field, newest first, get, delete, clear, values bound not spliced; exit 0");
        Console.WriteLine("  --assert-migrate     a new or empty file gets schema 1; reopening keeps the notes; a newer schema is refused and left untouched; exit 0");
        Console.WriteLine("  --assert-concurrent  two stores on one file write 2 x 200 notes at once with none lost; exit 0");
        Console.WriteLine("  --assert-corrupt     a file that is not a database is moved aside, kept, and a fresh store works; exit 0");
        Console.WriteLine("  --assert-retention   only the newest Keep notes stay; exit 0");
        return 0;
    }

    private static int Check(bool ok, string label, int failures)
    {
        Console.WriteLine($"  {(ok ? "PASS" : "FAIL")}  {label}");
        return ok ? failures : failures + 1;
    }

    private static int Summarize(int failures, string okLabel)
    {
        Console.WriteLine(failures == 0 ? $"{okLabel} (exit 0)" : $"FAILED: {failures} check(s) (exit 1)");
        return failures == 0 ? 0 : 1;
    }

    /// <summary>Runs <paramref name="body"/> with a fresh temporary folder, deleted afterwards.</summary>
    private static int InTemp(Func<string, int> body)
    {
        var dir = Path.Combine(Path.GetTempPath(), "heyganesh-notessmoke", Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(dir);
        try
        {
            return body(dir);
        }
        finally
        {
            try
            {
                Directory.Delete(dir, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static TurnNote Note(string question, int n = 0) => new()
    {
        At = new DateTimeOffset(2026, 10, 30, 9, 0, 0, TimeSpan.FromHours(5.5)).AddMinutes(n),
        Session = "s-test",
        Question = question,
        Leg = NoteLeg.Tutor,
        Answer = $"answer {n}",
        Outcome = "Played",
    };

    // NS-1: the module is self-contained: no package, no project reference, one native library.
    private static int RunAssertSource()
    {
        var root = AppContext.BaseDirectory;
        while (root is not null && !File.Exists(Path.Combine(root, "HeyGanesh.sln")))
        {
            root = Path.GetDirectoryName(root.TrimEnd(Path.DirectorySeparatorChar));
        }

        var dir = Path.Combine(root!, "src", "Foundation", "HeyGanesh.Notes");
        var csproj = File.ReadAllText(Path.Combine(dir, "HeyGanesh.Notes.csproj"));
        var sources = Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories)
            .Where(p => !p.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase)
                && !p.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.OrdinalIgnoreCase))
            .ToDictionary(p => Path.GetFileName(p), File.ReadAllText);
        var imports = sources.Values.SelectMany(s => Regex.Matches(s, @"DllImport\(([^,)]+)").Select(m => m.Groups[1].Value.Trim())).Distinct().ToArray();
        string[] needles = { "Console.", "HttpClient", "System.Net", "API_KEY", "LibraryImport", "dynamic" };
        var found = needles.Where(n => sources.Values.Any(s => s.Contains(n, StringComparison.Ordinal))).ToArray();
        var interpolatedSql = sources.Values.Any(s => Regex.IsMatch(s, @"\$""[^""]*(INSERT|UPDATE|DELETE)[^""]*\{note", RegexOptions.IgnoreCase));
        var version = NotesStore.LibraryVersion();

        var failures = 0;
        failures = Check(!csproj.Contains("<PackageReference", StringComparison.Ordinal) && !csproj.Contains("<ProjectReference", StringComparison.Ordinal), "no PackageReference and no ProjectReference", failures);
        failures = Check(imports.SequenceEqual(new[] { "Library" }) && sources["SqliteNative.cs"].Contains("\"winsqlite3\"", StringComparison.Ordinal)
            && sources.Where(s => s.Key != "SqliteNative.cs").All(s => !s.Value.Contains("DllImport", StringComparison.Ordinal)),
            "the only native library is Windows' winsqlite3, declared in SqliteNative.cs alone", failures);
        failures = Check(found.Length == 0 && !interpolatedSql, $"no console, network, key or dynamic use, and no note value spliced into SQL ({(found.Length == 0 ? "none" : string.Join(", ", found))})", failures);
        failures = Check(version is not null && version.StartsWith("3.", StringComparison.Ordinal), $"winsqlite3 loads on this Windows: SQLite {version ?? "unavailable"}", failures);
        return Summarize(failures, "SOURCE AUDIT OK");
    }

    // NS-2: what goes in comes out, field by field; listing, get, delete and clear.
    private static int RunAssertStore() => InTemp(dir =>
    {
        var failures = 0;
        using var store = NotesStore.Open(Path.Combine(dir, "notes.db"));
        failures = Check(store.Count() == 0 && store.Recent(10).Count == 0 && store.MovedAside is null, "a new store is empty and nothing was moved aside", failures);

        const string Tricky = "what's '); DROP TABLE turns;-- and \"quotes\", ünïcode, 日本語, emoji 🐘?";
        var lesson = new TurnNote
        {
            At = new DateTimeOffset(2026, 10, 30, 14, 5, 6, TimeSpan.FromHours(5.5)),
            Session = "s-1",
            Question = Tricky,
            Leg = NoteLeg.Lesson,
            Route = "lesson 3D four-stroke-engine",
            VerdictSource = "ElementSelection",
            Found = true,
            Element = "Save button",
            Confidence = "High",
            LessonTitle = "How a four-stroke engine works",
            LessonPlan = "{\"schema\":\"heyganesh.visualplan/1.2\",\"title\":\"with \\\"quotes\\\"\"}",
            Picks = new[] { "engine.connecting-rod", "engine.piston" },
            Outcome = "NoPick",
        };
        var id = store.Record(lesson);
        var back = store.Get(id);
        failures = Check(back is not null && back with { Id = 0, Picks = lesson.Picks } == lesson && back.Picks.SequenceEqual(lesson.Picks) && back.At == lesson.At,
            "every field reads back exactly, the time as the same instant, the picks in order", failures);
        failures = Check(store.Count() == 1, "the injection-shaped question is stored as text and the table still exists (values are bound, never spliced)", failures);

        var tutor = store.Record(Note("why is the sky blue?", 1) with { Found = false, VerdictSource = "Vision" });
        var highlight = store.Record(Note("where is Save?", 2) with { Leg = NoteLeg.Highlight, Answer = null });
        var recent = store.Recent(10);
        failures = Check(recent.Select(r => r.Id).SequenceEqual(new[] { highlight, tutor, id }) && recent[1].Found == false && recent[0].Answer is null && recent[0].Leg == NoteLeg.Highlight,
            "Recent lists newest first; false and null read back as false and null", failures);
        failures = Check(store.Recent(2).Count == 2 && store.Get(9999) is null, "Recent honours its count; an unknown id is null", failures);

        failures = Check(store.Delete(tutor) && !store.Delete(tutor) && store.Count() == 2, "Delete removes one note, and says so only the first time", failures);
        failures = Check(store.Clear() == 2 && store.Count() == 0, "Clear removes all and returns how many", failures);

        var refused = false;
        try
        {
            store.Record(Note(" "));
        }
        catch (ArgumentException)
        {
            refused = true;
        }

        failures = Check(refused && store.Count() == 0, "a note with no question is refused", failures);
        return Summarize(failures, "STORE OK");
    });

    // NS-3: schema versions: new, empty, reopened, and newer.
    private static int RunAssertMigrate() => InTemp(dir =>
    {
        var failures = 0;
        var path = Path.Combine(dir, "notes.db");
        File.WriteAllBytes(path, Array.Empty<byte>());
        long id;
        using (var store = NotesStore.Open(path))
        {
            id = store.Record(Note("kept across reopening"));
            failures = Check(store.MovedAside is null, "an empty file becomes a database in place (not moved aside)", failures);
        }

        using (var again = NotesStore.Open(path))
        {
            failures = Check(again.Get(id)?.Question == "kept across reopening" && again.Count() == 1, "reopening keeps the notes and does not migrate twice", failures);
        }

        failures = Check(UserVersion(path) == NotesStore.SchemaVersion, $"the file carries schema {NotesStore.SchemaVersion} in user_version", failures);

        var newer = Path.Combine(dir, "newer.db");
        using (var make = NotesStore.Open(newer))
        {
        }

        SetUserVersion(newer, 99);
        var before = File.ReadAllBytes(newer);
        var message = string.Empty;
        try
        {
            using var refused = NotesStore.Open(newer);
        }
        catch (NotesException ex)
        {
            message = ex.Message;
        }

        failures = Check(message.Contains("newer Hey Ganesh (schema 99)", StringComparison.Ordinal) && File.ReadAllBytes(newer).SequenceEqual(before)
            && !Directory.GetFiles(dir, "newer.db.broken-*").Any(),
            $"a newer schema is refused, and the file is left untouched, not moved aside ({message})", failures);
        return Summarize(failures, "MIGRATE OK");
    });

    // NS-4: the tray and the console exe can both hold the file; WAL and the busy timeout keep every write.
    private static int RunAssertConcurrent() => InTemp(dir =>
    {
        var failures = 0;
        var path = Path.Combine(dir, "notes.db");
        using var first = NotesStore.Open(path);
        using var second = NotesStore.Open(path);
        var errors = 0;
        void Write(NotesStore store, string who)
        {
            for (var i = 0; i < 200; i++)
            {
                try
                {
                    store.Record(Note($"{who} {i}", i));
                }
                catch (NotesException)
                {
                    Interlocked.Increment(ref errors);
                }
            }
        }

        var clock = System.Diagnostics.Stopwatch.StartNew();
        Parallel.Invoke(() => Write(first, "a"), () => Write(second, "b"), () => Write(first, "c"));
        failures = Check(errors == 0 && first.Count() == 600 && second.Count() == 600,
            $"two connections, three writers, 600 notes at once: none lost, none refused ({first.Count()} stored, {errors} errors, {clock.ElapsedMilliseconds} ms)", failures);
        var questions = second.Recent(600).Select(n => n.Question).ToHashSet();
        failures = Check(new[] { "a", "b", "c" }.All(w => Enumerable.Range(0, 200).All(i => questions.Contains($"{w} {i}"))), "every writer's 200 notes are there, readable from the other connection", failures);
        failures = Check(File.Exists(path + "-wal"), "the database runs in WAL mode (a -wal file beside it)", failures);
        return Summarize(failures, "CONCURRENT OK");
    });

    // NS-5: an unreadable file is kept, moved aside, and replaced by a working store.
    private static int RunAssertCorrupt() => InTemp(dir =>
    {
        var failures = 0;
        var path = Path.Combine(dir, "notes.db");
        var garbage = System.Text.Encoding.ASCII.GetBytes(new string('x', 8192));
        File.WriteAllBytes(path, garbage);
        using var store = NotesStore.Open(path);
        var aside = store.MovedAside;
        failures = Check(aside is not null && File.Exists(aside) && File.ReadAllBytes(aside).SequenceEqual(garbage) && Path.GetFileName(aside).StartsWith("notes.db.broken-", StringComparison.Ordinal),
            $"the unreadable file was moved aside intact ({(aside is null ? "not moved" : Path.GetFileName(aside))})", failures);
        var id = store.Record(Note("after the move"));
        failures = Check(store.Get(id)?.Question == "after the move" && store.Count() == 1, "a fresh store works in its place", failures);
        return Summarize(failures, "CORRUPT OK");
    });

    // NS-6: retention: the oldest notes go once there are more than Keep.
    private static int RunAssertRetention() => InTemp(dir =>
    {
        var failures = 0;
        using var store = NotesStore.Open(Path.Combine(dir, "notes.db"), new NotesOptions { Keep = 5 });
        var ids = Enumerable.Range(0, 8).Select(i => store.Record(Note($"q{i}", i))).ToArray();
        var kept = store.Recent(100);
        failures = Check(store.Count() == 5 && kept.Select(n => n.Id).SequenceEqual(ids.Skip(3).Reverse()), $"8 notes with Keep 5 -> the newest 5 stay ({string.Join(", ", kept.Select(n => n.Question))})", failures);
        failures = Check(new NotesOptions().Keep == 1000, "the default keeps the newest 1,000 (the plan's placeholder)", failures);
        var refused = false;
        try
        {
            NotesStore.Open(Path.Combine(dir, "other.db"), new NotesOptions { Keep = 0 }).Dispose();
        }
        catch (ArgumentOutOfRangeException)
        {
            refused = true;
        }

        failures = Check(refused, "Keep 0 is refused", failures);
        return Summarize(failures, "RETENTION OK");
    });

    private static long UserVersion(string path) => Pragma(path, "PRAGMA user_version");

    private static void SetUserVersion(string path, int version) => Pragma(path, $"PRAGMA user_version = {version}");

    /// <summary>Runs one pragma on its own connection, through the same native library the store uses.</summary>
    private static long Pragma(string path, string sql)
    {
        var name = System.Text.Encoding.UTF8.GetBytes(path + "\0");
        if (TestSqlite.sqlite3_open_v2(name, out var db, 0x2, IntPtr.Zero) != 0)
        {
            throw new InvalidOperationException("test connection failed");
        }

        try
        {
            var bytes = System.Text.Encoding.UTF8.GetBytes(sql + "\0");
            TestSqlite.sqlite3_prepare_v2(db, bytes, bytes.Length, out var stmt, IntPtr.Zero);
            long value = 0;
            if (TestSqlite.sqlite3_step(stmt) == 100)
            {
                value = TestSqlite.sqlite3_column_int64(stmt, 0);
            }

            TestSqlite.sqlite3_finalize(stmt);
            return value;
        }
        finally
        {
            TestSqlite.sqlite3_close_v2(db);
        }
    }

    /// <summary>The host's own minimal binding, so the checks read the file independently of the store's code.</summary>
    private static class TestSqlite
    {
        [System.Runtime.InteropServices.DllImport("winsqlite3")]
        internal static extern int sqlite3_open_v2(byte[] filename, out IntPtr db, int flags, IntPtr vfs);

        [System.Runtime.InteropServices.DllImport("winsqlite3")]
        internal static extern int sqlite3_close_v2(IntPtr db);

        [System.Runtime.InteropServices.DllImport("winsqlite3")]
        internal static extern int sqlite3_prepare_v2(IntPtr db, byte[] sql, int bytes, out IntPtr stmt, IntPtr tail);

        [System.Runtime.InteropServices.DllImport("winsqlite3")]
        internal static extern int sqlite3_step(IntPtr stmt);

        [System.Runtime.InteropServices.DllImport("winsqlite3")]
        internal static extern long sqlite3_column_int64(IntPtr stmt, int column);

        [System.Runtime.InteropServices.DllImport("winsqlite3")]
        internal static extern int sqlite3_finalize(IntPtr stmt);
    }
}
