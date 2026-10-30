using System.Globalization;
using System.Text;
using System.Text.Json;
using static HeyGanesh.Notes.SqliteNative;

namespace HeyGanesh.Notes;

/// <summary>
/// The notes database (DEC-058): one row per turn in <c>notes.db</c>, through Windows' own SQLite.
/// </summary>
/// <remarks>
/// <para>
/// One connection per store, opened full-mutex, and every call also holds <see cref="_gate"/>, so a store is safe
/// to share between threads. Two processes (the tray and the console exe) can each hold a store on the same file:
/// WAL journaling lets a reader and a writer run together, and the busy timeout makes a second writer wait instead
/// of failing.
/// </para>
/// <para>
/// The schema version lives in <c>PRAGMA user_version</c>. 0 is a new file and gets version 1. A version newer
/// than this code knows is refused, and the file is not touched, so an older build never damages a newer database.
/// A file that is not a database, or fails <c>quick_check</c>, is moved aside as <c>notes.db.broken-&lt;stamp&gt;</c>
/// (with its -wal and -shm files) and a fresh database starts. <see cref="MovedAside"/> says so; nothing is lost
/// silently.
/// </para>
/// <para>
/// Every value is bound as a parameter, never spliced into SQL. Nothing here logs, and no exception message holds a
/// note's text.
/// </para>
/// </remarks>
public sealed class NotesStore : IDisposable
{
    /// <summary>The schema this code writes.</summary>
    public const int SchemaVersion = 1;

    private const string Columns = "id, at, session, question, leg, route, verdict_source, found, element, confidence, answer, lesson_title, lesson_plan, picks, outcome";

    private readonly object _gate = new();
    private readonly NotesOptions _options;
    private IntPtr _db;

    private NotesStore(string path, IntPtr db, NotesOptions options, string? movedAside)
    {
        Path = path;
        _db = db;
        _options = options;
        MovedAside = movedAside;
    }

    public string Path { get; }

    /// <summary>Where an unreadable database was moved this time it was opened, or null.</summary>
    public string? MovedAside { get; }

    /// <summary><c>$HEYGANESH_DATA_DIR/notes.db</c>, else <c>%LOCALAPPDATA%/HeyGanesh/notes.db</c>.</summary>
    public static string DefaultPath()
    {
        var root = Environment.GetEnvironmentVariable("HEYGANESH_DATA_DIR");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HeyGanesh");
        }

        return System.IO.Path.Combine(root, "notes.db");
    }

    /// <summary>SQLite's version, or null when <c>winsqlite3.dll</c> cannot be loaded (the shell then runs without notes).</summary>
    public static string? LibraryVersion()
    {
        try
        {
            return FromUtf8Z(sqlite3_libversion());
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException)
        {
            return null;
        }
    }

    /// <summary>Opens or creates the database, migrating a new file to the current schema.</summary>
    /// <exception cref="NotesException">SQLite is unavailable, the schema is newer than this code, or the file cannot be opened.</exception>
    public static NotesStore Open(string path, NotesOptions? options = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(path);
        options ??= new NotesOptions();
        if (options.Keep < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Keep must be at least 1.");
        }

        if (LibraryVersion() is null)
        {
            throw new NotesException("winsqlite3.dll is not available on this Windows; notes are off.", -1);
        }

        path = System.IO.Path.GetFullPath(path);
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)!);

        string? movedAside = null;
        var db = TryOpenHealthy(path, options, out var why);
        if (db == IntPtr.Zero)
        {
            if (why is { Newer: true })
            {
                throw new NotesException($"notes.db was written by a newer Hey Ganesh (schema {why.Detail}); it was left untouched.", -1);
            }

            movedAside = MoveAside(path);
            db = TryOpenHealthy(path, options, out why);
            if (db == IntPtr.Zero)
            {
                throw new NotesException($"notes.db could not be opened even after moving the old file aside ({why?.Detail}).", why?.Code ?? -1);
            }
        }

        return new NotesStore(path, db, options, movedAside);
    }

    /// <summary>Records a turn and applies retention. Returns the new note's id.</summary>
    public long Record(TurnNote note)
    {
        ArgumentNullException.ThrowIfNull(note);
        ArgumentException.ThrowIfNullOrWhiteSpace(note.Question);
        lock (_gate)
        {
            Run("BEGIN IMMEDIATE");
            try
            {
                Run("INSERT INTO turns (at, session, question, leg, route, verdict_source, found, element, confidence, answer, lesson_title, lesson_plan, picks, outcome) " +
                    "VALUES (?1, ?2, ?3, ?4, ?5, ?6, ?7, ?8, ?9, ?10, ?11, ?12, ?13, ?14)",
                    note.At.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture), note.Session, note.Question, note.Leg.ToString(),
                    note.Route, note.VerdictSource, note.Found is { } f ? (f ? 1L : 0L) : null, note.Element, note.Confidence,
                    note.Answer, note.LessonTitle, note.LessonPlan, JsonSerializer.Serialize(note.Picks), note.Outcome);
                var id = sqlite3_last_insert_rowid(Db);
                Run("DELETE FROM turns WHERE id NOT IN (SELECT id FROM turns ORDER BY id DESC LIMIT ?1)", (long)_options.Keep);
                Run("COMMIT");
                return id;
            }
            catch
            {
                Run("ROLLBACK");
                throw;
            }
        }
    }

    /// <summary>The newest turns first, at most <paramref name="count"/>.</summary>
    public IReadOnlyList<TurnNote> Recent(int count)
    {
        lock (_gate)
        {
            return Query($"SELECT {Columns} FROM turns ORDER BY id DESC LIMIT ?1", (long)Math.Max(0, count));
        }
    }

    public TurnNote? Get(long id)
    {
        lock (_gate)
        {
            return Query($"SELECT {Columns} FROM turns WHERE id = ?1", id).FirstOrDefault();
        }
    }

    /// <summary>Deletes one turn; true when it existed.</summary>
    public bool Delete(long id)
    {
        lock (_gate)
        {
            Run("DELETE FROM turns WHERE id = ?1", id);
            return sqlite3_changes(Db) == 1;
        }
    }

    /// <summary>Deletes every turn; returns how many there were.</summary>
    public int Clear()
    {
        lock (_gate)
        {
            Run("DELETE FROM turns");
            return sqlite3_changes(Db);
        }
    }

    public int Count()
    {
        lock (_gate)
        {
            return (int)Scalar(Db, "SELECT COUNT(*) FROM turns");
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_db != IntPtr.Zero)
            {
                sqlite3_close_v2(_db);
                _db = IntPtr.Zero;
            }
        }
    }

    private IntPtr Db => _db != IntPtr.Zero ? _db : throw new ObjectDisposedException(nameof(NotesStore));

    // ---- opening, migrating, and moving a broken file aside -----------------------------------------------

    /// <summary>Why a file could not be used. <see cref="Newer"/>: written by newer code, so it is never moved aside.</summary>
    private sealed record OpenProblem(int Code, string Detail, bool Newer = false);

    /// <summary>Opens, checks and migrates. Zero with a reason when the file is unusable (or newer, which is never moved).</summary>
    private static IntPtr TryOpenHealthy(string path, NotesOptions options, out OpenProblem? why)
    {
        why = null;
        var rc = sqlite3_open_v2(Utf8Z(path), out var db, OpenReadWrite | OpenCreate | OpenFullMutex, IntPtr.Zero);
        if (rc != Ok)
        {
            why = new OpenProblem(rc, Message(db, rc));
            sqlite3_close_v2(db);
            return IntPtr.Zero;
        }

        try
        {
            sqlite3_busy_timeout(db, options.BusyTimeoutMs);
            var version = (int)Scalar(db, "PRAGMA user_version");
            if (version > SchemaVersion)
            {
                why = new OpenProblem(-1, version.ToString(CultureInfo.InvariantCulture), Newer: true);
                sqlite3_close_v2(db);
                return IntPtr.Zero;
            }

            var check = Text(db, "PRAGMA quick_check");
            if (!string.Equals(check, "ok", StringComparison.OrdinalIgnoreCase))
            {
                why = new OpenProblem(Corrupt, "quick_check did not return ok");
                sqlite3_close_v2(db);
                return IntPtr.Zero;
            }

            Exec(db, "PRAGMA journal_mode=WAL");
            if (version == 0)
            {
                Exec(db, "BEGIN IMMEDIATE");
                Exec(db, "CREATE TABLE IF NOT EXISTS turns (" +
                         "id INTEGER PRIMARY KEY AUTOINCREMENT, at TEXT NOT NULL, session TEXT NOT NULL, question TEXT NOT NULL, " +
                         "leg TEXT NOT NULL, route TEXT, verdict_source TEXT, found INTEGER, element TEXT, confidence TEXT, " +
                         "answer TEXT, lesson_title TEXT, lesson_plan TEXT, picks TEXT NOT NULL DEFAULT '[]', outcome TEXT NOT NULL)");
                Exec(db, "CREATE INDEX IF NOT EXISTS turns_session ON turns (session)");
                Exec(db, $"PRAGMA user_version = {SchemaVersion}");
                Exec(db, "COMMIT");
            }

            return db;
        }
        catch (NotesException ex) when (ex.Code is Corrupt or NotADatabase)
        {
            why = new OpenProblem(ex.Code, ex.Message);
            sqlite3_close_v2(db);
            return IntPtr.Zero;
        }
        catch
        {
            sqlite3_close_v2(db);
            throw;
        }
    }

    /// <summary>Renames the database and its -wal / -shm files to <c>.broken-&lt;stamp&gt;</c>; returns the new database name.</summary>
    private static string MoveAside(string path)
    {
        var stamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        var target = $"{path}.broken-{stamp}";
        for (var n = 2; File.Exists(target); n++)
        {
            target = $"{path}.broken-{stamp}-{n}";
        }

        File.Move(path, target);
        foreach (var suffix in new[] { "-wal", "-shm" })
        {
            if (File.Exists(path + suffix))
            {
                File.Move(path + suffix, target + suffix);
            }
        }

        return target;
    }

    // ---- statements ------------------------------------------------------------------------------------------

    private void Run(string sql, params object?[] args) => Execute(Db, sql, args, read: null);

    private List<TurnNote> Query(string sql, params object?[] args)
    {
        var notes = new List<TurnNote>();
        Execute(Db, sql, args, stmt => notes.Add(ReadNote(stmt)));
        return notes;
    }

    private static void Exec(IntPtr db, string sql) => Execute(db, sql, Array.Empty<object?>(), read: null);

    private static long Scalar(IntPtr db, string sql)
    {
        long value = 0;
        Execute(db, sql, Array.Empty<object?>(), stmt => value = sqlite3_column_int64(stmt, 0));
        return value;
    }

    private static string? Text(IntPtr db, string sql)
    {
        string? value = null;
        Execute(db, sql, Array.Empty<object?>(), stmt => value ??= ColumnText(stmt, 0));
        return value;
    }

    /// <summary>Prepares, binds each argument as ?1..?n (text, integer or null), steps to the end, finalizes.</summary>
    private static void Execute(IntPtr db, string sql, object?[] args, Action<IntPtr>? read)
    {
        var bytes = Utf8Z(sql);
        var rc = sqlite3_prepare_v2(db, bytes, bytes.Length, out var stmt, IntPtr.Zero);
        if (rc != Ok)
        {
            throw new NotesException($"SQLite could not prepare a statement: {Message(db, rc)}", rc);
        }

        try
        {
            for (var i = 0; i < args.Length; i++)
            {
                rc = args[i] switch
                {
                    null => sqlite3_bind_null(stmt, i + 1),
                    long n => sqlite3_bind_int64(stmt, i + 1, n),
                    string s => BindText(stmt, i + 1, s),
                    var other => throw new ArgumentException($"unsupported parameter type {other.GetType().Name}"),
                };
                if (rc != Ok)
                {
                    throw new NotesException($"SQLite could not bind parameter {i + 1}: {Message(db, rc)}", rc);
                }
            }

            while ((rc = sqlite3_step(stmt)) == Row)
            {
                read?.Invoke(stmt);
            }

            if (rc != Done)
            {
                throw new NotesException($"SQLite could not run a statement: {Message(db, rc)}", rc);
            }
        }
        finally
        {
            sqlite3_finalize(stmt);
        }
    }

    private static int BindText(IntPtr stmt, int index, string value)
    {
        var utf8 = Encoding.UTF8.GetBytes(value);
        return sqlite3_bind_text(stmt, index, utf8, utf8.Length, Transient);
    }

    private static string? ColumnText(IntPtr stmt, int column)
    {
        if (sqlite3_column_type(stmt, column) == TypeNull)
        {
            return null;
        }

        var text = sqlite3_column_text(stmt, column);
        var length = sqlite3_column_bytes(stmt, column);
        return text == IntPtr.Zero ? string.Empty : System.Runtime.InteropServices.Marshal.PtrToStringUTF8(text, length);
    }

    private static TurnNote ReadNote(IntPtr stmt) => new()
    {
        Id = sqlite3_column_int64(stmt, 0),
        At = DateTimeOffset.Parse(ColumnText(stmt, 1)!, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
        Session = ColumnText(stmt, 2)!,
        Question = ColumnText(stmt, 3)!,
        Leg = Enum.Parse<NoteLeg>(ColumnText(stmt, 4)!),
        Route = ColumnText(stmt, 5),
        VerdictSource = ColumnText(stmt, 6),
        Found = sqlite3_column_type(stmt, 7) == TypeNull ? null : sqlite3_column_int64(stmt, 7) != 0,
        Element = ColumnText(stmt, 8),
        Confidence = ColumnText(stmt, 9),
        Answer = ColumnText(stmt, 10),
        LessonTitle = ColumnText(stmt, 11),
        LessonPlan = ColumnText(stmt, 12),
        Picks = JsonSerializer.Deserialize<string[]>(ColumnText(stmt, 13) ?? "[]") ?? Array.Empty<string>(),
        Outcome = ColumnText(stmt, 14)!,
    };

    private static string Message(IntPtr db, int rc) => $"{FromUtf8Z(sqlite3_errmsg(db)) ?? "unknown error"} (code {rc})";
}
